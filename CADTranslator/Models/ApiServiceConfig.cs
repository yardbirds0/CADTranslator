namespace CADTranslator.Models
    {
    public class ApiServiceConfig
        {
        // 这个服务是否需要用户ID
        public bool RequiresUserId { get; }

        // 这个服务是否需要API Key
        public bool RequiresApiKey { get; }

        // 这个服务是否需要模型列表
        public bool RequiresModelList { get; }

        // 这个服务是否需要自定义API URL
        public bool RequiresApiUrl { get; }

        public ApiServiceConfig(bool requiresUserId, bool requiresApiKey, bool requiresModelList, bool requiresApiUrl)
            {
            RequiresUserId = requiresUserId;
            RequiresApiKey = requiresApiKey;
            RequiresModelList = requiresModelList;
            RequiresApiUrl = requiresApiUrl;
            }
        }
    }