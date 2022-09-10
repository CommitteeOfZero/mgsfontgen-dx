﻿using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using SharpDX.WIC;
using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;

namespace MgsFontGenDX
{
    public sealed class TextRenderer : RendererBase
    {
        private const int ColumnCount = 64;
        private const int NormalCellWidth = 48;
        private const int NormalCellHeight = 48;
        private const int OutlineCellWidth = 57;
        private const int OutlineCellHeight = 57;
        private const float Dpi = 96.0f;
        private const float GameWidthMultiplier = 1.5f;

        private int _cellWidth;
        private int _cellHeight;
        private Guid _formatGuid;
        private bool _drawingOutline;
        private TextFormat _textFormat;
        private Vector2 _baselineOrigin;
        private ImmutableDictionary<int, string> _puaCharacters;

        private OutlineRenderer _outlineRenderer;
        private byte[] _widths;
        private int _idxCurrent;

        private SolidColorBrush _whiteBrush;
        private SolidColorBrush _redBrush;

        public TextRenderer()
            : base()
        {
        }

        public Stream GenerateBitmapFont(string characters, ImmutableDictionary<int, string> puaCharacters, ImageFormat format,
            out byte[] widths, bool drawOutline, string fontFamily, int fontSize, int baselineOriginX, int baselineOriginY)
        {
            _cellWidth = drawOutline ? OutlineCellWidth : NormalCellWidth;
            _cellHeight = drawOutline ? OutlineCellHeight : NormalCellHeight;
            int rowCount = (int)Math.Ceiling((double)characters.Length / ColumnCount);
            int bitmapWidth = _cellWidth * ColumnCount;
            int bitmapHeight = 4 * (int)Math.Ceiling((double)_cellHeight * rowCount / 4);

            var bitmapProperties = new BitmapProperties1(DevicePixelFormat, Dpi, Dpi, BitmapOptions.Target);
            var containerGuid = format == ImageFormat.Png ? ContainerFormatGuids.Png : ContainerFormatGuids.Dds;
            using (var fontBitmap = new Bitmap1(DeviceContext, new Size2(bitmapWidth, bitmapHeight), bitmapProperties))
            {
                DrawCharacters(fontBitmap, characters, puaCharacters, drawOutline, 0, 0, fontFamily, fontSize, baselineOriginX, baselineOriginY);
                widths = _widths;
                return EncodeBitmap(fontBitmap, containerGuid);
            }
        }

        private void DrawCharacters(Bitmap1 target, string characters, ImmutableDictionary<int, string> puaCharacters,
            bool drawOutline, int offsetX, int offsetY, string fontFamily, int fontSize, int baselineOriginX, int baselineOriginY)
        {
            _drawingOutline = drawOutline;
            _puaCharacters = puaCharacters;
            _baselineOrigin = new Vector2(baselineOriginX, baselineOriginY);
            _widths = new byte[characters.Length];
            _idxCurrent = 0;

            DeviceContext.Target = target;
            DeviceContext.Transform = Matrix3x2.Identity;
            CreateBrushes();
            using (_textFormat = new TextFormat(DWriteFactory, fontFamily, FontWeight.Regular, FontStyle.Normal, FontStretch.Normal, fontSize))
            using (_outlineRenderer = new OutlineRenderer(DeviceContext))
            {
                _textFormat.WordWrapping = WordWrapping.NoWrap;

                DeviceContext.BeginDraw();
#if DEBUG
                DrawGridLines();
#endif

                DeviceContext.Transform = Matrix3x2.Translation(offsetX, offsetY);
                for (int i = 0; i < characters.Length; i += ColumnCount)
                {
                    int currentRowLength = Math.Min(ColumnCount, characters.Length - i);
                    string currentRow = characters.Substring(i, currentRowLength);

                    DrawRow(currentRow);

                    var transform = Matrix3x2.Multiply(DeviceContext.Transform, Matrix3x2.Translation(0, _cellHeight));
                    DeviceContext.Transform = transform;
                }

                DeviceContext.EndDraw();
            }

            DestroyBrushes();
            DeviceContext.Target = null;
        }

        private void CreateBrushes()
        {
            _whiteBrush = new SolidColorBrush(DeviceContext, Color.White);
            _redBrush = new SolidColorBrush(DeviceContext, Color.Red);
        }

