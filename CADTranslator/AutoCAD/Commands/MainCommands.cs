using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System;
using System.Text;
using CADTranslator.Models;
using CADTranslator.UI.ViewModels;
using CADTranslator.Services;
using CADTranslator.UI.Views;
using CADTranslator.AutoCAD.Jigs;

namespace CADTranslator.AutoCAD.Commands
{
    public class MainCommands
    {
        private static TranslatorWindow translatorWindow;

        [CommandMethod("GJX")]
        public void LaunchToolbox()
        {
            if (translatorWindow == null || !translatorWindow.IsLoaded)
            {
                translatorWindow = new TranslatorWindow();
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

            try
                {
                // 1. 创建我们的新服务，把当前文档传给它
                var layoutService = new TextLayoutService(doc);

                // 2. 命令服务执行任务
                layoutService.Execute();
                }
            catch (System.Exception ex)
                {
                // 提供一个最终的保障性错误捕获
                doc.Editor.WriteMessage($"\n[WZPB] 命令执行时发生顶层错误: {ex.Message}");
                }
            }
        }
}