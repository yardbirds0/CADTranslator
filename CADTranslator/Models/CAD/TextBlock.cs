using Autodesk.AutoCAD.DatabaseServices;
using System.Collections.Generic;

namespace CADTranslator.Models.CAD
{
    public class TextBlock
    {
        public int Id { get; set; }
        public string OriginalText { get; set; }
        public string TranslatedText { get; set; }
        public List<ObjectId> SourceObjectIds { get; set; } = new List<ObjectId>();
        public bool IsTitle { get; set; } = false;
        public string GroupKey { get; set; } = null;
        }
}