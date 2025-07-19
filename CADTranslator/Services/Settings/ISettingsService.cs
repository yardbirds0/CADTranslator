// 文件路径: CADTranslator/Services/ISettingsService.cs
using CADTranslator.Models;

namespace CADTranslator.Services.Settings
    {
    /// <summary>
    /// 定义设置服务的接口。
    /// 任何实现了此接口的类都必须提供加载和保存程序设置的功能。
    /// </summary>
    public interface ISettingsService
        {
        /// <summary>
        /// 从持久化存储中加载所有程序设置。
        /// </summary>
        /// <returns>包含所有设置的AppSettings对象。</returns>
        AppSettings LoadSettings();

        /// <summary>
        /// 将给定的设置对象保存到持久化存储中。
        /// </summary>
        /// <param name="settings">要保存的设置对象。</param>
        void SaveSettings(AppSettings settings);
        }
    }