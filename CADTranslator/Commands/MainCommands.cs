using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CADTranslator.Models.CAD;
using CADTranslator.Services.CAD;
using CADTranslator.Services.Settings;
using CADTranslator.Tools.CAD.Jigs;
using CADTranslator.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;


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
            bool addUnderline = currentSettings.AddUnderlineAfterWzpb;

            object oldShortcutMenu = Application.GetSystemVariable("SHORTCUTMENU");

            // 2. 启动交互循环
            while (true)
                {
                Application.SetSystemVariable("SHORTCUTMENU", 0);
                try
                    {
                    // 3. 创建选择选项
                    var selOpts = new PromptSelectionOptions();
                    string underlineStatus = addUnderline ? "是" : "否";
                    selOpts.MessageForAdding = "\n请选择要重新排版的文字对象";
                    // 4. 添加关键字
                    selOpts.Keywords.Add("H", "H", $"设置行间距(H) (当前: {currentLineSpacing})");
                    selOpts.Keywords.Add("UL", "UL", $"添加下划线(UL) (当前: {underlineStatus})");
                    selOpts.MessageForAdding += selOpts.Keywords.GetDisplayString(true);

                    selOpts.PrepareOptionalDetails = false;

                    // 5. 添加关键字输入事件处理，抛出自定义异常
                    selOpts.KeywordInput += (s, args) =>
                    {
                        throw new KeywordException(args.Input.ToUpper());
                    };

                    // 6. 获取用户选择
                    var selRes = editor.GetSelection(selOpts);
                    if (selRes.Status != PromptStatus.OK)
                        {
                        Application.SetSystemVariable("SHORTCUTMENU", oldShortcutMenu);
                        return;
                        }

                        // 7. 用户成功选择了对象，执行排版
                        List<ObjectId> newTextIds;
                    using (doc.LockDocument())
                        {
                        var layoutService = new TextLayoutService(doc);
                        newTextIds = layoutService.Execute(selRes.Value, currentLineSpacing);
                        }

                    // 8. 如果需要，则调用新的自动化接口添加下划线
                    if (addUnderline && newTextIds != null && newTextIds.Any())
                        {
                        editor.WriteMessage($"\n正在为新生成的 {newTextIds.Count} 个文本对象添加下划线...");

                        var underlineService = new UnderlineService(doc);
                        var underlineOptions = new UnderlineOptions(); // 使用默认选项

                        // 直接调用我们新增的自动化方法
                        underlineService.AddUnderlinesToObjectIds(newTextIds, underlineOptions);
                        }
                    break; // 完成操作，跳出循环
                    }
                catch (KeywordException ex)
                    {
                    // 9. 捕获关键字异常并处理
                    if (ex.Input == "H")
                        {
                        // (处理 "H" 的逻辑保持不变)
                        using (doc.LockDocument())
                            {
                            var pso = new PromptStringOptions($"\n当前行间距为: {currentLineSpacing}。请输入新值 (或直接回车使用'不指定'): ");
                            pso.AllowSpaces = true;
                            var psr = editor.GetString(pso);

                            if (psr.Status == PromptStatus.OK)
                                {
                                currentLineSpacing = string.IsNullOrWhiteSpace(psr.StringResult) ? "不指定" : psr.StringResult;
                                currentSettings.WzpbLineSpacing = currentLineSpacing;
                                settingsService.SaveSettings(currentSettings);
                                editor.WriteMessage($"\n行间距已更新为: {currentLineSpacing}");
                                }
                            }
                        }
                    else if (ex.Input == "UL")
                        {
                        // (处理 "U" 的逻辑)
                        using (doc.LockDocument())
                            {
                            var pko = new PromptKeywordOptions($"\n是否在排版后添加下划线? [是(Yes)/否(No)] 当前：<{(addUnderline ? "Y" : "N")}>，默认：Y  : ");
                            pko.Keywords.Add("Yes", "Y", "是(Y)");
                            pko.Keywords.Add("No", "N", "否(N)");
                            pko.AllowNone = true; // 允许用户直接回车
                            var pkr = editor.GetKeywords(pko);

                            // 如果用户按了ESC，就什么都不做
                            if (pkr.Status == PromptStatus.Cancel)
                                {
                                // continue to the next loop iteration
                                }
                            // 如果用户直接回车，结果是 pkr.Default
                            else if (string.IsNullOrEmpty(pkr.StringResult))
                                {
                                addUnderline = true;
                                currentSettings.AddUnderlineAfterWzpb = addUnderline;
                                settingsService.SaveSettings(currentSettings);
                                editor.WriteMessage($"\n添加下划线已设置为: {(addUnderline ? "是" : "否")}");
                                }
                            // 如果用户输入了关键字
                            else if (pkr.Status == PromptStatus.OK)
                                {
                                addUnderline = (pkr.StringResult == "Yes");
                                currentSettings.AddUnderlineAfterWzpb = addUnderline;
                                settingsService.SaveSettings(currentSettings);
                                editor.WriteMessage($"\n添加下划线已设置为: {(addUnderline ? "是" : "否")}");
                                }
                            }
                        }
                    else
                        {
                        editor.WriteMessage($"\n未知关键字: '{ex.Input}'。");
                        }
                    continue; // 处理完关键字后，继续下一次循环
                    }
                catch (System.Exception ex)
                    {
                    editor.WriteMessage($"\n执行过程中出错: {ex.Message}\n{ex.StackTrace}");
                    Application.SetSystemVariable("SHORTCUTMENU", oldShortcutMenu);
                    return;
                    }
                }
            Application.SetSystemVariable("SHORTCUTMENU", oldShortcutMenu);
            }

        private void SelOpts_KeywordInput(object sender, SelectionTextInputEventArgs e)
            {
            throw new NotImplementedException();
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


        [CommandMethod("WZPB_APPLY")]
        public void ApplyTranslationLayoutCommand()
            {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var editor = doc.Editor;

            // 1. 从静态桥梁中获取ViewModel传递过来的数据
            var textBlockList = CadBridgeService.TextBlocksToLayout;

            if (textBlockList == null || !textBlockList.Any())
                {
                // 如果没有数据，可能是异常情况，直接返回
                // 并且重新显示WPF窗口，让用户可以继续操作
                if (translatorWindow != null)
                    {
                    translatorWindow.Show();
                    translatorWindow.Activate();
                    }
                return;
                }

            // 2. 加载设置
            var settingsService = new SettingsService();
            var currentSettings = settingsService.LoadSettings();
            bool isLiveLayout = currentSettings.IsLiveLayoutEnabled;
            string lineSpacing = currentSettings.LastSelectedLineSpacing;

            bool success = false;
            try
                {
                // 3. 计算需要删除的原始实体ID列表
                var idsToDelete = textBlockList
                    .Where(item => !string.IsNullOrWhiteSpace(item.TranslatedText) && !item.TranslatedText.StartsWith("["))
                    .SelectMany(item => item.SourceObjectIds)
                    .Distinct()
                    .ToList();

                // 4. 创建布局服务实例并执行
                var layoutService = new CadLayoutService(doc);

                if (isLiveLayout)
                    {
                    editor.WriteMessage("\n“实时排版”已启用，将执行智能布局...");
                    success = layoutService.ApplySmartLayoutToCad(textBlockList, idsToDelete, lineSpacing);
                    }
                else
                    {
                    editor.WriteMessage("\n“实时排版”已关闭，将使用基本布局...");
                    success = layoutService.ApplyTranslationToCad(textBlockList, idsToDelete);
                    }
                }
            catch (System.Exception ex)
                {
                editor.WriteMessage($"\n[错误] 应用到CAD时发生意外异常: {ex.Message}");
                success = false;
                }
            finally
                {
                // 5. 【核心修改】根据操作结果决定是否重新显示WPF窗口
                if (translatorWindow != null)
                    {
                    // 只有在操作失败时，才重新显示窗口，以便用户看到错误并进行下一步操作
                    if (!success)
                        {
                        translatorWindow.Show();
                        translatorWindow.Activate();
                        }
                    }

                if (success)
                    {
                    editor.WriteMessage("\n成功将所有翻译应用到CAD图纸！现在可以查看效果。");
                    }
                else
                    {
                    editor.WriteMessage("\n[错误] 应用到CAD失败，请检查CAD命令行获取详细信息。");
                    }

                // 6. 清理静态变量，避免内存泄漏
                CadBridgeService.TextBlocksToLayout = null;
                }
            }

        }
    }