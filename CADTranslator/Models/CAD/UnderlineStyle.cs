// 文件路径: CADTranslator/Models/CAD/UnderlineStyle.cs
// 【这是一个新文件】

namespace CADTranslator.Models.CAD
    {
    /// <summary>
    /// 存储一种特定下划线样式的所有配置信息。
    /// 这个类可以被复用，例如定义标题样式、正文样式等。
    /// </summary>
    public class UnderlineStyle
        {
        /// <summary>
        /// 下划线所在的图层名称。
        /// </summary>
        public string Layer { get; set; } = "0";

        /// <summary>
        /// 下划线的AutoCAD颜色索引。
        /// </summary>
        public short ColorIndex { get; set; } = 256; // 256 表示 ByLayer

        /// <summary>
        /// 下划线的线型名称。
        /// </summary>
        public string Linetype { get; set; } = "ByLayer";

        /// <summary>
        /// 下划线的线型比例。
        /// </summary>
        public double LinetypeScale { get; set; } = 1.0;

        /// <summary>
        /// 多段线的全局宽度。
        /// 注意：这个属性只对 Polyline 类型的下划线有效。
        /// </summary>
        public double GlobalWidth { get; set; } = 0.0;
        }
    }