using CADTranslator.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace CADTranslator.Services
    {
    public class SettingsService
        {
        // 定义配置文件的保存路径和名称
        private readonly string _settingsFilePath;
        private const string SettingsFileName = "api_settings.json";

        public SettingsService()
            {
            // 将配置文件放在一个安全、通用的位置 (AppData)
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolderPath = Path.Combine(appDataPath, "CADTranslator");
            Directory.CreateDirectory(appFolderPath); // 确保文件夹存在
            _settingsFilePath = Path.Combine(appFolderPath, SettingsFileName);
            }

        // 从本地文件中加载所有API配置
        public List<ApiProfile> LoadApiProfiles()
            {
            if (!File.Exists(_settingsFilePath))
                {
                // 如果文件不存在，返回一个包含默认百度配置的列表
                return new List<ApiProfile>
                {
                    new ApiProfile { ProfileName = "百度翻译 (默认)", ServiceType = ApiServiceType.Baidu }
                };
                }

            try
                {
                string json = File.ReadAllText(_settingsFilePath);
                return JsonConvert.DeserializeObject<List<ApiProfile>>(json) ?? new List<ApiProfile>();
                }
            catch (Exception)
                {
                // 如果文件损坏或读取失败，返回一个空列表以防程序崩溃
                return new List<ApiProfile>();
                }
            }

        // 将所有API配置保存到本地文件
        public void SaveApiProfiles(List<ApiProfile> profiles)
            {
            try
                {
                string json = JsonConvert.SerializeObject(profiles, Formatting.Indented);
                File.WriteAllText(_settingsFilePath, json);
                }
            catch (Exception)
                {
                // 在实际应用中，这里应该记录错误日志
                }
            }
        }
    }