using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;

namespace CADTranslator.Models
{
    public class ParagraphInfo
    {
        public string Text { get; set; }
        public Entity TemplateEntity { get; set; }
        public string OriginalText { get; set; }
        public TextHorizontalMode HorizontalMode { get; set; }
        public TextVerticalMode VerticalMode { get; set; }
        public double Height { get; set; }
        public double WidthFactor { get; set; }
        public ObjectId TextStyleId { get; set; }
        public ObjectId AssociatedGraphicsBlockId { get; set; } = ObjectId.Null;
        public Point3d OriginalAnchorPoint { get; set; }
        public bool ContainsSpecialPattern { get; set; } = false;
        public int OriginalSpaceCount { get; set; } = 0;
        public List<ObjectId> SourceObjectIds { get; set; } = new List<ObjectId>();
        }
}