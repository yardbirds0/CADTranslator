// 文件路径: CADTranslator/Models/ApiException.cs
// 【新增文件】

using System;
using System.Net;

namespace CADTranslator.Models
    {
    /// <summary>
    /// 定义了API错误的具体类别，用于ViewModel层判断如何向用户展示错误信息。
    /// </summary>
    public enum ApiErrorType
        {
        /// <summary>
        /// 未知或未分类的错误。
        /// </summary>
        Unknown,

        /// <summary>
        /// API明确返回的业务错误（例如，HTTP状态码为4xx或5xx，且响应体中包含错误信息）。
        /// </summary>
        ApiError,

        /// <summary>
        /// 网络连接层面的错误（例如，无法访问主机、DNS解析失败、请求超时）。
        /// 这通常意味着用户的网络环境（或VPN）有问题。
        /// </summary>
        NetworkError,

        /// <summary>
        /// 发送给API的请求本身无效（例如，API密钥为空、参数缺失）。
        /// 这通常是用户的配置问题。
        /// </summary>
        ConfigurationError,

        /// <summary>
        /// API返回了成功状态码（HTTP 200），但响应的内容格式不正确，无法解析。
        /// </summary>
        InvalidResponse
        }

    /// <summary>
    /// 一个自定义的异常类，用于在服务层和视图模型层之间传递结构化的API错误信息。
    /// </summary>
    public class ApiException : Exception
        {
        #region --- 属性 ---

        /// <summary>
        /// 错误的具体类别。
        /// </summary>
        public ApiErrorType ErrorType { get; }

        /// <summary>
        /// 发生错误的API服务提供商。
        /// </summary>
        public ApiServiceType Provider { get; }

        /// <summary>
        /// HTTP状态码 (如果适用)。
        /// </summary>
        public HttpStatusCode? StatusCode { get; }

        /// <summary>
        /// API返回的业务错误码 (如果适用)。
        /// </summary>
        public string ApiErrorCode { get; }

        #endregion

        #region --- 构造函数 ---

        /// <summary>
        /// 初始化一个新的ApiException实例。
        /// </summary>
        /// <param name="errorType">错误的类别。</param>
        /// <param name="provider">发生错误的服务商。</param>
        /// <param name="message">要向用户展示的、友好的错误消息。</param>
        /// <param name="statusCode">HTTP状态码。</param>
        /// <param name="apiErrorCode">API业务错误码。</param>
        public ApiException(ApiErrorType errorType, ApiServiceType provider, string message, HttpStatusCode? statusCode = null, string apiErrorCode = null)
            : base(message) // 将用户友好的消息直接作为Exception的基类消息
            {
            ErrorType = errorType;
            Provider = provider;
            StatusCode = statusCode;
            ApiErrorCode = apiErrorCode;
            }

        #endregion
        }
    }