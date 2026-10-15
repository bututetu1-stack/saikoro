using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;

namespace SaiNoMichi.Run
{
    /// <summary>
    /// 協力「賽の旅団」のラン（STS2 風）。全員が同じシードの盤面を、同じマスにそろって進む。
    /// HP・ポーチ・レリック・ゴールドは各自（1人ずつの RunState）。
    /// 移動：全員が自分のダイスを1個ずつ同時に振る → どの出目で進むかを投票 → 全員がその出目で、同じ道を進む。
    /// 振ったダイスは全員使用済みになる（使用済みサイクルを全員が回す）。通るマス・止まるマスの効果は各自に効く。
    /// </summary>
    public class PartyRun
    {
        public readonly List<RunState> members = new List<RunState>();

        /// <param name="starters">各自が選んだ最初のダイス（人数ぶん。null ならなし）。</param>
        public PartyRun(GameConfig config, int seed, IReadOnlyList<DiceData> starters)
        {
            if (starters == null || starters.Count == 0) throw new ArgumentException("パーティーがいません。", nameof(starters));
            // 盤面はシードで決まるので、全員同じシードで作れば同じ盤面になる
            foreach (var starter in starters) members.Add(new RunState(config, seed, starter));
        }

        public int Count => members.Count;

        /// <summary>全員が同じマスにいるか（ずれていたら、どこかの計算がずれている）。</summary>
        public bool InSync => members.All(m => m.LayerIndex == members[0].LayerIndex && m.Current?.id == members[0].Current?.id);

        /// <summary>全員がダイスを振る（まだ1歩も動かない）。dice[i] は i 番目の人のダイス。</summary>
        public List<RunState.MoveInProgress> RollAll(IReadOnlyList<DiceInstance> dice)
        {
            if (dice.Count != members.Count) throw new ArgumentException("人数ぶんのダイスがいります。", nameof(dice));
            return members.Select((m, i) => m.BeginMove(dice[i])).ToList();
        }

        /// <summary>
        /// 投票で、どの人の出目で進むかを決める。votes[i] は i 番目の人が選んだ人の番号。
        /// いちばん票の多い出目。同じ票数なら、代表（leader）が選んだもの、それもなければ番号の若い人。
        /// </summary>
        public static int Tally(IReadOnlyList<int> votes, int leader)
        {
            var counts = votes.Where(v => v >= 0).GroupBy(v => v).Select(g => (slot: g.Key, count: g.Count())).ToList();
            if (counts.Count == 0) return Math.Max(0, leader);
            int best = counts.Max(c => c.count);
            var top = counts.Where(c => c.count == best).Select(c => c.slot).ToList();
            if (leader >= 0 && leader < votes.Count && top.Contains(votes[leader])) return votes[leader];
            return top.Min();
        }

        /// <summary>選ばれた人の出目（と残りの歩数）に、全員の移動をそろえる。</summary>
        public void Adopt(List<RunState.MoveInProgress> moves, int chosen)
        {
            var c = moves[chosen];
            foreach (var m in moves)
            {
                m.value = c.value;
                m.remaining = c.remaining;
                m.adjust = 0;                // 風などの選び直しは、選ばれた人の出目には使わない（そろわなくなるため）
                m.forcedTarget = c.forcedTarget;
            }
        }

        /// <summary>全員が1歩進む（分岐では next を選ぶ。全員同じ道）。</summary>
        public void StepAll(List<RunState.MoveInProgress> moves, TileNode nextOfLeader = null)
        {
            for (int i = 0; i < members.Count; i++)
            {
                // 盤面は同じ形なので、マスの番号で同じマスを選ぶ
                TileNode next = null;
                if (nextOfLeader != null) next = members[i].Current.next.FirstOrDefault(t => t.id == nextOfLeader.id);
                members[i].StepMove(moves[i], next);
            }
        }

        public bool AllDone(List<RunState.MoveInProgress> moves) => moves.All(m => m.Done);

        public List<MoveResult> FinishAll(List<RunState.MoveInProgress> moves) => members.Select((m, i) => m.FinishMove(moves[i])).ToList();
    }
}
