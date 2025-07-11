using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace CADTranslator
{
    public class TextLayoutJig : DrawJig
    {
        private Point3d _currentPosition;
        private readonly string _text;
        private readonly double _height;
        private readonly ObjectId _textStyleId;

        public Point3d ResultPosition { get; private set; }

        public TextLayoutJig(string text, Point3d basePoint, double height, ObjectId textStyleId)
        {
            _text = text;
            _currentPosition = basePoint;
            _height = height;
            _textStyleId = textStyleId;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var options = new JigPromptPointOptions("\n请指定文本放置点:")
            {
                UseBasePoint = false,
                Cursor = CursorType.Crosshair
            };
            var result = prompts.AcquirePoint(options);

            if (result.Status == PromptStatus.OK)
            {
                if (_currentPosition.IsEqualTo(result.Value))
                {
                    return SamplerStatus.NoChange;
                }
                _currentPosition = result.Value;
                return SamplerStatus.OK;
            }
            return SamplerStatus.Cancel;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            var text = new MText
            {
                Contents = _text,
                Location = _currentPosition,
                TextHeight = _height,
                TextStyleId = _textStyleId
            };
            draw.Geometry.Draw(text);
            return true;
        }

        public PromptStatus Run()
        {
            var editor = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            var result = editor.Drag(this);
            if (result.Status == PromptStatus.OK)
            {
                ResultPosition = _currentPosition;
            }
            return result.Status;
        }
    }
}