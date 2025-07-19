// 文件路径: CADTranslator/Services/IAdvancedTextService.cs
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using CADTranslator.Models;
using CADTranslator.Models.CAD;
using System.Collections.Generic;

namespace CADTranslator.Services.CAD
    {
    /// <summary>
    /// 定义高级文本分析服务的接口。
    /// </summary>
    public interface IAdvancedTextService
        {
        /// <summary>
        /// 从给定的选择集中提取、合并、分析文本，并处理图例等特殊格式。
        /// </summary>
        /// <param name="selSet">用户在CAD中选择的对象集合。</param>
        /// <param name="allSourceIds">一个输出参数，返回所有被处理过的原始对象的ID。</param>
        /// <returns>一个包含详细段落信息的列表。</returns>
        public List<ParagraphInfo> ExtractAndProcessParagraphs(SelectionSet selSet, double similarityThreshold);
        List<string> GetOriginalTextsByIds(List<ObjectId> ids);
        }
    }