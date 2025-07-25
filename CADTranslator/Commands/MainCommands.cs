﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Interop;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using CADTranslator.Models.CAD;
using CADTranslator.Services.CAD;
using CADTranslator.Services.Settings;
using CADTranslator.Services.Translation;
using CADTranslator.Tools.CAD.Jigs;
using CADTranslator.Views;
using Exception = System.Exception;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;


namespace CADTranslator.AutoCAD.Commands
{
    public class MainCommands
    {
        private static TranslatorWindow translatorWindow;

        /// <summary>
        ///     静态构造函数，在类第一次被使用前自动执行，且只执行一次。
        ///     这是注册程序集解析事件的最佳位置。
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
                try
                {
                    // 获取当前正在执行的程序集（也就是我们的插件）所在的目录
                    var assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    // 构造我们实际拥有的那个新版本DLL的完整路径
                    var targetDllPath = Path.Combine(assemblyLocation, "System.Runtime.CompilerServices.Unsafe.dll");

                    // 如果文件存在，就从这个路径加载它
                    if (File.Exists(targetDllPath))
                        // 加载并返回这个程序集，问题解决
                        return Assembly.LoadFrom(targetDllPath);
                }
                catch (Exception ex)
                {
                    // 如果在解析过程中出错，就将错误打印到CAD命令行，方便调试
                    Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[AssemblyResolve] Error: {ex.Message}");
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
                new WindowInteropHelper(translatorWindow).Owner = Application.MainWindow.Handle;
                translatorWindow.Show();
            }
            else
            {
                translatorWindow.Activate();
                if (!translatorWindow.IsVisible)
                    translatorWindow.Show();
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
            var startPoint = ppr1.Value;

            var jig = new BreakLineJig(startPoint);
            var result = editor.Drag(jig);

            if (result.Status == PromptStatus.OK)
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


        [CommandMethod("WZPB")]
        public void TextLayoutCommand()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var editor = doc.Editor;

            // 1. 加载设置
            var settingsService = new SettingsService();
            var currentSettings = settingsService.LoadSettings();
            var currentLineSpacing = currentSettings.WzpbLineSpacing;
            var addUnderline = currentSettings.AddUnderlineAfterWzpb;

            var oldShortcutMenu = Application.GetSystemVariable("SHORTCUTMENU");

            // 2. 启动交互循环
            while (true)
            {
                Application.SetSystemVariable("SHORTCUTMENU", 0);
                try
                {
                    // 3. 创建选择选项
                    var selOpts = new PromptSelectionOptions();
                    var underlineStatus = addUnderline ? "是" : "否";
                    selOpts.MessageForAdding = "\n请选择要重新排版的文字对象";
                    // 4. 添加关键字
                    selOpts.Keywords.Add("H", "H", $"设置行间距(H) (当前: {currentLineSpacing})");
                    selOpts.Keywords.Add("UL", "UL", $"添加下划线(UL) (当前: {underlineStatus})");
                    selOpts.MessageForAdding += selOpts.Keywords.GetDisplayString(true);

                    selOpts.PrepareOptionalDetails = false;

                    // 5. 添加关键字输入事件处理，抛出自定义异常
                    selOpts.KeywordInput += (s, args) => { throw new KeywordException(args.Input.ToUpper()); };

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
                    else if (ex.Input == "UL")
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
                                addUnderline = pkr.StringResult == "Yes";
                                currentSettings.AddUnderlineAfterWzpb = addUnderline;
                                settingsService.SaveSettings(currentSettings);
                                editor.WriteMessage($"\n添加下划线已设置为: {(addUnderline ? "是" : "否")}");
                            }
                        }
                    else
                        editor.WriteMessage($"\n未知关键字: '{ex.Input}'。");

                    // 1. 在清除选择之前，先获取当前所有高亮的对象ID
                    var impliedSelectionResult = editor.SelectImplied();

                    // 2. 清除逻辑上的选择（这步依然需要）
                    editor.SetImpliedSelection(new ObjectId[0]);

                    // 3. 【关键】如果确实存在预选择集，则手动取消这些对象的视觉高亮
                    if (impliedSelectionResult.Status == PromptStatus.OK)
                    {
                        var impliedSelectionSet = impliedSelectionResult.Value;
                        if (impliedSelectionSet != null && impliedSelectionSet.Count > 0)
                            using (var tr = doc.TransactionManager.StartTransaction())
                            {
                                foreach (var id in impliedSelectionSet.GetObjectIds())
                                    // 增加一个try-catch以防止对象ID失效等意外情况
                                    try
                                    {
                                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                                        ent?.Unhighlight(); // 调用 Unhighlight 方法
                                    }
                                    catch
                                    {
                                    } // 忽略单个对象的失败

                                tr.Commit();
                            }
                    }

                    // 4. 给予用户清晰的提示
                    editor.WriteMessage("\n设置已更新，请重新选择文字对象。");
                }
                catch (Exception ex)
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
            catch (Exception ex)
            {
                doc.Editor.WriteMessage($"\n[ZJXHX] 命令执行时发生顶层错误: {ex.Message}");
            }
        }


