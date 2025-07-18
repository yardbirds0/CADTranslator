// 文件路径: CADTranslator/Services/SettingsService.cs

using CADTranslator.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CADTranslator.Services
    {
    #region --- 数据模型 ---

    /// <summary>
    /// 用于定义历史记录中原始Key与友好名称映射规则的类
    /// </summary>
    public class MappingRule
        {
        /// <summary>
        /// 此规则关联的所有原始Key（别名）。
        /// 例如: ["id", "Identity", "user_id"]
        /// </summary>
        public List<string> Aliases { get; set; } = new List<string>();

        /// <summary>
        /// 全局默认的友好名称，用于UI显示。
        /// 例如: "用户ID"
        /// </summary>
        public string DefaultFriendlyName { get; set; } = string.Empty;

        /// <summary>
        /// 针对特定API的局部覆盖。
        /// Key: ApiServiceType的字符串名称 (例如 "SomeFutureApiForModels")
        /// Value: 针对该API的特定友好名称 (例如 "模型ID")
        /// </summary>
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

        // API相关的设置
        public ApiServiceType LastSelectedApiService { get; set; } = ApiServiceType.Baidu;
        public List<ApiProfile> ApiProfiles { get; set; } = new List<ApiProfile>();

        // 历史记录与统计
        public List<BalanceRecord> BalanceHistory { get; set; } = new List<BalanceRecord>();

        /// <summary>
        /// 【新增】存储每个API的用量统计数据
        /// </summary>
        public Dictionary<string, ApiUsageStats> UsageStatistics { get; set; } = new Dictionary<string, ApiUsageStats>();

        /// <summary>
        /// 【新增】存储历史记录中Key到友好名称的映射规则
        /// Key: 我们自己定义的、永不改变的标准Key (如 "CanonicalUserId")
        /// </summary>
        public Dictionary<string, MappingRule> FriendlyNameMappings { get; set; } = new Dictionary<string, MappingRule>();
        }

    #endregion

    public class SettingsService
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
                // 如果文件不存在或为空，创建一个全新的默认设置
                settings = CreateDefaultSettings();
                }
            else
                {
                try
                    {
                    // 否则，从文件中加载现有设置
                    string json = File.ReadAllText(_settingsFilePath);
                    settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? CreateDefaultSettings();
                    }
                catch
                    {
                    // 如果文件损坏，也创建一个全新的默认设置
                    settings = CreateDefaultSettings();
                    }
                }

            // 【最终核心修正】
            // 无条件地、强制地使用代码中定义的最新默认规则，
            // 直接覆盖掉从文件中加载的任何旧的映射规则。
            settings.FriendlyNameMappings = GetDefaultMappings();

            // 确保其他字段不为null
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
                FriendlyNameMappings = GetDefaultMappings() // 包含我们预定义的映射规则
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
        {
            "CanonicalChargeBalance", new MappingRule
            {
                Aliases = new List<string> { "chargeBalance" },
                DefaultFriendlyName = "充值余额"
            }
        },
        {
            "CanonicalRemainingBalance", new MappingRule
            {
                Aliases = new List<string> { "balance", "credit", "remaining_balance" },
                DefaultFriendlyName = "（剩余）余额"
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