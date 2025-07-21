// 文件路径: CADTranslator/Models/CAD/UnderlineOptions.cs
// 【请用此代码完整替换】

namespace CADTranslator.Models.CAD
    {
    /// <summary>
    /// 存储为一组文字添加下划线所需的所有配置。
    /// 这个类现在可以区分“标题”和“默认”两种不同的样式。
    /// </summary>
    public class UnderlineOptions
        {
        /// <summary>
        /// 获取或设置标题行的下划线样式。
        /// </summary>
        public UnderlineStyle TitleStyle { get; set; }

        /// <summary>
        /// 获取或设置普通（非标题）文本行的下划线样式。
        /// </summary>
        public UnderlineStyle DefaultStyle { get; set; }

        /// <summary>
        /// 下划线相对于文字基线的垂直偏移量。
        /// 这个值对所有类型的下划线都生效。
        /// </summary>
        public double VerticalOffset { get; set; } = -2.0;

        /// <summary>
        /// 构造函数，在这里为两种样式设置好默认值。
        /// </summary>
        public UnderlineOptions()
            {
            // 这是您为“标题行”指定的样式
            TitleStyle = new UnderlineStyle
                {
                Layer = "S-TEXT",
                ColorIndex = 3, // 3 代表绿色
                Linetype = "ByLayer",
                LinetypeScale = 1.0,
                GlobalWidth = 100.0
                };

            // 这是普通行的样式，保持和您原来一样的默认值
            DefaultStyle = new UnderlineStyle
                {
                Layer = "0-辅助线(打印)",
                ColorIndex = 253,
                Linetype = "ByLayer",
                LinetypeScale = 1.0,
                GlobalWidth = 0.0 // 普通直线宽度为0
                };
            }
        }
    }