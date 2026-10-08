using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SaiNoMichi.Battle;

namespace SaiNoMichi.Run
{
    /// <summary>
    /// 検証用の遊んだ記録（Docs/tasks/phase0-prototype.md「遊んだ記録」）。
    /// 1行1イベント（move / battle / result）の縦長の CSV。列はどのイベントでも同じで、使わない列は空にする。
    /// </summary>
    public class PlayLog
    {
        public static readonly string[] Columns =
        {
            "run_id", "seed", "event", "turn",
            "available_count", "available_dice", "chosen_dice", "roll", "from", "to", "tile", "refreshed",
            "enemy", "battle_result", "rounds", "dice_used", "damage_taken", "damage_by_round",
            "reward_kind", "gold_gained", "reward_choice", "gold",
            "result", "hp", "max_hp",
            // フェーズ1で追加：手に入れたもの（acquire）・止まったマスの結果（tile）・成績のまとめ（result）
            "item_kind", "item", "detail",
            // フェーズ2で追加：いまの層（1 始まり）
            "layer",
        };

        public readonly string runId;
        public readonly int seed;
        /// <summary>いまの所持金（どの行にも gold 列として書く）。</summary>
        public Func<int> goldSource;
        /// <summary>いまの層（1 始まり。どの行にも layer 列として書く）。</summary>
        public Func<int> layerSource;
        readonly List<Dictionary<string, string>> pending = new List<Dictionary<string, string>>();

        public PlayLog(string runId, int seed)
        {
            this.runId = runId;
            this.seed = seed;
        }

        Dictionary<string, string> NewRow(string evt, int turn)
        {
            var row = new Dictionary<string, string>
            {
                ["run_id"] = runId,
                ["seed"] = seed.ToString(),
                ["event"] = evt,
                ["turn"] = turn.ToString(),
                ["gold"] = goldSource != null ? goldSource().ToString() : "",
                ["layer"] = layerSource != null ? layerSource().ToString() : "",
            };
            pending.Add(row);
            return row;
        }

        public void RecordMove(int turn, MoveResult move)
        {
            var row = NewRow("move", turn);
            row["available_count"] = move.availableBefore.ToString();
            row["available_dice"] = Join(move.availableDiceBefore.Select(d => d.DisplayName));
            row["chosen_dice"] = move.dice.DisplayName;
            row["roll"] = move.value.ToString();
            row["from"] = move.from.id.ToString();
            row["to"] = move.to.id.ToString();
            row["tile"] = move.to.type.ToString();
            row["refreshed"] = move.refreshed ? "1" : "0";
        }

        public void RecordBattle(int turn, BattleState battle)
        {
            var row = NewRow("battle", turn);
            row["enemy"] = battle.enemy.data.id;
            row["battle_result"] = battle.Outcome.ToString();
            row["rounds"] = battle.History.Count.ToString();
            row["dice_used"] = Join(battle.History.SelectMany(r => r.rolled).Select(r => r.dice.DisplayName));
            row["damage_taken"] = battle.History.Sum(r => r.taken).ToString();
            row["damage_by_round"] = Join(battle.History.Select(r => r.taken.ToString()));
            row["hp"] = battle.player.hp.ToString();
            row["max_hp"] = battle.player.maxHp.ToString();
        }

        /// <param name="choice">選んだダイスの名前。スキップなら "skip"、入れ替えなら "名前>手放した名前"。</param>
        public void RecordReward(int turn, RewardKind kind, int goldGained, string choice, int goldAfter)
        {
            var row = NewRow("reward", turn);
            row["reward_kind"] = kind.ToString();
            row["gold_gained"] = goldGained.ToString();
            row["reward_choice"] = choice;
            row["gold"] = goldAfter.ToString();
        }

        public void RecordResult(int turn, bool cleared, int hp, int maxHp, RunStats stats = null)
        {
            var row = NewRow("result", turn);
            row["result"] = cleared ? "clear" : "gameover";
            row["hp"] = hp.ToString();
            row["max_hp"] = maxHp.ToString();
            if (stats != null)
            {
                row["detail"] = Join(new[]
                {
                    $"battles={stats.battlesWon}", $"elites={stats.elitesWon}", $"gold_earned={stats.goldEarned}",
                    "stops=" + string.Join("/", stats.tilesStopped.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}")),
                });
            }
        }

        /// <summary>手に入れた／手放したもの。kind は dice / relic / engraving / remove / discard。</summary>
        public void RecordAcquire(int turn, string kind, string item)
        {
            var row = NewRow("acquire", turn);
            row["item_kind"] = kind;
            row["item"] = item;
        }

        /// <summary>止まったマスの種類と、そこで起きたこと（イベントの選択など。画面のメッセージのまま）。</summary>
        public void RecordTile(int turn, Board.TileType tile, string detail)
        {
            var row = NewRow("tile", turn);
            row["tile"] = tile.ToString();
            row["detail"] = detail;
        }

        /// <summary>まだ書き出していない行を CSV の行にして返し、溜まっている行を空にする。</summary>
        public List<string> TakePendingLines()
        {
            var lines = pending.Select(row => string.Join(",", Columns.Select(c => Escape(row.TryGetValue(c, out var v) ? v : "")))).ToList();
            pending.Clear();
            return lines;
        }

        public static string Header => string.Join(",", Columns);

        /// <summary>ファイルに追記する。新しいファイルなら先頭に見出し行を書く。Excel で文字化けしないよう BOM 付き UTF-8。</summary>
        public void AppendTo(string path)
        {
            var lines = TakePendingLines();
            if (lines.Count == 0) return;

            // 列が増えて見出しが変わったら、古いファイルは別名で残して新しく書き始める（列がずれないように）
            if (File.Exists(path))
            {
                string firstLine;
                using (var reader = new StreamReader(path, Encoding.UTF8)) firstLine = reader.ReadLine();
                if (firstLine != Header)
                {
                    string old = Path.Combine(Path.GetDirectoryName(path) ?? "", $"{Path.GetFileNameWithoutExtension(path)}_old_{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(path)}");
                    File.Move(path, old);
                }
            }
            bool isNew = !File.Exists(path);
            using (var writer = new StreamWriter(path, true, new UTF8Encoding(isNew)))
            {
                if (isNew) writer.WriteLine(Header);
                foreach (var line in lines) writer.WriteLine(line);
            }
        }

        static string Join(IEnumerable<string> items) => string.Join("|", items);

        public static string Escape(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static string NewRunId() => DateTime.Now.ToString("yyyyMMdd-HHmmss");
    }
}