        // ▼▼▼ 【请用下面的代码块，替换掉旧的 ApplyTranslationLayoutCommand 方法】 ▼▼▼

        /// <summary>
        ///     【这是新的核心逻辑方法】
        ///     一个可被外部直接调用的静态方法，负责执行应用翻译到图纸的完整逻辑。
        /// </summary>
        public static void ExecuteApplyLayoutLogic()
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
            var isLiveLayout = currentSettings.IsLiveLayoutEnabled;
            var lineSpacing = currentSettings.LastSelectedLineSpacing;

            var success = false;
            try
            {
                // 3. 计算需要删除的原始实体ID列表
                var idsToDelete = textBlockList.Where(item => !string.IsNullOrWhiteSpace(item.TranslatedText) && !item.TranslatedText.StartsWith("[")).SelectMany(item => item.SourceObjectIds)
                    .Distinct().ToList();

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
            catch (Exception ex)
            {
                editor.WriteMessage($"\n[错误] 应用到CAD时发生意外异常: {ex.Message}");
                success = false;
            }
            finally
            {
                // 5. 【核心修改】根据操作结果决定是否重新显示WPF窗口
                if (translatorWindow != null)
                    // 只有在操作失败时，才重新显示窗口，以便用户看到错误并进行下一步操作
                    if (!success)
                    {
                        translatorWindow.Show();
                        translatorWindow.Activate();
                    }

                if (success)
                    editor.WriteMessage("\n成功将所有翻译应用到CAD图纸！现在可以查看效果。");
                else
                    editor.WriteMessage("\n[错误] 应用到CAD失败，请检查CAD命令行获取详细信息。");

                // 6. 清理静态变量，避免内存泄漏
                CadBridgeService.TextBlocksToLayout = null;
            }
        }

        /// <summary>
        ///     【这是旧的命令入口，现在只负责调用核心逻辑】
        ///     这个方法依然存在，以确保如果将来有其他地方通过命令行调用 WZPB_APPLY，功能依然有效。
        /// </summary>
        [CommandMethod("WZPB_APPLY")]
        public void ApplyTranslationLayoutCommand()
        {
            ExecuteApplyLayoutLogic();
        }


        [CommandMethod("FYY")]
        public async void TranslateAndReplaceCommand()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var editor = doc.Editor;

            // 1. --- 准备工作 ---
            // 加载设置
            var settingsService = new SettingsService();
            var settings = settingsService.LoadSettings();
            var apiProfile = settings.ApiProfiles.FirstOrDefault(p => p.ServiceType == settings.LastSelectedApiService);

            if (apiProfile == null)
            {
                editor.WriteMessage("\n[错误] 未找到有效的API配置，请先运行 GJX 命令进行设置。");
                return;
            }

            // 创建翻译器实例
            ITranslator translator;
            var apiRegistry = new ApiRegistry();
            try
            {
                translator = apiRegistry.CreateProviderForProfile(apiProfile);
            }
            catch (Exception ex)
            {
                editor.WriteMessage($"\n[错误] 创建翻译服务失败: {ex.Message}");
                return;
            }

            // --- 生成详细的启动提示信息 ---
            var info = new StringBuilder();
            info.Append($"\n接口服务: {translator.DisplayName}   ");

            // 判断接口是否需要模型
            if (translator.IsModelRequired)
            {
                if (!string.IsNullOrWhiteSpace(apiProfile.LastSelectedModel))
                    info.AppendLine($"翻译模型: {apiProfile.LastSelectedModel}");
                else
                    info.AppendLine("翻译模型: [未设置!] - 请先在GJX工具箱中选择模型。");
            }

            // 显示并发设置状态
            if (settings.IsMultiThreadingEnabled)
                info.Append($"多线程模式: 已开启 (多线程数: {settings.LastSelectedConcurrency})    ");
            else
                info.Append("多线程模式: 已关闭    ");
            editor.WriteMessage(info.ToString());

            // 2. --- 获取用户选择 ---
            var selOpts = new PromptSelectionOptions();
            selOpts.MessageForAdding = "此为单行翻译功能，不支持段落识别。请选择你想翻译的文字：";

            var selRes = editor.GetSelection(selOpts);
            if (selRes.Status != PromptStatus.OK) return;

            var selectedIds = selRes.Value.GetObjectIds().ToList();
            if (selectedIds.Count == 0)
            {
                editor.WriteMessage("\n未选择任何有效的文字对象。");
                return;
            }

