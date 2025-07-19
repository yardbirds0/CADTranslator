// 文件路径: CADTranslator/Services/IWindowService.cs
using CADTranslator.ViewModels;
using System;
using System.Threading.Tasks;
using Wpf.Ui.Controls; // 确保引入WPF-UI库的命名空间

namespace CADTranslator.Services.UI
    {
    /// <summary>
    /// 定义一个服务接口，用于解耦ViewModel和View之间的交互。
    /// ViewModel通过调用此接口中的方法来请求UI操作，而不是直接引用和操作Window对象。
    /// </summary>
    public interface IWindowService
        {
        /// <summary>
        /// (名称已修改) 显示一个只包含信息和关闭按钮的对话框。
        /// </summary>
        /// <param name="title">窗口标题</param>
        /// <param name="content">显示内容</param>
        /// <param name="closeButtonText">关闭按钮文字</param>
        Task ShowInformationDialogAsync(string title, string content, string closeButtonText = "确定");

        /// <summary>
        /// (保持不变) 显示一个需要用户做出选择的确认对话框。
        /// </summary>
        /// <returns>用户的选择结果</returns>
        Task<MessageBoxResult> ShowConfirmationDialogAsync(string title, string content, string primaryButtonText, string closeButtonText = "取消");

        /// <summary>
        /// 显示“模型管理”对话框。
        /// </summary>
        /// <returns>如果用户点击了“应用选择模型”，则返回true。</returns>
        bool? ShowModelManagementDialog(ModelManagementViewModel viewModel);

        /// <summary>
        /// 显示“余额历史”对话框。
        /// </summary>
        void ShowBalanceHistoryDialog(BalanceHistoryViewModel viewModel);

        /// <summary>
        /// 显示“编辑”对话框
        /// </summary>
        /// <param name="initialText">初始文本</param>
        /// <returns>一个元组，包含对话框结果和编辑后的文本</returns>
        (bool? DialogResult, string EditedText) ShowEditDialog(string initialText);

        // 控制主窗口状态的接口
        void HideMainWindow();
        void ShowMainWindow();
        void MinimizeMainWindow();
        void ActivateMainWindow();
        void InvokeOnUIThread(Action action);
        }
    }