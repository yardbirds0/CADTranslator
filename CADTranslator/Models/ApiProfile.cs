using System;
using System.Collections.Generic;

namespace CADTranslator.Models
    {
    // 定义一个枚举来代表不同的API服务类型
    public enum ApiServiceType
        {
        Baidu,
        Gemini,
        OpenAI,
        Custom
        }

    public class ApiProfile
        {
        // 用于唯一标识一套配置
        public Guid Id { get; set; } = Guid.NewGuid();

        // 用户在界面上看到的名称，例如 "我的Gemini Key"
        public string ProfileName { get; set; }

        public ApiServiceType ServiceType { get; set; }

        public string UserId { get; set; }
        public string ApiKey { get; set; }

        // 为自定义API准备的字段
        public string ApiEndpoint { get; set; }

        // 这套配置下保存的可用模型列表
        public List<string> Models { get; set; } = new List<string>();

        // 这套配置下用户最后选择的模型
        public string LastSelectedModel { get; set; }
        }
    }