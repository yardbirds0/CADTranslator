// 文件路径: CADTranslator/Models/ApiServiceDisplay.cs
namespace CADTranslator.Models.API
    {
    /// <summary>
    /// 用于在UI中显示API服务选项的包装类
    /// </summary>
    public class ApiServiceDisplay
        {
        /// <summary>
        /// 在下拉菜单中显示的文本，例如 "百度翻译"
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 对应的后台枚举值，例如 ApiServiceType.Baidu
        /// </summary>
        public ApiServiceType ServiceType { get; set; }
        }
    }