// 文件路径: CADTranslator/Models/API/PromptEnums.cs
// 【这是一个新文件】

namespace CADTranslator.Models.API
    {
    /// <summary>
    /// 定义提示词的专业模板类型
    /// </summary>
    public enum PromptTemplateType
        {
        Custom,
        Structure,
        Architecture,
        HVAC, // 暖通
        Electrical,
        Plumbing, // 给排水
        Process, // 工艺
        Thermal // 热力
        }

    /// <summary>
    /// 定义提示词的发送模式
    /// </summary>
    public enum PromptSendingMode
        {
        Once, // 只发送一次（批处理模式）
        PerSentence // 每句都发送（旧模式）
        }
    }