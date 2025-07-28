// 文件路径: CADTranslator/Services/ApiRegistry.cs

using CADTranslator.Models;
using CADTranslator.Models.API;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CADTranslator.Services.Translation
    {
    /// <summary>
    /// API服务注册中心，用于统一管理和提供所有翻译服务(ITranslator)的实例。
    /// </summary>
    public class ApiRegistry
        {
        #region --- 字段和属性 ---

        /// <summary>
        /// 存储所有已注册的翻译服务提供商实例的字典。
        /// Key: 服务的枚举类型 (ApiServiceType)
        /// Value: 实现了ITranslator接口的服务实例
        /// </summary>
        private readonly Dictionary<ApiServiceType, ITranslator> _providers = new Dictionary<ApiServiceType, ITranslator>();

        /// <summary>
        /// 获取所有已注册的服务提供商的列表，用于UI绑定。
        /// </summary>
        public List<ITranslator> Providers => _providers.Values.ToList();

        #endregion

        #region --- 构造与初始化 ---

        /// <summary>
        /// 在构造函数中，初始化所有内置的API服务提供商。
        /// </summary>
        public ApiRegistry()
            {
            // 注意：这里我们为每个服务都提供了一个临时的、可能无效的API Profile。
            // 在实际使用中，ViewModel会用真实的、从设置中加载的Profile来创建新的实例。
            // 这里的初始化主要是为了填充注册表，确保每个ApiServiceType都有一个默认的提供商。

            const string placeholder = "placeholder";

            Register(new BaiduTranslator(placeholder, placeholder));
            Register(new GeminiTranslator(placeholder, "gemini-1.0-pro"));
            Register(new OpenAiTranslator(placeholder, "gpt-3.5-turbo"));
            Register(new CustomTranslator("http://placeholder.url/v1", placeholder, "custom-model"));
            Register(new SiliconFlowTranslator("https://api.siliconflow.cn/v1", placeholder, "deepseek-llm-7b-chat"));
            Register(new ChatAnywhereTranslator("https://api.chatanywhere.tech", placeholder, "gpt-3.5-turbo"));
            }

        #endregion

        #region --- 公共方法 ---

        /// <summary>
        /// 注册一个服务提供商。
        /// </summary>
        /// <param name="provider">要注册的服务实例。</param>
        public void Register(ITranslator provider)
            {
            if (provider != null && !_providers.ContainsKey(provider.ServiceType))
                {
                _providers[provider.ServiceType] = provider;
                }
            }

        /// <summary>
        /// 根据服务类型获取对应的服务提供商实例。
        /// </summary>
        /// <param name="serviceType">API服务的枚举类型。</param>
        /// <returns>对应的服务实例，如果未找到则返回null。</returns>
        public ITranslator GetProvider(ApiServiceType serviceType)
            {
            return _providers.ContainsKey(serviceType) ? _providers[serviceType] : null;
            }

        /// <summary>
        /// （核心方法）根据一个API配置文件(ApiProfile)创建一个新的、完全配置好的翻译服务实例。
        /// </summary>
        /// <param name="profile">包含API密钥、URL等信息的配置文件。</param>
        /// <returns>一个新的、可用的ITranslator实例。</returns>
        public ITranslator CreateProviderForProfile(ApiProfile profile)
            {
            if (profile == null)
                {
                throw new ArgumentNullException(nameof(profile));
                }

            switch (profile.ServiceType)
                {
                case ApiServiceType.Baidu:
                    return new BaiduTranslator(profile.UserId, profile.ApiKey);
                case ApiServiceType.Gemini:
                    return new GeminiTranslator(profile.ApiKey, profile.LastSelectedModel);
                case ApiServiceType.OpenAI:
                    return new OpenAiTranslator(profile.ApiKey, profile.LastSelectedModel);
                case ApiServiceType.SiliconFlow:
                    return new SiliconFlowTranslator(profile.ApiEndpoint, profile.ApiKey, profile.LastSelectedModel);
                case ApiServiceType.Custom:
                    return new CustomTranslator(profile.ApiEndpoint, profile.ApiKey, profile.LastSelectedModel);
                case ApiServiceType.ChatAnywhere:
                    return new ChatAnywhereTranslator(profile.ApiEndpoint, profile.ApiKey, profile.LastSelectedModel);
                default:
                    throw new NotSupportedException($"不支持的服务类型: {profile.ServiceType}");
                }
            }

        #endregion
        }
    }