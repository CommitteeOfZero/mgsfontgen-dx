using SharpDX;
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
        private const int CellWidth = 48;
        private const int CellHeight = 48;
        private const float Dpi = 96.0f;
        private const float WidthMultiplier = 1.5f;

        private Guid _formatGuid;
        private bool _drawOutline;
        private TextFormat _textFormat;
        private Vector2 _baselineOrigin;
        private ImmutableDictionary<int, string> _puaCharacters;

        private OutlineRenderer _outlineRenderer;
        private byte[] _widths;
        private int _idxCurrent;

        public TextRenderer()
            : base()
        {
        }

        public Stream GenerateBitmapFont(string characters, ImmutableDictionary<int, string> puaCharacters, ImageFormat format,
            out byte[] widths, bool drawOutline, string fontFamily, int fontSize, int baselineOriginX, int baselineOriginY)
        {
            int rowCount = (int)Math.Ceiling((double)characters.Length / ColumnCount);
            int bitmapWidth = CellWidth * ColumnCount;
            int bitmapHeight = CellHeight * rowCount;

            var bitmapProperties = new BitmapProperties1(Direct2DPixelFormat, Dpi, Dpi, BitmapOptions.Target);
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
            _drawOutline = drawOutline;
            _puaCharacters = puaCharacters;
            _baselineOrigin = new Vector2(baselineOriginX, baselineOriginY);
            _widths = new byte[characters.Length];
            _idxCurrent = 0;

            DeviceContext.Target = target;
            using (_textFormat = new TextFormat(DWriteFactory, fontFamily, FontWeight.Regular, FontStyle.Normal, FontStretch.Normal, fontSize))
            using (_outlineRenderer = new OutlineRenderer(DeviceContext, WhiteBrush))
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

                    var transform = Matrix3x2.Multiply(DeviceContext.Transform, Matrix3x2.Translation(0, CellHeight));
                    DeviceContext.Transform = transform;
                }

                DeviceContext.EndDraw();
            }

            DeviceContext.Target = null;
        }

        private void DrawRow(string characters)
        {
            var old = DeviceContext.Transform;
            for (int i = 0; i < characters.Length; i++)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(characters[i]) != UnicodeCategory.PrivateUse)
                {
                    DrawCharacter(characters[i].ToString());
                }
                else
                {
                    DrawCompoundCharacter(_puaCharacters[char.ConvertToUtf32(characters, i)]);
                }

                var transform = Matrix3x2.Multiply(DeviceContext.Transform, Matrix3x2.Translation(CellWidth, 0));
                DeviceContext.Transform = transform;
            }

            DeviceContext.Transform = old;
        }

        private void DrawCharacter(string character)
        {
            using (var layout = new TextLayout(DWriteFactory, character, _textFormat, CellWidth, CellHeight))
            {
                if (!_drawOutline)
                {
                    DeviceContext.DrawTextLayout(_baselineOrigin, layout, WhiteBrush);
                }
                else
                {
                    layout.Draw(_outlineRenderer, _baselineOrigin.X, _baselineOrigin.Y);
                }

                _widths[_idxCurrent] = Measure(character, layout);
                _idxCurrent++;
            }
        }

        private byte Measure(string character, TextLayout layout)
        {
            return  (byte)(Math.Ceiling(layout.Metrics.WidthIncludingTrailingWhitespace / WidthMultiplier) + 1);
        }

        private void DrawCompoundCharacter(string compoundCharacter)
        {

            var old = DeviceContext.Transform;
            if (NeedToScale(compoundCharacter))
            {
                var transform = Matrix3x2.Multiply(Matrix3x2.Transformation((float)1 / compoundCharacter.Length, 1.0f, 0.0f, 0.0f, 0.0f), DeviceContext.Transform);
                DeviceContext.Transform = transform;
            }

            DrawCharacter(compoundCharacter);
            DeviceContext.Transform = old;
        }

        private bool NeedToScale(string compoundCharacter)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(compoundCharacter[0]);
            return !(category == UnicodeCategory.ModifierLetter || category == UnicodeCategory.OtherNumber || category == UnicodeCategory.SpaceSeparator);
        }

        private void DrawGridLines()
        {
            int rowCount = DeviceContext.PixelSize.Height / CellHeight;
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < ColumnCount; col++)
                {
                    DeviceContext.DrawRectangle(new RectangleF(col * CellWidth, row * CellHeight, CellWidth, CellHeight), RedBrush);
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
                converter.Initialize(frame, WicPixelFormat);

                var props = new BitmapProperties1()
                {
                    BitmapOptions = BitmapOptions.Target,
                    PixelFormat = Direct2DPixelFormat
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
                    var wicPixelFormat = WicPixelFormat;
                    frameEncode.SetPixelFormat(ref wicPixelFormat);

                    var imageParams = new ImageParameters()
                    {
                        PixelFormat = Direct2DPixelFormat,
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
