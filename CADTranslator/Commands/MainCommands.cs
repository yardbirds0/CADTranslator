using System;
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
using Autodesk.AutoCAD.Geometry;
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
        public async void TestLayoutCommand()
            {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var editor = doc.Editor;

            var settingsService = new SettingsService();
            var settings = settingsService.LoadSettings();

            // 新增一个标志，用于决定是否执行翻译
            bool useTranslation = settings.TestModeUsesTranslation;

            while (true)
                {
                try
                    {
                    var selOpts = new PromptSelectionOptions();
                    var translationStatus = useTranslation ? "翻译排版" : "原文排版";
                    selOpts.MessageForAdding = $"\n请选择要分析的对象 (当前模式: {translationStatus})";

                    selOpts.Keywords.Add("FY", "FY", "切换排版模式(FY)");
                    selOpts.MessageForAdding += selOpts.Keywords.GetDisplayString(true);

                    selOpts.KeywordInput += (s, args) => { throw new KeywordException(args.Input.ToUpper()); };

                    var selRes = editor.GetSelection(selOpts);
                    if (selRes.Status != PromptStatus.OK) return;

                    var selectedIds = selRes.Value.GetObjectIds();

                    // --- 数据采集 ---
                    var targets = new List<LayoutTask>();
                    var rawObstacles = new List<Entity>();
                    var obstacleReportData = new List<Tuple<Extents3d, string>>();
                    var preciseObstacles = new List<NtsGeometry>();
                    var obstacleIdMap = new Dictionary<ObjectId, NtsGeometry>();

                    using (var tr = db.TransactionManager.StartTransaction())
                        {
                        foreach (var id in selectedIds)
                            {
                            if (id.IsNull || id.IsErased) continue;
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
                            else if (ent is Hatch hatch)
                                {
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

                    // --- 语义分析 ---
                    editor.WriteMessage("\n数据采集完成，正在启动语义分析引擎...");
                    var analyzer = new SemanticAnalyzer(targets, rawObstacles);
                    var analyzedTargets = analyzer.AnalyzeAndGroup();
                    editor.WriteMessage($"\n语义分析完成！识别出 {analyzedTargets.Count} 个独立的布局任务。");

                    // --- 【核心新增】翻译流程 ---
                    if (useTranslation)
                        {
                        editor.WriteMessage("\n翻译模式已激活，正在准备翻译...");
                        var apiProfile = settings.ApiProfiles.FirstOrDefault(p => p.ServiceType == settings.LastSelectedApiService);
                        if (apiProfile == null)
                            {
                            editor.WriteMessage("\n[错误] 未找到有效的API配置，请先运行 GJX 命令进行设置。");
                            continue;
                            }

                        var apiRegistry = new ApiRegistry();
                        ITranslator translator = apiRegistry.CreateProviderForProfile(apiProfile);

                        editor.WriteMessage($"\n正在使用“{translator.DisplayName}”进行翻译，请稍候...");
                        var translationTasks = analyzedTargets.Select(async task =>
                        {
                            try
                                {
                                task.TranslatedText = await translator.TranslateAsync(task.OriginalText, settings.SourceLanguage, settings.TargetLanguage, CancellationToken.None);
                                }
                            catch (Exception ex)
                                {
                                task.TranslatedText = $"[翻译失败] {ex.Message}";
                                }
                        }).ToList();

                        await System.Threading.Tasks.Task.WhenAll(translationTasks);
                        editor.WriteMessage("\n翻译完成！");
                        }


                    // --- 核心计算 ---
                    editor.WriteMessage("\n正在启动核心计算引擎...");
                    var calculator = new LayoutCalculator();
                    var summary = calculator.CalculateLayouts(analyzedTargets, rawObstacles, settings.TestNumberOfRounds);
                    editor.WriteMessage("\n计算完成！正在准备可视化报告...");

                    var preciseReport = new StringBuilder();
                    preciseReport.AppendLine($"成功提取 {preciseObstacles.Count} 个精确几何障碍物：");
                    preciseReport.AppendLine("========================================");
                    var preciseCount = 1;
                    foreach (var geom in preciseObstacles)
                        preciseReport.AppendLine($"--- 精确几何 #{preciseCount++} [类型: {geom.GeometryType}] ---");

                    Application.DocumentManager.ExecuteInApplicationContext(state =>
                    {
                        var resultWindow = new TestResultWindow(analyzedTargets, rawObstacles, obstacleReportData, preciseObstacles, preciseReport.ToString(), obstacleIdMap, summary);
                        new WindowInteropHelper(resultWindow).Owner = Application.MainWindow.Handle;
                        resultWindow.Show();
                    }, null);

                    break; // 成功执行，跳出循环
                    }
                catch (KeywordException ex)
                    {
                    if (ex.Input == "FY")
                        {
                        var pko = new PromptKeywordOptions($"\n请选择排版模式 [原文(Original)/翻译(Translate)] <{(useTranslation ? "T" : "O")}>: ");
                        pko.Keywords.Add("Original", "O", "使用原文(O)");
                        pko.Keywords.Add("Translate", "T", "使用翻译(T)");
                        pko.AllowNone = true;

                        var pkr = editor.GetKeywords(pko);
                        if (pkr.Status == PromptStatus.OK)
                            {
                            if (string.IsNullOrEmpty(pkr.StringResult)) // 用户回车
                                {
                                useTranslation = !useTranslation;
                                }
                            else // 用户输入关键字
                                {
                                useTranslation = pkr.StringResult == "Translate";
                                }
                            settings.TestModeUsesTranslation = useTranslation;
                            settingsService.SaveSettings(settings);
                            editor.WriteMessage($"\n排版模式已切换为: {(useTranslation ? "翻译排版" : "原文排版")}");
                            }
                        }
                    editor.SetImpliedSelection(new ObjectId[0]);
                    }
                catch (Exception ex)
                    {
                    editor.WriteMessage($"\n[错误] 在TEST命令执行期间发生异常: {ex.Message}\n{ex.StackTrace}");
                    return;
                    }
                }
            }

        [CommandMethod("WZKD")]
        public void AdjustWidthFactorCommand()
            {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var editor = doc.Editor;

            // 1. 提示用户选择对象
            var selOpts = new PromptSelectionOptions
                {
                MessageForAdding = "\n请选择要统一调整宽度因子的文字对象: "
                };
            var selRes = editor.GetSelection(selOpts);
            if (selRes.Status != PromptStatus.OK) return;

            // 2. 筛选出有效的文字对象ID
            var textEntityIds = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
                {
                foreach (var id in selRes.Value.GetObjectIds())
                    {
                    var ent = tr.GetObject(id, OpenMode.ForRead);
                    if (ent is DBText || ent is MText)
                        {
                        textEntityIds.Add(id);
                        }
                    }
                tr.Commit();
                }

            if (textEntityIds.Count == 0)
                {
                editor.WriteMessage("\n选择中未找到任何有效的文字对象。");
                return;
                }

            // 3. 启动Jig进行交互
            var jig = new WidthFactorJig(doc, textEntityIds);
            var result = editor.Drag(jig);

            // 4. 用户确认后，应用最终的宽度因子
            if (result.Status == PromptStatus.OK)
                {
                using (var tr = db.TransactionManager.StartTransaction())
                    {
                    foreach (var id in textEntityIds)
                        {
                        var ent = tr.GetObject(id, OpenMode.ForWrite);
                        if (ent is DBText dbText)
                            {
                            dbText.WidthFactor = jig.FinalWidthFactor;
                            }
                        // MText的宽度因子是只读的，但Jig的预览已经模拟了效果
                        // 实际应用时，对于MText我们不做修改，以保持其原始属性
                        }
                    tr.Commit();
                    editor.WriteMessage($"\n已成功将 {textEntityIds.Count} 个文字对象的宽度因子统一调整为 {jig.FinalWidthFactor:F2}。");
                    }
                }
            }

        [CommandMethod("WZHJJ")]
        public void AdjustLineSpacingCommand()
            {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var editor = doc.Editor;

            // 1. 提示用户选择对象
            var selOpts = new PromptSelectionOptions
                {
                MessageForAdding = "\n请选择要统一调整行间距的文字对象: "
                };
            var selRes = editor.GetSelection(selOpts);
            if (selRes.Status != PromptStatus.OK) return;

            // 2. 筛选出有效的文字对象ID
            var textEntityIds = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
                {
                foreach (var id in selRes.Value.GetObjectIds())
                    {
                    var ent = tr.GetObject(id, OpenMode.ForRead);
                    if (ent is DBText || ent is MText)
                        {
                        textEntityIds.Add(id);
                        }
                    }
                tr.Commit();
                }

            if (textEntityIds.Count < 2)
                {
                editor.WriteMessage("\n请至少选择两个文字对象以调整行间距。");
                return;
                }

            // 3. 启动Jig进行交互
            var jig = new LineSpacingJig(doc, textEntityIds);
            var result = editor.Drag(jig);

            // 4. 用户确认后，应用最终计算出的新位置
            if (result.Status == PromptStatus.OK)
                {
                using (var tr = db.TransactionManager.StartTransaction())
                    {
                    foreach (var pair in jig.FinalPositions)
                        {
                        var ent = tr.GetObject(pair.Key, OpenMode.ForWrite);
                        if (ent is DBText dbText)
                            {
                            // 【核心修正】智能判断：保留原始对齐方式
                            if (dbText.HorizontalMode == TextHorizontalMode.TextLeft && dbText.VerticalMode == TextVerticalMode.TextBase)
                                {
                                // 对左对齐文字，直接设置Position
                                dbText.Position = pair.Value;
                                }
                            else
                                {
                                // 对非左对齐文字，设置AlignmentPoint并让CAD自动调整
                                dbText.AlignmentPoint = pair.Value;
                                dbText.AdjustAlignment(db); // <-- 关键！
                                }
                            }
                        else if (ent is MText mText)
                            {
                            mText.Location = pair.Value;
                            }
                        }
                    tr.Commit();
                    editor.WriteMessage($"\n已成功为 {textEntityIds.Count} 个文字对象统一调整了行间距。");
                    }
                }
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
        [CommandMethod("TEST_APPLY")]
        public void ApplyTestLayoutCommand()
            {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var editor = doc.Editor;

            var tasksToApply = CadBridgeService.LayoutTasksToApply;
            if (tasksToApply == null || !tasksToApply.Any())
                {
                editor.WriteMessage("\n[错误] 未找到从预览窗口传递过来的布局数据。");
                return;
                }

            try
                {
                using (doc.LockDocument())
                    {
                    using (var tr = db.TransactionManager.StartTransaction())
                        {
                        var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                        foreach (var task in tasksToApply)
                            {
                            // 如果任务没有有效位置，或者翻译失败，则跳过
                            if (!task.CurrentUserPosition.HasValue || string.IsNullOrEmpty(task.TranslatedText) || task.TranslatedText.StartsWith("[翻译失败]"))
                                {
                                continue;
                                }

                            // 【核心修正】将预览文本按换行符分割成多行
                            var lines = task.TranslatedText.Split('\n');
                            var currentPosition = task.CurrentUserPosition.Value;

                            // 获取用于样式继承的模板实体
                            var templateEntity = tr.GetObject(task.ObjectId, OpenMode.ForRead) as Entity;
                            if (templateEntity == null) continue;

                            // 【核心修正】遍历每一行，为每一行都创建一个独立的DBText
                            for (int i = 0; i < lines.Length; i++)
                                {
                                string lineText = lines[i].Trim(); // 去除可能的前后空格
                                if (string.IsNullOrEmpty(lineText)) continue;

                                using (var newText = new DBText())
                                    {
                                    newText.TextString = lineText;
                                    newText.Height = task.Height;
                                    newText.Rotation = task.Rotation;
                                    newText.Oblique = task.Oblique;
                                    newText.WidthFactor = task.WidthFactor;
                                    newText.TextStyleId = task.TextStyleId;

                                    // 【重要】设置新一行的位置
                                    // 第一行的位置就是任务的当前位置
                                    // 后续行在此基础上，Y坐标向下偏移 (行高 * 1.5)
                                    newText.Position = new Point3d(currentPosition.X, currentPosition.Y - (i * task.Height * 1.2) - task.Height, currentPosition.Z);

                                    // 继承图层、颜色等属性
                                    newText.SetPropertiesFrom(templateEntity);

                                    modelSpace.AppendEntity(newText);
                                    tr.AddNewlyCreatedDBObject(newText, true);
                                    }
                                }
                            }

                        tr.Commit();
                        }
                    }

                int successCount = tasksToApply.Count(t => t.CurrentUserPosition.HasValue && !string.IsNullOrEmpty(t.TranslatedText) && !t.TranslatedText.StartsWith("[翻译失败]"));
                editor.WriteMessage($"\n成功应用 {successCount} 个翻译布局到图纸！原始文本已保留。");
                }
            catch (Exception ex)
                {
                editor.WriteMessage($"\n[错误] 应用布局到CAD时发生意外异常: {ex.Message}\n{ex.StackTrace}");
                }
            finally
                {
                CadBridgeService.LayoutTasksToApply = null;
                Application.MainWindow.Focus();
                }
            }
        #endregion
        }
    }