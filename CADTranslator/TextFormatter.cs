using System.Text;

namespace CADTranslator
{
    public static class TextFormatter
    {
        public static string Format(string rawText, int maxCharsPerLine, int indentation)
        {
            if (string.IsNullOrWhiteSpace(rawText) || maxCharsPerLine <= 0) return rawText;
            var words = rawText.Split(new[] { ' ', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
            var formattedText = new StringBuilder();
            var currentLine = new StringBuilder();
            var indentString = new string(' ', indentation);
            foreach (var word in words)
            {
                if (currentLine.Length > 0 && currentLine.Length + word.Length + 1 > maxCharsPerLine)
                {
                    formattedText.AppendLine(currentLine.ToString());
                    currentLine.Clear();
                }
                if (currentLine.Length > 0) currentLine.Append(" ");
                currentLine.Append(word);
            }
            if (currentLine.Length > 0) formattedText.Append(currentLine.ToString());
            var lines = formattedText.ToString().Split(new[] { '\n' }, System.StringSplitOptions.None);
            var finalResult = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (i == 0) finalResult.AppendLine(lines[i]);
                else finalResult.Append(indentString).AppendLine(lines[i]);
            }
            return finalResult.ToString().TrimEnd();
        }
    }
}