            editor.WriteMessage($"\n已选择 {selectedIds.Count} 个对象，开始翻译...");
            var successCount = 0;
            var failCount = 0;

            // 3. --- 核心处理逻辑 ---
            var docLock = Application.DocumentManager.MdiActiveDocument.LockDocument();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                foreach (var id in selectedIds)
                {
                    if (id.IsNull || id.IsErased) continue;

                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (!(ent is DBText || ent is MText))
                        continue; // 跳过非文字对象

                    var originalText = ent is DBText dbText ? dbText.TextString : (ent as MText)?.Text;
                    if (string.IsNullOrWhiteSpace(originalText)) continue;

                    string translatedText = null;
                    try
                    {
                        // a. 调用翻译API
                        translatedText = await translator.TranslateAsync(originalText, settings.SourceLanguage, settings.TargetLanguage, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        editor.WriteMessage($"\n[翻译失败] 对象ID {id}: {ex.Message}");
                        continue; // 跳过此对象的替换
                    }

                    if (string.IsNullOrWhiteSpace(translatedText))
                    {
                        failCount++;
                        editor.WriteMessage($"\n[翻译失败] 对象ID {id}: API返回了空内容。");
                        continue;
                    }

                    // b. 创建并替换 (复用我们的“完美替换”逻辑)
                    using (var newText = new DBText())
                    {
                        newText.TextString = translatedText.Replace('\n', ' ').Replace('\r', ' ');
                        newText.SetPropertiesFrom(ent);

                        if (ent is DBText oldDbText)
                        {
                            newText.Height = oldDbText.Height;
                            newText.Rotation = oldDbText.Rotation;
                            newText.Oblique = oldDbText.Oblique;
                            newText.WidthFactor = oldDbText.WidthFactor == 0 ? 1.0 : oldDbText.WidthFactor; // 安全检查
                            newText.TextStyleId = oldDbText.TextStyleId;
                            newText.HorizontalMode = oldDbText.HorizontalMode;
                            newText.VerticalMode = oldDbText.VerticalMode;
                            newText.Position = oldDbText.Position;
                            if (newText.HorizontalMode != TextHorizontalMode.TextLeft || newText.VerticalMode != TextVerticalMode.TextBase)
                                newText.AlignmentPoint = oldDbText.AlignmentPoint;
                        }
                        else if (ent is MText oldMText)
                        {
                            newText.Height = oldMText.TextHeight;
                            newText.Rotation = oldMText.Rotation;
                            newText.TextStyleId = oldMText.TextStyleId;
                            newText.Position = oldMText.Location;
                        }

                        try
                        {
                            modelSpace.AppendEntity(newText);
                            tr.AddNewlyCreatedDBObject(newText, true);
                        }
                        catch (Exception ex)
                        {
                            editor.WriteMessage($"\n[尝试应用翻译失败]   出错信息: {ex.Message}");
                        }


                        if (newText.HorizontalMode != TextHorizontalMode.TextLeft || newText.VerticalMode != TextVerticalMode.TextBase)
                            newText.AdjustAlignment(db);
                    }

                    // c. 删除原始对象
                    var entToErase = tr.GetObject(id, OpenMode.ForWrite);
                    entToErase.Erase();

                    successCount++;
                    editor.WriteMessage($"\r处理进度: {successCount + failCount} / {selectedIds.Count}");
                }

                tr.Commit();
            }

            docLock.Dispose();

