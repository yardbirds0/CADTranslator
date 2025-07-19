// 文件路径: CADTranslator/Services/ICadLayoutService.cs
using Autodesk.AutoCAD.DatabaseServices;
using CADTranslator.ViewModels;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CADTranslator.Services.CAD
    {
    /// <summary>
    /// 定义CAD布局服务的接口，负责将翻译结果写回图纸。
    /// </summary>
    public interface ICadLayoutService
        {
        /// <summary>
        /// 使用智能实时排版Jig，将翻译文本应用到CAD图纸。
        /// </summary>
        /// <param name="textBlockList">包含翻译结果和原始几何信息的ViewModel列表。</param>
        /// <param name="idsToDelete">需要被删除的原始CAD对象ID列表。</param>
        /// <param name="lineSpacing">用户指定的行间距字符串。</param>
        /// <returns>操作是否成功。</returns>
        bool ApplySmartLayoutToCad(ObservableCollection<TextBlockViewModel> textBlockList, List<ObjectId> idsToDelete, string lineSpacing);

        /// <summary>
        /// 使用基本的替换逻辑，将翻译文本应用到CAD图纸（用于关闭“实时排版”时）。
        /// </summary>
        /// <param name="textBlockList">包含翻译结果的ViewModel列表。</param>
        /// <param name="idsToDelete">一个动态计算出的、需要被删除的原始CAD对象ID列表。</param>
        /// <returns>操作是否成功。</returns>
        bool ApplyTranslationToCad(ObservableCollection<TextBlockViewModel> textBlockList, List<ObjectId> idsToDelete);
        }
    }