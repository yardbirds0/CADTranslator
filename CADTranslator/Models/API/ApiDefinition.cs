// 文件路径: CADTranslator/Models/ApiDefinition.cs

using System;

namespace CADTranslator.Models.API
    {
    /// <summary>
    /// 存储用户自定义API服务的所有配置信息
    /// </summary>
    public class ApiDefinition
        {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string DisplayName { get; set; } = "新的自定义API";
        public string ApiDocumentationUrl { get; set; } = string.Empty;

        // 这些属性将直接决定自定义的GenericTranslator的行为
        public bool IsApiKeyRequired { get; set; } = true;
        public bool IsUserIdRequired { get; set; } = false;
        public bool IsApiUrlRequired { get; set; } = true;
        public bool IsModelRequired { get; set; } = true;
        public bool IsPromptSupported { get; set; } = true;
        public bool IsModelFetchingSupported { get; set; } = false; // 自定义API默认不支持在线获取模型
        public bool IsBalanceCheckSupported { get; set; } = false; // 自定义API默认不支持查询余额

        // 与API Profile共享的属性
        public string ApiEndpoint { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        }
    }