            editor.WriteMessage($"\n\n翻译任务完成！成功: {successCount}，失败: {failCount}。");
        }


        [CommandMethod("TEST")]
        public void TestLayoutCommand()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var editor = doc.Editor;

            // 【新增】加载设置
            var settingsService = new SettingsService();
            var settings = settingsService.LoadSettings();

            editor.WriteMessage("\n[TEST] 请选择需要分析的对象...");
            var selRes = editor.GetSelection();
            if (selRes.Status != PromptStatus.OK) return;

            var selectedIds = selRes.Value.GetObjectIds();

            // --- 第1阶段：数据采集 (逻辑不变) ---
            var targets = new List<LayoutTask>();
            var rawObstacles = new List<Entity>();
            var obstacleReportData = new List<Tuple<Extents3d, string>>();
            var preciseObstacles = new List<NtsGeometry>();
            var obstacleIdMap = new Dictionary<ObjectId, NtsGeometry>();

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    // ▼▼▼ 请用下面的循环体替换原有的 for/foreach 循环 ▼▼▼
                    foreach (var id in selectedIds)
                    {
                        if (id.IsNull || id.IsErased)
                            continue;
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        var entityType = id.ObjectClass.DxfName;

                        if (ent is DBText dbText)
                        {
                            if (Regex.IsMatch(dbText.TextString, @"[\u4e00-\u9fa5]"))
                                targets.Add(LayoutTask.From(dbText, tr));
                            rawObstacles.Add(ent);
                            obstacleReportData.Add(new Tuple<Extents3d, string>(ent.GeometricExtents, entityType));
                        }
                        else if (ent is MText mText)
                        {
                            if (Regex.IsMatch(mText.Text, @"[\u4e00-\u9fa5]"))
                                targets.Add(LayoutTask.From(mText));
                            rawObstacles.Add(ent);
                            obstacleReportData.Add(new Tuple<Extents3d, string>(ent.GeometricExtents, entityType));
                        }
                        // 【核心修改】在这里增加了对 Hatch 颜色的判断
                        else if (ent is Hatch hatch)
                        {
                            // 只有当颜色不是 251, 252, 253 时，才视为障碍物
                            if (hatch.ColorIndex != 251 && hatch.ColorIndex != 252 && hatch.ColorIndex != 253)
                            {
                                rawObstacles.Add(ent);
                                obstacleReportData.Add(new Tuple<Extents3d, string>(ent.GeometricExtents, entityType));
                            }
                        }
                        else
                        {
                            rawObstacles.Add(ent);
                            obstacleReportData.Add(new Tuple<Extents3d, string>(ent.GeometricExtents, entityType));
                        }
                    }

                    foreach (var obstacleEntity in rawObstacles)
                    {
                        var ntsGeometries = GeometryConverter.ToNtsGeometry(obstacleEntity).ToList();
                        preciseObstacles.AddRange(ntsGeometries);
                        ntsGeometries.ForEach(g =>
                        {
                            if (!obstacleIdMap.ContainsKey(obstacleEntity.ObjectId))
                                obstacleIdMap[obstacleEntity.ObjectId] = g;
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                editor.WriteMessage($"\n[错误] 在数据采集阶段发生异常: {ex.Message}\n{ex.StackTrace}");
                return;
            }

            // --- 第2阶段：语义分析 (逻辑不变) ---
            editor.WriteMessage("\n数据采集完成，正在启动语义分析引擎...");
            var analyzer = new SemanticAnalyzer(targets, rawObstacles);
            var analyzedTargets = analyzer.AnalyzeAndGroup();
            editor.WriteMessage($"\n语义分析完成！识别出 {analyzedTargets.Count} 个独立的布局任务。");

            // --- 第3阶段：核心计算 ---
            editor.WriteMessage("\n正在启动核心计算引擎...");
            var calculator = new LayoutCalculator();
            // 【核心修改】使用从设置中读取的轮次进行计算
            var summary = calculator.CalculateLayouts(analyzedTargets, rawObstacles, settings.TestNumberOfRounds);
            editor.WriteMessage("\n计算完成！正在准备可视化报告...");

            // --- 第4阶段：生成报告与显示 ---
            var targetsReport = new StringBuilder();
            targetsReport.AppendLine($"成功筛选和分析 {analyzedTargets.Count} 个待处理的中文文字目标：");
            targetsReport.AppendLine("========================================");
            foreach (var task in analyzedTargets)
                targetsReport.Append(task);

            var obstaclesReport = new StringBuilder();
            obstaclesReport.AppendLine($"共分析 {obstacleReportData.Count} 个初始障碍物 (基于边界框)：");
            obstaclesReport.AppendLine("========================================");
            obstacleReportData.ForEach(obs => obstaclesReport.AppendLine($"--- [类型: {obs.Item2}] Min: {obs.Item1.MinPoint}, Max: {obs.Item1.MaxPoint}"));

            var preciseReport = new StringBuilder();
            preciseReport.AppendLine($"成功提取 {preciseObstacles.Count} 个精确几何障碍物：");
            preciseReport.AppendLine("========================================");
            var preciseCount = 1;
            foreach (var geom in preciseObstacles)
                preciseReport.AppendLine($"--- 精确几何 #{preciseCount++} [类型: {geom.GeometryType}] ---");


            Application.DocumentManager.ExecuteInApplicationContext(state =>
            {
                // 【核心修改】将 rawObstacles 传递给窗口
                var resultWindow = new TestResultWindow(analyzedTargets, rawObstacles, obstacleReportData, preciseObstacles, preciseReport.ToString(), obstacleIdMap, summary);
                new WindowInteropHelper(resultWindow).Owner = Application.MainWindow.Handle;
                resultWindow.Show();
            }, null);
        }

        #region 一个自定义异常类，专门用于在GetSelection的事件中传递关键字。

        public class KeywordException : Exception
        {
            public KeywordException(string input)
            {
                Input = input;
            }

            public string Input { get; }
        }

        #endregion
    }
}