using System.Text;
using TMPro;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 日本語の改行の位置を整える。TextMeshPro は日本語をどの文字の間でも折り返すので、
    /// 「使え／ます」のように中途半端な所で切れて見苦しかった（開発者の指摘）。
    /// 句読点や括弧（、。」）！？ など）の後ろでだけ折り返すよう、その間を &lt;nobr&gt; で囲み、
    /// 区切りには幅のない空白（U+200B）を入れて、そこで折り返せるようにする。
    /// 1つの区切りが1行より長いときは、TextMeshPro がその中で折り返す。
    /// </summary>
    public class JapaneseLineBreak : ITextPreprocessor
    {
        public static readonly JapaneseLineBreak Instance = new JapaneseLineBreak();

        // この文字の後ろで折り返してよい
        const string BreakAfter = "、。，．！？!?）」』】〉》…：　";
        // この文字の前で折り返してよい
        const string BreakBefore = "（「『【〈《";
        const char ZeroWidthSpace = '​';

        public string PreprocessText(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Contains("<nobr>") || !HasJapanese(text)) return text;
            var sb = new StringBuilder(text.Length + 64);
            bool open = false;

            void Close()
            {
                if (!open) return;
                sb.Append("</nobr>");
                open = false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                // 色や大きさのタグはそのまま写す（nobr の中にあってもよい）
                if (c == '<')
                {
                    int end = text.IndexOf('>', i);
                    if (end > i)
                    {
                        sb.Append(text, i, end - i + 1);
                        i = end;
                        continue;
                    }
                }
                if (c == '\n' || c == ' ')
                {
                    Close();
                    sb.Append(c);
                    continue;
                }
                if (BreakBefore.IndexOf(c) >= 0 && open)
                {
                    Close();
                    sb.Append(ZeroWidthSpace);
                }
                if (!open)
                {
                    sb.Append("<nobr>");
                    open = true;
                }
                sb.Append(c);
                if (BreakAfter.IndexOf(c) >= 0)
                {
                    Close();
                    sb.Append(ZeroWidthSpace);
                }
            }
            Close();
            return sb.ToString();
        }

        static bool HasJapanese(string text)
        {
            foreach (char c in text)
            {
                if (c >= '　' && c <= '鿿') return true;
            }
            return false;
        }
    }
}
