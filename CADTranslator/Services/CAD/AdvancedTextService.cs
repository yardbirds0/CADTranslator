using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models;
using CADTranslator.Models.CAD;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CADTranslator.Services.CAD
    {
    /// <summary>
    /// 高级文本分析服务。
    /// 此版本假定调用方已处理 DocumentLock，因此内部使用单个事务处理所有读写操作。
    /// </summary>
    public class AdvancedTextService : IAdvancedTextService
        {
        public const string LegendPlaceholder = "__LEGEND_POS__";
        public const string JigPlaceholder = "*图例位置*";
        private readonly Database _db;
        private readonly Editor _editor;

        public AdvancedTextService(Document doc)
            {
            _db = doc.Database;
            _editor = doc.Editor;
            }

        public List<ParagraphInfo> ExtractAndProcessParagraphs(SelectionSet selSet, double similarityThreshold)
            {
            var paragraphInfos = new List<ParagraphInfo>();

            // 因为外部已经加锁，所以我们可以安全地开启一个包含读写的事务
            using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                try
                    {
                    // 1. 在事务开始时就用写模式打开块表，获得最高权限
                    BlockTable bt = (BlockTable)tr.GetObject(_db.BlockTableId, OpenMode.ForWrite);

                    // 2. 分类实体
                    var (textEntities, graphicEntities) = ClassifyEntities(selSet, tr);
                    if (textEntities.Count == 0) return paragraphInfos;
                    var availableGraphics = new List<Entity>(graphicEntities);

                    // 3. 合并文本 (核心修改：传入相似度阈值)
                    List<TextBlock> rawTextBlocks = MergeRawText(textEntities, similarityThreshold);

                    // 4. 处理段落、图例，并执行“占位符注入”
                    string specialPattern = @"\s{3,}";

                    foreach (var block in rawTextBlocks)
                        {
                        var paraInfo = new ParagraphInfo();
                        paraInfo.OriginalText = block.OriginalText; // 始终保存一份未经修改的原文
                        paraInfo.IsTitle = block.IsTitle;
                        paraInfo.GroupKey = block.GroupKey;

                        var firstId = block.SourceObjectIds.FirstOrDefault(id => !id.IsNull && !id.IsErased);
                        if (firstId.IsNull) continue;

                        var templateEntity = tr.GetObject(firstId, OpenMode.ForRead) as Entity;
                        if (templateEntity == null) continue;

                        // 完整捕获原始文本的几何状态
                        if (templateEntity is DBText dbText)
                            {
                            paraInfo.Height = dbText.Height;
                            paraInfo.WidthFactor = dbText.WidthFactor;
                            paraInfo.TextStyleId = dbText.TextStyleId;
                            paraInfo.Position = dbText.Position;
                            paraInfo.AlignmentPoint = dbText.AlignmentPoint;
                            paraInfo.HorizontalMode = dbText.HorizontalMode;
                            paraInfo.VerticalMode = dbText.VerticalMode;
                            }
                        else if (templateEntity is MText mText)
                            {
                            paraInfo.Height = mText.TextHeight;
                            paraInfo.WidthFactor = 1.0;
                            paraInfo.TextStyleId = mText.TextStyleId;
                            paraInfo.Position = mText.Location;
                            paraInfo.AlignmentPoint = mText.Location;
                            paraInfo.HorizontalMode = TextHorizontalMode.TextLeft;
                            paraInfo.VerticalMode = TextVerticalMode.TextTop;
                            }

                        var match = Regex.Match(block.OriginalText, specialPattern);
                        if (match.Success)
                            {
                            // a. 记录原始空格数量
                            paraInfo.OriginalSpaceCount = match.Length;

                            // b. 计算“旧”锚点
                            string textBeforeSpaces = block.OriginalText.Substring(0, match.Index);
                            paraInfo.OriginalAnchorPoint = GetTextEndPoint(textBeforeSpaces, paraInfo, tr);

                            // c. 关联图形并创建块
                            var (associatedGraphics, _) = FindAssociatedGraphics(block.SourceObjectIds, availableGraphics, tr); // <-- 注意：这里要用 availableGraphics
                            if (associatedGraphics.Any())
                                {
                                paraInfo.AssociatedGraphicsBlockId = CreateAnonymousBlockForGraphics(associatedGraphics, paraInfo.OriginalAnchorPoint, tr, bt);

                                // 将找到的原始图例的ID，也加入到这个段落的“源对象ID列表”中，以便后续统一删除
                                paraInfo.SourceObjectIds.AddRange(associatedGraphics.Select(g => g.ObjectId));

                                // ▼▼▼ 在这里添加新代码 ▼▼▼
                                // 【核心修正】将“已用”的图形从“素材库”中移除
                                foreach (var usedGraphic in associatedGraphics)
                                    {
                                    availableGraphics.Remove(usedGraphic);
                                    }
                                }

                            // d.【核心】执行“占位符注入”，生成用于翻译的文本
                            paraInfo.Text = new Regex(specialPattern).Replace(block.OriginalText, LegendPlaceholder, 1);
                            paraInfo.ContainsSpecialPattern = true;
                            }
                        else
                            {
                            // 如果没有图例，Text属性就等于未经修改的原文
                            paraInfo.Text = block.OriginalText;
                            paraInfo.ContainsSpecialPattern = false;
                            }

                        paraInfo.SourceObjectIds.AddRange(block.SourceObjectIds);
                        paragraphInfos.Add(paraInfo);
                        }

                    tr.Commit();
                    }
                catch (System.Exception ex)
                    {
                    _editor.WriteMessage($"\n处理文本时出错: {ex.Message}\n{ex.StackTrace}");
                    tr.Abort();
                    }
                }
            return paragraphInfos;
            }

        public List<string> GetOriginalTextsByIds(List<ObjectId> ids)
            {
            var texts = new List<string>();
            if (ids == null || !ids.Any())
                {
                return texts;
                }

            // 使用一个事务来安全地从数据库读取信息
            using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                foreach (var id in ids)
                    {
                    if (id.IsNull || id.IsErased) continue;

                    try
                        {
                        var entity = tr.GetObject(id, OpenMode.ForRead);
                        string text = string.Empty;

                        if (entity is DBText dbText)
                            {
                            text = dbText.TextString;
                            }
                        else if (entity is MText mText)
                            {
                            text = mText.Text;
                            }

                        // 只添加非空的文本
                        if (!string.IsNullOrWhiteSpace(text))
                            {
                            texts.Add(text.Trim());
                            }
                        }
                    catch (System.Exception ex)
                        {
                        // 如果某个ID无效或无法读取，记录错误并继续
                        _editor.WriteMessage($"\n[GetOriginalTextsByIds] 读取文本时出错: {ex.Message}");
                        }
                    }
                tr.Commit(); // 只读操作，Commit 和 Abort 效果一样
                }

            return texts;
            }
        // --- 辅助方法现在都接收事务作为参数 ---

        private Point3d GetTextEndPoint(string text, ParagraphInfo paraInfo, Transaction tr)
            {
            Point3d endPoint = Point3d.Origin; // 初始化一个默认值

            // 创建一个临时DBText对象来精确模拟原始状态
            using (var tempText = new DBText())
                {
                // 1. 设置文本内容和基本样式
                tempText.TextString = text;
                tempText.Height = paraInfo.Height;
                tempText.WidthFactor = paraInfo.WidthFactor;
                tempText.TextStyleId = paraInfo.TextStyleId;

                // 2.【核心】应用原始的对齐属性
                tempText.HorizontalMode = paraInfo.HorizontalMode;
                tempText.VerticalMode = paraInfo.VerticalMode;

                // 3.【核心】根据不同的对齐方式，设置Position或AlignmentPoint
                if (tempText.HorizontalMode == TextHorizontalMode.TextLeft && tempText.VerticalMode == TextVerticalMode.TextBase)
                    {
                    // 对于默认的左下角对齐，我们设置Position
                    tempText.Position = paraInfo.Position;
                    }
                else
                    {
                    // 对于所有其他对齐方式，我们必须设置AlignmentPoint
                    tempText.AlignmentPoint = paraInfo.AlignmentPoint;
                    }

                // 4.【关键步骤】将临时文本添加到数据库并调整对齐，以获取正确的几何边界
                var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);
                modelSpace.AppendEntity(tempText);
                tr.AddNewlyCreatedDBObject(tempText, true);

                // 让CAD根据对齐属性，自动计算其准确的几何位置
                tempText.AdjustAlignment(_db);

                // 5. 获取计算后的精确边界
                if (tempText.Bounds.HasValue)
                    {
                    endPoint = tempText.Bounds.Value.MaxPoint;
                    }

                // 6. 任务完成，从数据库中擦除这个临时对象
                tempText.Erase();
                }

            return endPoint;
            }

        private ObjectId CreateAnonymousBlockForGraphics(List<Entity> graphics, Point3d basePoint, Transaction tr, BlockTable bt)
            {
            // 使用您在 TextLayoutService.cs 中已经验证成功的命名方式
            BlockTableRecord btr = new BlockTableRecord();
            btr.Name = "TEMP_GRAPHIC_GROUP_" + Guid.NewGuid().ToString("N");
            btr.Origin = basePoint;

            int count = 0;
            foreach (var ent in graphics)
                {
                try
                    {
                    var clone = ent.Clone() as Entity;
                    if (clone != null)
                        {
                        btr.AppendEntity(clone);
                        count++;
                        }
                    }
                catch (System.Exception ex)
                    {
                    _editor.WriteMessage($"\n警告: 无法克隆一个图形实体: {ex.Message}");
                    }
                }

            if (count == 0) return ObjectId.Null;

            ObjectId blockId = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);
            return blockId;
            }

        private static double CalculateSimilarity(string s1, string s2)
            {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

            var maxLength = Math.Max(s1.Length, s2.Length);
            if (maxLength == 0) return 1.0;

            var d = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++)
                {
                d[i, 0] = i;
                }

            for (int j = 0; j <= s2.Length; j++)
                {
                d[0, j] = j;
                }

            for (int i = 1; i <= s1.Length; i++)
                {
                for (int j = 1; j <= s2.Length; j++)
                    {
                    int cost = (s2[j - 1] == s1[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                    }
                }

            return 1.0 - ((double)d[s1.Length, s2.Length] / maxLength);
            }

        // 其他不涉及写操作的辅助方法保持原样
        private (List<TextEntityInfo> textEntities, List<Entity> graphicEntities) ClassifyEntities(SelectionSet selSet, Transaction tr)
            {
            var textEntities = new List<TextEntityInfo>();
            var graphicEntities = new List<Entity>();
            foreach (SelectedObject selObj in selSet)
                {
                var ent = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                if (ent is DBText dbText) textEntities.Add(new TextEntityInfo { ObjectId = ent.ObjectId, Text = dbText.TextString, Position = dbText.Position, Height = dbText.Height });
                else if (ent is MText mText) textEntities.Add(new TextEntityInfo { ObjectId = ent.ObjectId, Text = mText.Text, Position = mText.Location, Height = mText.TextHeight });
                else graphicEntities.Add(ent);
                }
            return (textEntities, graphicEntities);
            }

        private List<TextBlock> MergeRawText(List<TextEntityInfo> textEntities, double similarityThreshold)
            {
            var sortedEntities = textEntities.OrderBy(e => -e.Position.Y).ThenBy(e => e.Position.X).ToList();
            var textBlocks = new List<TextBlock>();
            if (sortedEntities.Count == 0) return textBlocks;

            var titleMarkers = new Regex(@"(说明|注意|技术要求|参数|示例|NOTES|SPECIFICATION|LEGEND)[\s:：]*$|:$", RegexOptions.IgnoreCase);
            var paragraphMarkers = new Regex(@"^\s*(?:[<(（【〔](?:\d+|[a-zA-Z])[>)）】〕]|\d+[、.]|[\u25A0\u25CF\u25B2\u25B6]|[a-zA-Z][、.])");

            var firstEntity = sortedEntities.First();
            var firstBlock = new TextBlock { Id = 1, OriginalText = firstEntity.Text.Trim(), SourceObjectIds = { firstEntity.ObjectId } };
            if (titleMarkers.IsMatch(firstBlock.OriginalText))
                {
                firstBlock.IsTitle = true;
                }
            textBlocks.Add(firstBlock);

            string currentGroupKey = null;

            for (int i = 1; i < sortedEntities.Count; i++)
                {
                var prevEntity = sortedEntities[i - 1];
                var currEntity = sortedEntities[i];
                string currentText = currEntity.Text.Trim();
                if (string.IsNullOrEmpty(currentText)) continue;

                var lastBlock = textBlocks.Last();

                // 规则1: 物理距离过远 -> 强制分割
                bool isTooFar = (prevEntity.Position.Y - currEntity.Position.Y) > (prevEntity.Height * 2.0);
                if (isTooFar)
                    {
                    textBlocks.Add(new TextBlock { Id = textBlocks.Count + 1, OriginalText = currentText, SourceObjectIds = { currEntity.ObjectId } });
                    currentGroupKey = null;
                    continue;
                    }

                // 规则2: 显式序号开头 -> 强制分割
                bool startsNewParagraph = paragraphMarkers.IsMatch(currentText);
                if (startsNewParagraph)
                    {
                    textBlocks.Add(new TextBlock { Id = textBlocks.Count + 1, OriginalText = currentText, SourceObjectIds = { currEntity.ObjectId } });
                    currentGroupKey = null;
                    continue;
                    }

                // ▼▼▼ 【核心修改】智能判断逻辑升级为“双轨制” ▼▼▼
                // 我们现在同时使用两种算法进行判断

                // 算法1: 计算原始的莱文斯坦距离相似度
                double levenshteinSimilarity = CalculateSimilarity(lastBlock.OriginalText, currentText);

                // 算法2: 计算新的结构化相似度 (LCS)
                double structuralSimilarity = CalculateStructuralSimilarity(lastBlock.OriginalText, currentText);

                // 如果【任何一个】算法认为相似度足够高，就执行分割和分组
                if (levenshteinSimilarity > similarityThreshold || structuralSimilarity > similarityThreshold)
                    {
                    // 相似度高 -> 认为是并列项，进行分割和分组
                    if (currentGroupKey == null)
                        {
                        currentGroupKey = Guid.NewGuid().ToString();
                        lastBlock.GroupKey = currentGroupKey;
                        }
                    var newGroupedBlock = new TextBlock { Id = textBlocks.Count + 1, OriginalText = currentText, SourceObjectIds = { currEntity.ObjectId }, GroupKey = currentGroupKey };
                    textBlocks.Add(newGroupedBlock);
                    }
                else
                    {
                    // 两个算法都认为相似度低 -> 认为是普通换行，进行合并
                    currentGroupKey = null;
                    bool onSameLine = Math.Abs(prevEntity.Position.Y - currEntity.Position.Y) < (prevEntity.Height * 0.5);
                    string separator = onSameLine ? " " : "";
                    lastBlock.OriginalText += separator + currentText;
                    lastBlock.SourceObjectIds.Add(currEntity.ObjectId);
                    }
                // ▲▲▲ 修改结束 ▲▲▲
                }

            return textBlocks;
            }
        private (List<Entity> associatedGraphics, Extents3d paragraphBounds) FindAssociatedGraphics(List<ObjectId> textObjectIds, List<Entity> allGraphics, Transaction tr)
            {
            var pBounds = new Extents3d(Point3d.Origin, Point3d.Origin);
            double maxLineHeight = 0;
            foreach (var textId in textObjectIds)
                {
                var textEnt = tr.GetObject(textId, OpenMode.ForRead) as Entity;
                if (textEnt?.Bounds == null) continue;
                if (pBounds.MinPoint == Point3d.Origin) pBounds = textEnt.Bounds.Value;
                else pBounds.AddExtents(textEnt.Bounds.Value);
                if (textEnt is DBText dbt) maxLineHeight = Math.Max(maxLineHeight, dbt.Height);
                else if (textEnt is MText mt) maxLineHeight = Math.Max(maxLineHeight, mt.TextHeight);
                }
            var associatedGraphics = allGraphics.Where(g => g?.Bounds != null && pBounds.MinPoint.X <= g.Bounds.Value.MinPoint.X && (pBounds.MinPoint.Y - maxLineHeight) <= g.Bounds.Value.MinPoint.Y && pBounds.MaxPoint.X >= g.Bounds.Value.MaxPoint.X && (pBounds.MaxPoint.Y + maxLineHeight) >= g.Bounds.Value.MaxPoint.Y).ToList();
            return (associatedGraphics, pBounds);
            }

        #region --- 结构相似度计算 (LCS 算法) ---

        /// <summary>
        /// 【新增方法】使用基于词语的“最长公共子序列”(LCS)算法，计算两个句子的结构相似度。
        /// </summary>
        /// <param name="s1">第一个句子</param>
        /// <param name="s2">第二个句子</param>
        /// <returns>一个 0.0 到 1.0 之间的相似度分数</returns>
        private double CalculateStructuralSimilarity(string s1, string s2)
            {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0.0;

            // 步骤 1: 分词 (Tokenization)
            // 这个正则表达式会把连续的字母/数字/特定符号视为一个词，其他所有字符（汉字、标点）都单个作为词。
            var tokenizer = new Regex(@"[a-zA-Z0-9\.-]+|%%[a-zA-Z0-9@]+|[^\s]");
            var tokens1 = tokenizer.Matches(s1).Cast<Match>().Select(m => m.Value).ToList();
            var tokens2 = tokenizer.Matches(s2).Cast<Match>().Select(m => m.Value).ToList();

            if (tokens1.Count == 0 || tokens2.Count == 0) return 0.0;

            // 步骤 2: 使用动态规划计算LCS的长度
            int[,] dp = new int[tokens1.Count + 1, tokens2.Count + 1];
            for (int i = 1; i <= tokens1.Count; i++)
                {
                for (int j = 1; j <= tokens2.Count; j++)
                    {
                    if (tokens1[i - 1] == tokens2[j - 1])
                        {
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                        }
                    else
                        {
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                        }
                    }
                }

            int lcsLength = dp[tokens1.Count, tokens2.Count];

            // 步骤 3: 根据LCS长度计算相似度分数
            return (2.0 * lcsLength) / (tokens1.Count + tokens2.Count);
            }

        #endregion
        }
    }