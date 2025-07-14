// CADTranslator/Services/CadBridgeService.cs

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
        /// 将消息写入到AutoCAD的命令行。
        /// </summary>
        /// <param name="message">要显示的消息。</param>
        public static void WriteToCommandLine(string message)
            {
            // 确保我们能获取到当前活动的文档
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // 移除可能存在的时间戳等信息，让命令行更整洁
            // 例如，移除 [14:25:10] 这样的部分
            var cleanMessage = Regex.Replace(message, @"^\[\d{2}:\d{2}:\d{2}\]\s*", "");

            // 向命令行写入消息，\n确保每条消息都换行
            doc.Editor.WriteMessage($"\n[CADTranslator] {cleanMessage}");
            }
        }
    }