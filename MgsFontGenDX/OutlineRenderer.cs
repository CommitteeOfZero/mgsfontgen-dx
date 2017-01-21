using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;

namespace MgsFontGenDX
{
    public class OutlineRenderer : TextRendererBase
    {
        readonly SharpDX.Direct2D1.Factory _factory;
        readonly RenderTarget _surface;
        readonly Brush _brush;

        public OutlineRenderer(RenderTarget surface, Brush brush)
        {
            _factory = surface.Factory;
            _surface = surface;
            _brush = brush;
        }

        public override Result DrawGlyphRun(object clientDrawingContext, float baselineOriginX, float baselineOriginY, MeasuringMode measuringMode, GlyphRun glyphRun, GlyphRunDescription glyphRunDescription, ComObject clientDrawingEffect)
        {
            using (PathGeometry path = new PathGeometry(_factory))
            using (GeometrySink sink = path.Open())
            {
                glyphRun.FontFace.GetGlyphRunOutline(glyphRun.FontSize, glyphRun.Indices, glyphRun.Advances, glyphRun.Offsets, glyphRun.IsSideways, false, sink);

                sink.Close();

                var matrix = Matrix3x2.Translation(baselineOriginX, baselineOriginY);
                TransformedGeometry transformedGeometry = new TransformedGeometry(_factory, path, matrix);

                _surface.DrawGeometry(transformedGeometry, _brush);

            }
            return new Result();
        }
    }
}
