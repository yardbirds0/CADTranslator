using CADTranslator.Models;
using CADTranslator.Models.API;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CADTranslator.Services.Settings
    {
    #region --- 数据模型 ---

    /// <summary>
    /// 用于定义历史记录中原始Key与友好名称映射规则的类
    /// </summary>
    public class MappingRule
        {
        public List<string> Aliases { get; set; } = new List<string>();
        public string DefaultFriendlyName { get; set; } = string.Empty;
        public Dictionary<string, string> Overrides { get; set; } = new Dictionary<string, string>();
        }

    /// <summary>
    /// 一个新的、更全面的设置类，用于管理程序的所有设置
    /// </summary>
    public class AppSettings
        {
        // UI相关的设置
        public bool IsLiveLayoutEnabled { get; set; } = true;
        public string LastSelectedLineSpacing { get; set; } = "不指定";
        public List<string> LineSpacingPresets { get; set; } = new List<string> { "不指定", "200" };
        public string WzpbLineSpacing { get; set; } = "不指定";
        public bool IsMultiThreadingEnabled { get; set; } = false;
        public List<string> ConcurrencyPresets { get; set; } = new List<string> { "2", "5" };
        public string LastSelectedConcurrency { get; set; } = "5";
        public double ParagraphSimilarityThreshold { get; set; } = 0.6;
        public bool AddUnderlineAfterWzpb { get; set; } = false;
        public bool AddUnderlineAfterSmartLayout { get; set; } = false;
        public bool IsLanguageSettingsExpanded { get; set; } = true;
        public bool IsApiSettingsExpanded { get; set; } = true;
        public bool IsFunctionSettingsExpanded { get; set; } = false;
        public string SourceLanguage { get; set; } = "auto"; // 默认源语言为自动检测
        public string TargetLanguage { get; set; } = "en";   // 默认目标语言为英语

        // API相关的设置
        public ApiServiceType LastSelectedApiService { get; set; } = ApiServiceType.Baidu;
        public List<ApiProfile> ApiProfiles { get; set; } = new List<ApiProfile>();

        // 历史记录与统计
        public List<BalanceRecord> BalanceHistory { get; set; } = new List<BalanceRecord>();

        public Dictionary<string, ApiUsageStats> UsageStatistics { get; set; } = new Dictionary<string, ApiUsageStats>();
        public Dictionary<string, MappingRule> FriendlyNameMappings { get; set; } = new Dictionary<string, MappingRule>();
        }

    #endregion

    // ▼▼▼ 【核心修改】在这里添加对 ISettingsService 接口的实现 ▼▼▼
    public class SettingsService : ISettingsService
        {
        private readonly string _settingsFilePath;
        private const string SettingsFileName = "cad_translator_settings.json";

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
            AppSettings settings;
            if (!File.Exists(_settingsFilePath) || string.IsNullOrWhiteSpace(File.ReadAllText(_settingsFilePath)))
                {
                settings = CreateDefaultSettings();
                }
            else
                {
                try
                    {
                    string json = File.ReadAllText(_settingsFilePath);
                    settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? CreateDefaultSettings();
                    }
                catch
                    {
                    settings = CreateDefaultSettings();
                    }
                }

            settings.FriendlyNameMappings = GetDefaultMappings();

            if (settings.UsageStatistics == null) settings.UsageStatistics = new Dictionary<string, ApiUsageStats>();
            if (settings.ApiProfiles == null) settings.ApiProfiles = new List<ApiProfile>();

            return settings;
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

        /// <summary>
        /// 创建一份包含所有默认值的设置
        /// </summary>
        private AppSettings CreateDefaultSettings()
            {
            return new AppSettings
                {
                ApiProfiles = new List<ApiProfile>
                {
                    new ApiProfile { ProfileName = "百度翻译 (默认)", ServiceType = ApiServiceType.Baidu }
                },
                FriendlyNameMappings = GetDefaultMappings()
                };
            }

        /// <summary>
        /// 获取我们预定义的友好名称映射规则
        /// </summary>
        private Dictionary<string, MappingRule> GetDefaultMappings()
            {
            return new Dictionary<string, MappingRule>
            {
                // --- 用户基础信息 ---
                {
                    "CanonicalUserId", new MappingRule
                    {
                        Aliases = new List<string> { "id", "user_id", "userId", "uid", "user-id" },
                        DefaultFriendlyName = "用户ID",
                        Overrides = new Dictionary<string, string> { { "SomeFutureApiForModels", "模型ID" } }
                    }
                },
                {
                    "CanonicalRemainingBalance", new MappingRule
                    {
                        Aliases = new List<string> { "balance", "credit", "remaining_balance" },
                        DefaultFriendlyName = "余额"
                    }
                },
                {
                    "CanonicalUserName", new MappingRule
                    {
                        Aliases = new List<string> { "name", "username" },
                        DefaultFriendlyName = "用户名"
                    }
                },
               
                {
                    "CanonicalUserEmail", new MappingRule
                    {
                        Aliases = new List<string> { "email" },
                        DefaultFriendlyName = "邮箱地址"
                    }
                },
                {
                    "CanonicalUserImage", new MappingRule
                    {
                        Aliases = new List<string> { "image" },
                        DefaultFriendlyName = "用户头像"
                    }
                },

                // --- 账户状态与角色 ---
                {
                    "CanonicalAccountStatus", new MappingRule
                    {
                        Aliases = new List<string> { "status", "accountStatus", "state" },
                        DefaultFriendlyName = "账户状态"
                    }
                },
                  {
                    "CanonicalChargeBalance", new MappingRule
                    {
                        Aliases = new List<string> { "chargeBalance" },
                        DefaultFriendlyName = "充值余额"
                    }
                },
                {
                    "CanonicalUserRole", new MappingRule
                    {
                        Aliases = new List<string> { "role" },
                        DefaultFriendlyName = "角色"
                    }
                },
                {
                    "CanonicalIsAdmin", new MappingRule
                    {
                        Aliases = new List<string> { "isAdmin", "Admin" },
                        DefaultFriendlyName = "是否为管理员"
                    }
                },

                // --- 余额相关 ---
                {
                    "CanonicalTotalBalance", new MappingRule
                    {
                        Aliases = new List<string> { "totalBalance", "totalbalance" },
                        DefaultFriendlyName = "总余额"
                    }
                },
               
        
                // --- 用量统计 ---
                {
                    "CanonicalRequestCount", new MappingRule
                    {
                        Aliases = new List<string> { "requests", "request_count", "call_count" },
                        DefaultFriendlyName = "请求次数"
                    }
                },
                {
                    "CanonicalTokenCount", new MappingRule
                    {
                        Aliases = new List<string> { "tokens", "token_count", "usage" },
                        DefaultFriendlyName = "Tokens用量"
                    }
                }
            };
            }
        }
    }