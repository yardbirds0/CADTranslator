﻿// 文件路径: CADTranslator/Services/CadBridgeService.cs
using Autodesk.AutoCAD.ApplicationServices;
using CADTranslator.ViewModels;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CADTranslator.AutoCAD.Commands;

namespace CADTranslator.Services.CAD
    {
    /// <summary>
    /// 一个静态服务类，用于在UI层（ViewModel）和AutoCAD环境之间进行通信。
    /// </summary>
    public static class CadBridgeService
        {

        /// <summary>
        /// 【新增】向AutoCAD发送一个命令字符串。
        /// </summary>
        /// <param name="command">要执行的命令 (需要以 \n 结尾以模拟回车)</param>
        public static void SendCommandToAutoCAD(string command)
            {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            doc.SendStringToExecute(command, true, false, false);
            }

        /// <summary>
        /// 【新的桥接方法】
        /// 直接、安全地调用 MainCommands 中的核心布局逻辑。
        /// 这使得调用堆栈可以被调试器跟踪。
        /// </summary>
        public static void InvokeApplyLayout()
            {
            // 检查当前活动的文档是否存在
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // 使用 ExecuteInApplicationContext 来确保我们的代码在 AutoCAD 的主线程中安全执行
            Application.DocumentManager.ExecuteInApplicationContext(
                (state) =>
                {
                    // 在这里，我们直接调用 MainCommands 类中的静态方法
                    MainCommands.ExecuteApplyLayoutLogic();
                },
                null // state 对象，这里我们不需要传递任何额外状态
            );
            }

        public static ObservableCollection<TextBlockViewModel> TextBlocksToLayout { get; set; }
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