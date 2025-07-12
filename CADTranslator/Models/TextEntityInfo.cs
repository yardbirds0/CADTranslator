using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CADTranslator.Models
{
    public class TextEntityInfo
    {
        public ObjectId ObjectId { get; set; }
        public string Text { get; set; }
        public Point3d Position { get; set; }
        public double Height { get; set; }
    }
}