        private void DestroyBrushes()
        {
            _whiteBrush.Dispose();
            _redBrush.Dispose();
        }

        private void DrawRow(string characters)
        {
            var old = DeviceContext.Transform;
            for (int i = 0; i < characters.Length; i++)
            {
                string ch;
                if (CharUnicodeInfo.GetUnicodeCategory(characters[i]) != UnicodeCategory.PrivateUse)
                {
                    ch = characters[i].ToString();
                }
                else
                {
                    ch = _puaCharacters[char.ConvertToUtf32(characters, i)];
                }

                using (var layout = new TextLayout(DWriteFactory, ch, _textFormat, _cellWidth, _cellHeight))
                {
                    DrawCharacter(ch, layout);
                }

                var transform = Matrix3x2.Multiply(DeviceContext.Transform, Matrix3x2.Translation(_cellWidth, 0));
                DeviceContext.Transform = transform;
            }

            DeviceContext.Transform = old;
        }

        private void DrawCharacter(string character, TextLayout layout)
        {
            var oldTransform = DeviceContext.Transform;

            int gameCellWidth = (int)(NormalCellWidth / GameWidthMultiplier);
            float charWidth = layout.Metrics.WidthIncludingTrailingWhitespace;
            int gameCharWidth = (int)Math.Ceiling(charWidth / GameWidthMultiplier) + 1;

            float widthScale = Math.Min((float)gameCellWidth / gameCharWidth, 1);
            var scale = Matrix3x2.Transformation(widthScale, 1.0f, 0.0f, 0.0f, 0.0f);
            var transform = Matrix3x2.Multiply(scale, DeviceContext.Transform);
            DeviceContext.Transform = transform;

            if (!_drawingOutline)
            {
                DeviceContext.DrawTextLayout(_baselineOrigin, layout, _whiteBrush);
            }
            else
            {
                layout.Draw(_outlineRenderer, _baselineOrigin.X, _baselineOrigin.Y);
            }

            _widths[_idxCurrent] = (byte)Math.Min(gameCharWidth, gameCellWidth);
            _idxCurrent++;

            DeviceContext.Transform = oldTransform;
        }

        private void DrawGridLines()
        {
            int rowCount = DeviceContext.PixelSize.Height / _cellHeight;
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < ColumnCount; col++)
                {
                    var bounds = new RectangleF(col * _cellWidth, row * _cellHeight, _cellWidth, _cellHeight);
                    DeviceContext.DrawRectangle(bounds, _redBrush);
                }
            }
        }

        private Bitmap1 DecodeImage(Stream imageStream)
        {
            using (var bitmapDecoder = new BitmapDecoder(WicFactory, imageStream, DecodeOptions.CacheOnDemand))
            using (var converter = new FormatConverter(WicFactory))
            {
                _formatGuid = bitmapDecoder.ContainerFormat;
                var frame = bitmapDecoder.GetFrame(0);
                converter.Initialize(frame, ImagePixelFormat);

                var props = new BitmapProperties1()
                {
                    BitmapOptions = BitmapOptions.Target,
                    PixelFormat = DevicePixelFormat
                };

                return SharpDX.Direct2D1.Bitmap1.FromWicBitmap(DeviceContext, converter, props);
            }
        }

        private Stream EncodeBitmap(Bitmap1 bitmap, Guid containerFormat)
        {
            using (var bitmapEncoder = new BitmapEncoder(WicFactory, containerFormat))
            {
                var memoryStream = new MemoryStream();
                bitmapEncoder.Initialize(memoryStream);
                using (var frameEncode = new BitmapFrameEncode(bitmapEncoder))
                {
                    frameEncode.Initialize();
                    var wicPixelFormat = ImagePixelFormat;
                    frameEncode.SetPixelFormat(ref wicPixelFormat);

                    var imageParams = new ImageParameters()
                    {
                        PixelFormat = DevicePixelFormat,
                        DpiX = Dpi,
                        DpiY = Dpi,
                        PixelWidth = bitmap.PixelSize.Width,
                        PixelHeight = bitmap.PixelSize.Height
                    };

                    WicImageEncoder.WriteFrame(bitmap, frameEncode, imageParams);

                    frameEncode.Commit();
                    bitmapEncoder.Commit();
                }

                memoryStream.Position = 0;
                return memoryStream;
            }
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }

    public enum ImageFormat
    {
        Png,
        Dds
    }
}
