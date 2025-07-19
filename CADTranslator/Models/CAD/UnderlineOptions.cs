// 文件路径: CADTranslator/Models/UnderlineOptions.cs

namespace CADTranslator.Models.CAD
    {
    /// <summary>
    /// 存储下划线样式的配置类
    /// </summary>
    public class UnderlineOptions
        {
        /// <summary>
        /// 下划线所在的图层名称
        /// </summary>
        public string Layer { get; set; } = "0-辅助线(打印)";

        /// <summary>
        /// 下划线的颜色索引 (251)
        /// </summary>
        public short ColorIndex { get; set; } = 253;

        /// <summary>
        /// 下划线的线型名称
        /// </summary>
        public string Linetype { get; set; } = "ByLayer";

        /// <summary>
        /// 下划线的线型比例
        /// </summary>
        public double LinetypeScale { get; set; } = 1.0;

        /// <summary>
        /// 下划线相对于文字基线的垂直偏移量
        /// </summary>
        public double VerticalOffset { get; set; } = -2.0; // 默认向下偏移2个单位
        }
    }