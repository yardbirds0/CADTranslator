// 文件路径: CADTranslator/Services/CadBridgeService.cs
using Autodesk.AutoCAD.ApplicationServices;
using System.Text.RegularExpressions;

namespace CADTranslator.Services
    {
    /// <summary>
    /// 一个静态服务类，用于在UI层（ViewModel）和AutoCAD环境之间进行通信。
    /// </summary>
    public static class CadBridgeService
        {
        /// <summary>
        /// (已修改) 将消息作为新的一行写入到AutoCAD的命令行历史记录中。
        /// </summary>
        /// <param name="message">要显示的消息。</param>
        public static void WriteToCommandLine(string message)
            {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var cleanMessage = Regex.Replace(message, @"^\[\d{2}:\d{2}:\d{2}\]\s*", "");

            // 使用 \n[CADTranslator] {message}\n 的格式
            // 开始的 \n 确保结束之前的任何输入提示
            // 结尾的 \n 确保我们的消息输出后，光标会换到新的一行，避免污染后续的命令输入
            doc.Editor.WriteMessage($"\n[CADTranslator] {cleanMessage}\n");
            }

        /// <summary>
        /// 【新增】通过回车符(\r)覆盖当前命令行，用于实时更新状态。
        /// </summary>
        /// <param name="message">要显示的消息。</param>
        public static void UpdateLastMessageOnCommandLine(string message)
            {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var cleanMessage = Regex.Replace(message, @"^\[\d{2}:\d{2}:\d{2}\]\s*", "");

            // \r (回车符) 将光标移动到行首，后续的输出会覆盖之前的内容
            // 同样以 \n 结尾，确保更新完成后光标换行
            doc.Editor.WriteMessage($"\r[CADTranslator] {cleanMessage}\n");
            }
        }
    }