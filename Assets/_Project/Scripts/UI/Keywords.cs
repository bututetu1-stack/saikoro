using System.Collections.Generic;
using System.Text;
using SaiNoMichi.Battle;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 用語の説明（STS のキーワードのように、説明文に出てきた用語の意味を、吹き出しの下に足す）。
    /// レリック・お守り・予告などの吹き出しで使う。
    /// </summary>
    public static class Keywords
    {
        // 長い用語を先に並べる（「使用済み」の中の「使用」などに引っかからないように。いまは重なる用語はない）
        static readonly (string word, string color, string explanation)[] All =
        {
            ("筋力", "FFD070", "攻撃するとき、攻撃値に足す。戦闘が終わると消える。"),
            ("脱力", "C79BFF", $"与えるダメージが {BattleResolver.WeakPercent}% になる。ラウンドが終わるたびに 1 減る。"),
            ("弱体", "FF9A7A", $"受けるダメージが {BattleResolver.VulnerablePercent}% になる。ラウンドが終わるたびに 1 減る。"),
            ("脆弱", "9FC7D9", $"作れる防御が {BattleResolver.FrailPercent}% になる。ラウンドが終わるたびに 1 減る。"),
            ("毒", "8BE07A", "防御を無視してダメージ。そのあと毒が 1 減る。"),
            ("堅守", "A8D8FF", "ラウンドの終わりに防御が消えず、半分残る。"),
            ("縛り", "E0A0FF", "そのラウンドは振れるダイスが 1 個だけ。"),
            ("封印", "FF8A6A", "そのダイスは戦闘が終わるまで使えない。"),
            ("呪い", "D0A0FF", "弱いダイスがポーチに押し付けられる。"),
            ("使用済み", "D8CDB8", "振ったダイスはしばらく使えない。使用可能なダイスが 0 個になると全部戻る（リフレッシュ）。"),
            ("リフレッシュ", "FFD24D", "使用可能なダイスが 0 個になった瞬間、使用済みのダイスが全部戻ること。"),
            ("刻印", "FFD24D", "鍛冶でダイスの面に付ける。数値を変えるものと、その面が出たときに効果発動するものがある。"),
            ("裏返し", "E2B4FF", "出目が「7−出目」になる（6なら1、1なら6）。"),
            ("棘", "FF8A6A", "大きい出目で攻撃したダイス1個ごとに、防御を無視してダメージを受ける。"),
        };

        /// <summary>吹き出し用：1行目（見出し）に出てくる用語は、その吹き出し自身の説明なので足さない。</summary>
        public static string AppendForTip(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int newline = text.IndexOf('\n');
            string header = newline >= 0 ? text.Substring(0, newline) : text;
            var exclude = new List<string>();
            foreach (var (word, _, _) in All)
            {
                if (header.Contains(word)) exclude.Add(word);
            }
            return Append(text, exclude.ToArray());
        }

        /// <summary>
        /// text に出てくる用語の説明を、下に小さく足して返す。exclude に入れた用語（吹き出しの見出しそのものなど）は足さない。
        /// </summary>
        public static string Append(string text, params string[] exclude)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var excluded = new HashSet<string>(exclude ?? new string[0]);
            var sb = new StringBuilder();
            foreach (var (word, color, explanation) in All)
            {
                if (excluded.Contains(word) || !text.Contains(word)) continue;
                sb.Append($"\n<color=#{color}>{word}</color>：{explanation}");
            }
            if (sb.Length == 0) return text;
            return text + "\n<size=80%><color=#B8AC98>────────</color>" + sb + "</size>";
        }
    }
}
