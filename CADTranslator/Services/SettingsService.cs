// 文件路径: CADTranslator/Services/SettingsService.cs

using CADTranslator.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace CADTranslator.Services
    {
    /// <summary>
    /// 一个新的、更全面的设置类，用于管理程序的所有设置
    /// </summary>
    public class AppSettings
        {
        // UI相关的设置
        public bool IsLiveLayoutEnabled { get; set; } = true; // 默认开启

        // API相关的设置
        public List<ApiProfile> ApiProfiles { get; set; } = new List<ApiProfile>();
        public ApiServiceType LastSelectedApiService { get; set; } = ApiServiceType.Baidu; // 默认使用百度

        public string LastSelectedLineSpacing { get; set; } = "不指定";
        public List<string> LineSpacingPresets { get; set; } = new List<string> { "不指定", "200" };
        public string WzpbLineSpacing { get; set; } = "不指定";
        }


    public class SettingsService
        {
        private readonly string _settingsFilePath;
        private const string SettingsFileName = "cad_translator_settings.json"; // 改个更明确的名字

        public SettingsService()
            {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolderPath = Path.Combine(appDataPath, "CADTranslator");
            Directory.CreateDirectory(appFolderPath);
            _settingsFilePath = Path.Combine(appFolderPath, SettingsFileName);
            }

        /// <summary>
        /// 从本地文件加载所有程序设置
        /// </summary>
        public AppSettings LoadSettings()
            {
            if (!File.Exists(_settingsFilePath))
                {
                // 如果文件不存在，返回一个包含默认值的全新设置对象
                return new AppSettings
                    {
                    IsLiveLayoutEnabled = true, // 默认开启
                    ApiProfiles = new List<ApiProfile>
                    {
                        // 提供一个默认的百度翻译配置
                        new ApiProfile { ProfileName = "百度翻译 (默认)", ServiceType = ApiServiceType.Baidu }
                    }
                    };
                }

            try
                {
                string json = File.ReadAllText(_settingsFilePath);
                // 如果文件存在但为空，也返回默认设置
                if (string.IsNullOrWhiteSpace(json))
                    {
                    return new AppSettings();
                    }
                // 反序列化成我们新的 AppSettings 对象
                return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
            catch (Exception)
                {
                // 如果文件损坏，返回一个全新的默认设置对象，防止程序崩溃
                return new AppSettings();
                }
            }

        /// <summary>
        /// 将所有程序设置保存到本地文件
        /// </summary>
        public void SaveSettings(AppSettings settings)
            {
            if (settings == null) return;
            try
                {
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(_settingsFilePath, json);
                }
            catch (Exception)
                {
                // 在实际应用中，这里应该记录错误日志
                }
            }
        }
    }