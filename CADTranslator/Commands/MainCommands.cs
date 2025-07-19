using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CADTranslator.Tools.CAD.Jigs;
using CADTranslator.Models.CAD;
using CADTranslator.Services.CAD;
using CADTranslator.Views;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CADTranslator.Services.Settings;


namespace CADTranslator.AutoCAD.Commands
    {
    public class MainCommands
        {
        #region 一个自定义异常类，专门用于在GetSelection的事件中传递关键字。
        public class KeywordException : System.Exception
            {
            public string Input { get; }
            public KeywordException(string input)
                {
                Input = input;
                }
            }
        #endregion

        private static TranslatorWindow translatorWindow;

        /// <summary>
        /// 静态构造函数，在类第一次被使用前自动执行，且只执行一次。
        /// 这是注册程序集解析事件的最佳位置。
        /// </summary>
        static MainCommands()
            {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            }

        #region 当.NET运行时找不到某个程序集时，会调用这个方法。用来解决没有app.config
        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
            {
            // 获取请求的程序集名称
            var requestedAssemblyName = new AssemblyName(args.Name);

            // 检查是不是我们正在寻找的那个出问题的程序集
            if (requestedAssemblyName.Name == "System.Runtime.CompilerServices.Unsafe")
                {
                try
                    {
                    // 获取当前正在执行的程序集（也就是我们的插件）所在的目录
                    string assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    // 构造我们实际拥有的那个新版本DLL的完整路径
                    string targetDllPath = Path.Combine(assemblyLocation, "System.Runtime.CompilerServices.Unsafe.dll");

                    // 如果文件存在，就从这个路径加载它
                    if (File.Exists(targetDllPath))
                        {
                        // 加载并返回这个程序集，问题解决
                        return Assembly.LoadFrom(targetDllPath);
                        }
                    }
                catch (System.Exception ex)
                    {
                    // 如果在解析过程中出错，就将错误打印到CAD命令行，方便调试
                    Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[AssemblyResolve] Error: {ex.Message}");
                    }
                }

            // 如果不是我们关心的程序集，或者加载失败，则返回null，让.NET按默认方式继续处理
            return null;
            }
        #endregion

        [CommandMethod("GJX")]
        public void LaunchToolbox()
            {
            if (translatorWindow == null || !translatorWindow.IsLoaded)
                {
                translatorWindow = new TranslatorWindow();
                new System.Windows.Interop.WindowInteropHelper(translatorWindow).Owner = Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle;
                translatorWindow.Show();
                }
            else
                {
                translatorWindow.Activate();
                if (!translatorWindow.IsVisible)
                    {
                    translatorWindow.Show();
                    }
                }
            }

        [CommandMethod("JDX")]
        public void DrawBreakLineCommand()
            {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var editor = doc.Editor;

            var ppr1 = editor.GetPoint("\n请指定截断线起点:");
            if (ppr1.Status != PromptStatus.OK) return;
            Point3d startPoint = ppr1.Value;

            var jig = new BreakLineJig(startPoint);
            var result = editor.Drag(jig);

            if (result.Status == PromptStatus.OK)
                {
                using (var tr = db.TransactionManager.StartTransaction())
                    {
                    var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    var finalPolyline = jig.Polyline;
                    if (finalPolyline != null)
                        {
                        modelSpace.AppendEntity(finalPolyline);
                        tr.AddNewlyCreatedDBObject(finalPolyline, true);
                        }
                    tr.Commit();
                    }
                }
            }

        [CommandMethod("WZPB")]
        public void TextLayoutCommand()
            {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var editor = doc.Editor;

            // 1. 加载设置
            var settingsService = new Services.Settings.SettingsService();
            var currentSettings = settingsService.LoadSettings();
            string currentLineSpacing = currentSettings.WzpbLineSpacing;
            object oldShortcutMenu = Application.GetSystemVariable("SHORTCUTMENU");


            // 2. 启动交互循环
            while (true)
                {
                Application.SetSystemVariable("SHORTCUTMENU", 0);
                try
                    {
                    // 3. 创建选择选项
                    var selOpts = new PromptSelectionOptions();
                    selOpts.MessageForAdding = $"\n请选择要重新排版的文字对象, 或 [设置行间距(H)] (当前: {currentLineSpacing})";

                    // 4. 添加关键字
                    selOpts.Keywords.Add("H");
                    selOpts. PrepareOptionalDetails= false;

                    // 5. 添加关键字输入事件处理，这里我们抛出自定义异常
                    selOpts.KeywordInput += (s, args) =>
                    {
                        throw new KeywordException(args.Input.ToUpper());
                    };

                    // 6. 获取用户选择
                    var selRes = editor.GetSelection(selOpts);
                    if (selRes.Status != PromptStatus.OK)
                        {
                        // 用户按下了ESC或发生其他错误，直接退出命令
                        return;
                        }

                    // 7. 用户成功选择了对象，执行排版并退出
                    using (doc.LockDocument())
                        {
                        var layoutService = new TextLayoutService(doc);
                        layoutService.Execute(selRes.Value, currentLineSpacing);
                        }
                    break; // 完成操作，跳出循环
                    }
                // 8. 捕获我们自己抛出的关键字异常
                catch (KeywordException ex)
                    {
                    if (ex.Input == "H")
                        {
                        // 用户输入了 "H"，进入行间距设置流程
                        using (doc.LockDocument())
                            {
                            var pso = new PromptStringOptions($"\n当前行间距为: {currentLineSpacing}。请输入新值 (或直接回车使用'不指定'): ");
                            pso.AllowSpaces = true;
                            var psr = editor.GetString(pso);

                            if (psr.Status == PromptStatus.OK)
                                {
                                // 更新临时的行间距变量，并立即保存到设置
                                currentLineSpacing = string.IsNullOrWhiteSpace(psr.StringResult) ? "不指定" : psr.StringResult;
                                currentSettings.WzpbLineSpacing = currentLineSpacing;
                                settingsService.SaveSettings(currentSettings);
                                editor.WriteMessage($"\n行间距已更新为: {currentLineSpacing}");
                                }
                            }
                        }
                    else
                        {
                        // 用户输入了无效的关键字
                        editor.WriteMessage($"\n未知关键字: '{ex.Input}'。请输入正确命令。");
                        }
                    // 处理完关键字后，继续下一次循环
                    continue;
                    }
                // 捕获其他所有可能的意外错误
                catch (System.Exception ex)
                    {
                    editor.WriteMessage($"\n执行过程中出错: {ex.Message}");
                    return;
                    }
                }
            Application.SetSystemVariable("SHORTCUTMENU", oldShortcutMenu);
            }


        [CommandMethod("WZXHX")]
        public void AddUnderlineCommand()
            {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
                {
                // 1. 创建服务实例
                var underlineService = new UnderlineService(doc);
                // 2. 创建默认选项实例
                var options = new UnderlineOptions();
                // 3. 执行核心功能
                underlineService.AddUnderlinesToSelectedText(options);
                }
            catch (System.Exception ex)
                {
                doc.Editor.WriteMessage($"\n[ZJXHX] 命令执行时发生顶层错误: {ex.Message}");
                }
            }

        }
    }