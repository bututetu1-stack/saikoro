using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SaiNoMichi.Board
{
    /// <summary>
    /// 分かれては合流する1層ぶんの盤面を作る（仕様書 第8章「生成の手順」）。
    /// 1. 骨組み：一本道 → 3本に分岐 → 合流 → 一本道 → 3本に分岐 → 合流 → 一本道 → 2本に分岐 → 合流 → 一本道 → ボス。
    ///    分かれた道どうしは横道でつながり、途中で乗り換えられる（開発者の判断：ルートを選ぶ場面を増やす）
    /// 2. 固定マス：ボスの3〜6マス手前に休憩かショップ、中盤（合流と2つ目の分岐の間）に鍛冶
    /// 3. 抽選：残りを出現率で。短い道は戦闘・罠、長い道はショップ・宝箱が出やすい
    /// 4. 制約チェック：違反したマスだけ引き直す
    /// 5. 検証：普通の賽だけで進んでショップか休憩に1回も止まれない割合が上限を超えたら作り直す
    /// </summary>
    public static class BranchBoardGenerator
    {
        enum Zone
        {
            Trunk,
            Short,
            Standard,
            Long,
        }

        // 小さな分かれ道のマスを、元の道からどれだけ外へずらすか（段の間隔の何倍か）
        const float DiamondOffset = 0.55f;

        class Work
        {
            public readonly List<TileNode> nodes = new List<TileNode>();
            public readonly Dictionary<TileNode, Zone> zone = new Dictionary<TileNode, Zone>();
            public readonly Dictionary<TileNode, List<TileNode>> preds = new Dictionary<TileNode, List<TileNode>>();
            public readonly HashSet<TileNode> fixedNodes = new HashSet<TileNode>();
            public readonly List<TileNode> middleTrunk = new List<TileNode>();
            public readonly List<TileNode> finalTrunk = new List<TileNode>();
            public TileNode start, boss;

            public IEnumerable<TileNode> Neighbors(TileNode n) => preds[n].Concat(n.next);
        }

        public static BoardData Generate(System.Random rng, LayerBoardSettings s)
        {
            for (int attempt = 0; attempt < s.maxAttempts; attempt++)
            {
                var w = BuildSkeleton(rng, s);
                if (!AssignTypes(w, rng, s)) continue;

                var simRng = new System.Random(rng.Next());
                if (MissShopRestRate(w.start, w.boss, simRng, s.validationRuns) > s.maxMissShopRestRate) continue;

                return new BoardData(w.nodes, w.boss);
            }
            throw new InvalidOperationException("分岐盤面の生成に失敗しました。設定を見直してください。");
        }

        // ---- 1. 骨組み ----

        static Work BuildSkeleton(System.Random rng, LayerBoardSettings s)
        {
            var w = new Work();

            TileNode New(Zone z, float x, float y)
            {
                var n = new TileNode(w.nodes.Count, TileType.Empty) { position = new Vector2(x, y) };
                w.nodes.Add(n);
                w.zone[n] = z;
                w.preds[n] = new List<TileNode>();
                return n;
            }

            void Link(TileNode a, TileNode b)
            {
                a.next.Add(b);
                w.preds[b].Add(a);
            }

            // 分岐区間：from から paths の道を作り、合流点を返す
            TileNode Section(TileNode from, List<(Zone zone, int length)> paths)
            {
                // 短い道がいつも上に来ないよう、道の並び（上下）を混ぜる
                for (int i = paths.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (paths[i], paths[j]) = (paths[j], paths[i]);
                }
                int span = paths.Max(p => p.length) + 1;
                float x0 = from.position.x;
                var ends = new List<TileNode>();
                var lanes = new List<List<TileNode>>();
                for (int p = 0; p < paths.Count; p++)
                {
                    float lane = paths.Count == 1 ? 0 : -1f + 2f * p / (paths.Count - 1);
                    var prev = from;
                    int length = paths[p].length;
                    var laneNodes = new List<TileNode>();
                    for (int j = 1; j <= length; j++)
                    {
                        var n = New(paths[p].zone, x0 + j * span / (float)(length + 1), lane);
                        Link(prev, n);
                        prev = n;
                        laneNodes.Add(n);
                    }
                    ends.Add(prev);
                    lanes.Add(laneNodes);
                }
                // 横道：隣り合う道の途中から、もう一方の道の少し先へつなぐ（前にしか進まないので、x が先のマスへ）
                for (int p = 0; p + 1 < lanes.Count; p++)
                {
                    int links = rng.Next(s.crossLinksMin, s.crossLinksMax + 1);
                    for (int k = 0; k < links; k++)
                    {
                        bool down = rng.Next(2) == 0;
                        var fromLane = lanes[down ? p : p + 1];
                        var toLane = lanes[down ? p + 1 : p];
                        // 分かれてすぐ・合流の直前は避ける（分岐・合流と変わらないため）
                        var sources = fromLane.Skip(1).Take(fromLane.Count - 2).Where(n => n.next.Count == 1).ToList();
                        if (sources.Count == 0) continue;
                        var src = sources[rng.Next(sources.Count)];
                        var dst = toLane.FirstOrDefault(n => n.position.x > src.position.x + 0.01f);
                        if (dst == null || src.next.Contains(dst)) continue;
                        Link(src, dst);
                    }
                }

                // 小さな分かれ道：外側の道（上下の端）で、a → b → c の b の外側に b' を足し、a → b' → c もつなぐ。
                // 真ん中の道に置くと隣の道のマスと重なるので、外側の道だけ（外へふくらませる）
                for (int p = 0; p < lanes.Count; p++)
                {
                    var laneNodes = lanes[p];
                    float y = laneNodes.Count > 0 ? laneNodes[0].position.y : 0f;
                    bool outer = paths.Count >= 2 && (p == 0 || p == lanes.Count - 1) && Mathf.Abs(y) > 0.5f;
                    if (!outer || laneNodes.Count < s.diamondMinLaneLength || rng.Next(100) >= s.diamondPercent) continue;

                    // 分岐・合流・横道に関わらないマスの並びを探す
                    var spots = new List<int>();
                    for (int i = 0; i + 2 < laneNodes.Count; i++)
                    {
                        var a = laneNodes[i];
                        var b = laneNodes[i + 1];
                        if (a.next.Count == 1 && b.next.Count == 1 && w.preds[b].Count == 1) spots.Add(i);
                    }
                    if (spots.Count == 0) continue;
                    int at = spots[rng.Next(spots.Count)];
                    var mid = laneNodes[at + 1];
                    var side = New(w.zone[mid], mid.position.x, y + Mathf.Sign(y) * DiamondOffset);
                    Link(laneNodes[at], side);
                    Link(side, laneNodes[at + 2]);
                }
                lanes.Clear();

                var merge = New(Zone.Trunk, x0 + span, 0);
                foreach (var end in ends) Link(end, merge);
                return merge;
            }

            // 一本道：from から count マス（最後のマスを返す）
            TileNode Trunk(TileNode from, int count, List<TileNode> into = null)
            {
                var prev = from;
                for (int i = 1; i <= count; i++)
                {
                    var n = New(Zone.Trunk, from.position.x + i, 0);
                    Link(prev, n);
                    prev = n;
                    into?.Add(n);
                }
                return prev;
            }

            List<(Zone, int)> ThreePaths(int baseLength) => new List<(Zone, int)>
            {
                (Zone.Short, baseLength - s.shortPathDelta), (Zone.Standard, baseLength), (Zone.Long, baseLength + s.longPathDelta),
            };

            // 一本道（スタート〜最初の分岐点）
            int branch1 = rng.Next(s.firstBranchMin, s.firstBranchMax + 1);
            w.start = New(Zone.Trunk, 0, 0);
            var last = Trunk(w.start, branch1);

            // 1つ目の分岐（短い・標準・長いの3本）
            int base1 = rng.Next(s.section1Min, s.section1Max + 1);
            var merge1 = Section(last, ThreePaths(base1));

            // 合流 → 2つ目の分岐点（最後が分岐点。鍛冶はこの間に置く）
            int middle = rng.Next(s.middleTrunkMin, s.middleTrunkMax + 1);
            last = Trunk(merge1, middle, w.middleTrunk);

            // 2つ目の分岐（短い・標準・長いの3本）
            int base2 = rng.Next(s.section2Min, s.section2Max + 1);
            var merge2 = Section(last, ThreePaths(base2));

            // 合流 → 3つ目の分岐点
            int middle2 = rng.Next(s.middleTrunk2Min, s.middleTrunk2Max + 1);
            last = Trunk(merge2, middle2);

            // 3つ目の分岐（上の道・下の道）
            int base3 = rng.Next(s.section3Min, s.section3Max + 1);
            var merge3 = Section(last, new List<(Zone, int)> { (Zone.Standard, base3), (Zone.Standard, base3 + s.section3Delta) });

            // 合流 → ボス。標準の道で minLength〜maxLength 歩になるように長さを決める
            int soFar = branch1 + (base1 + 1) + middle + (base2 + 1) + middle2 + (base3 + s.section3Delta / 2 + 1);
            int target = rng.Next(s.minLength, s.maxLength + 1);
            int final = Math.Max(s.finalTrunkMin, target - soFar - 1);
            last = Trunk(merge3, final, w.finalTrunk);
            w.boss = New(Zone.Trunk, last.position.x + 1, 0);
            Link(last, w.boss);
            return w;
        }

        /// <summary>前から後ろの順（どのマスも、手前のマスより後に来る）。横道があるので作った順とは限らない。</summary>
        static List<TileNode> TopologicalOrder(Work w)
        {
            var indegree = w.nodes.ToDictionary(n => n, n => w.preds[n].Count);
            var queue = new Queue<TileNode>(w.nodes.Where(n => indegree[n] == 0));
            var order = new List<TileNode>();
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                order.Add(n);
                foreach (var next in n.next)
                {
                    if (--indegree[next] == 0) queue.Enqueue(next);
                }
            }
            return order;
        }

        // ---- 2〜4. 固定マス・抽選・制約チェック ----

        static bool AssignTypes(Work w, System.Random rng, LayerBoardSettings s)
        {
            void Fix(TileNode n, TileType t)
            {
                n.type = t;
                w.fixedNodes.Add(n);
            }

            Fix(w.start, TileType.Empty);
            Fix(w.boss, TileType.Boss);
            int beforeBoss = rng.Next(s.restOrShopBeforeBossMin, s.restOrShopBeforeBossMax + 1);
            Fix(w.finalTrunk[w.finalTrunk.Count - beforeBoss], rng.Next(2) == 0 ? TileType.Rest : TileType.Shop);
            Fix(w.middleTrunk[rng.Next(w.middleTrunk.Count)], TileType.Forge);

            foreach (var n in w.nodes)
            {
                if (!w.fixedNodes.Contains(n)) Draw(w, n, rng, s);
            }

            for (int pass = 0; pass < s.maxRedrawPasses; pass++)
            {
                var violations = FindViolations(w, s);
                if (violations.Count == 0)
                {
                    PlacePassTiles(w, rng, s);
                    return true;
                }
                foreach (var v in violations)
                {
                    if (!w.fixedNodes.Contains(v)) Draw(w, v, rng, s);
                }
            }
            return false;
        }

        static readonly TileType[] PassTypes = { TileType.Shrine, TileType.Checkpoint, TileType.Teahouse, TileType.DiceHall };

        // 通過マスに置き換えてよいマス（休憩・ショップ・鍛冶・エリート・罠は減らさない）
        static readonly TileType[] PassCandidateTypes = { TileType.Empty, TileType.Battle, TileType.Event, TileType.Treasure };

        /// <summary>
        /// 通過マスを 2〜3 個、固定でない戦闘・イベント・宝箱のマスに置く（出現率の表とは別枠。仕様書 第8章）。
        /// 種類はすべて違うものにするので、連続やショップの間隔のルールには影響しない。
        /// </summary>
        static void PlacePassTiles(Work w, System.Random rng, LayerBoardSettings s)
        {
            var candidates = w.nodes.Where(n => PassCandidateTypes.Contains(n.type) && !w.fixedNodes.Contains(n)).ToList();
            var types = PassTypes.OrderBy(_ => rng.Next()).ToList();
            int count = Math.Min(rng.Next(s.passTilesMin, s.passTilesMax + 1), Math.Min(candidates.Count, types.Count));
            for (int i = 0; i < count; i++)
            {
                int pick = rng.Next(candidates.Count);
                var n = candidates[pick];
                candidates.RemoveAt(pick);
                n.type = types[i];
                n.passEffect = true;
                w.fixedNodes.Add(n);
            }
        }

        /// <summary>出現率でマスの種類を引く。エリートの前後の制約は FindViolations で引き直す。</summary>
        static void Draw(Work w, TileNode n, System.Random rng, LayerBoardSettings s)
        {
            n.type = Roll(rng, s, w.zone[n]);
        }

        /// <summary>エリートの前後に置けないマス（狙って避けられるように。仕様書 第8章）。</summary>
        public static bool BlocksElite(TileType t) => t == TileType.Battle || t == TileType.Elite || t == TileType.Trap || t == TileType.Boss;

        static TileType Roll(System.Random rng, LayerBoardSettings s, Zone zone)
        {
            int Weight(TileWeight tw)
            {
                bool shortBias = zone == Zone.Short && (tw.type == TileType.Battle || tw.type == TileType.Trap);
                bool longBias = zone == Zone.Long && (tw.type == TileType.Shop || tw.type == TileType.Treasure);
                return shortBias || longBias ? tw.weight * s.pathBiasPercent / 100 : tw.weight;
            }

            int total = s.weights.Sum(Weight);
            int roll = rng.Next(total);
            foreach (var tw in s.weights)
            {
                int wgt = Weight(tw);
                if (roll < wgt) return tw.type;
                roll -= wgt;
            }
            return TileType.Empty;
        }

        static HashSet<TileNode> FindViolations(Work w, LayerBoardSettings s)
        {
            var bad = new HashSet<TileNode>();
            void Flag(TileNode n)
            {
                if (!w.fixedNodes.Contains(n)) bad.Add(n);
            }

            // 最初の5マス：戦闘は1つまで、エリートと罠は置かない
            var dist = Distances(w.start);
            int battles = 0;
            foreach (var n in w.nodes.Where(n => dist.TryGetValue(n, out int d) && d >= 1 && d <= s.safeStartTiles).OrderBy(n => dist[n]))
            {
                if (n.type == TileType.Elite || n.type == TileType.Trap) Flag(n);
                if (n.type == TileType.Battle && ++battles > s.maxBattlesAtStart) Flag(n);
            }

            // 同じ種類の連続（どの道を通っても）。横道があるので、前から後ろの順に並べ直して数える
            var run = new Dictionary<TileNode, int>();
            foreach (var n in TopologicalOrder(w))
            {
                if (n == w.start || n == w.boss)
                {
                    run[n] = 0;
                    continue;
                }
                int best = 0;
                foreach (var p in w.preds[n])
                {
                    if (p != w.start && p.type == n.type) best = Math.Max(best, run[p]);
                }
                run[n] = best + 1;
                int limit = n.type == TileType.Empty ? s.maxEmptyRun : s.maxSameRun;
                if (run[n] > limit)
                {
                    if (w.fixedNodes.Contains(n)) foreach (var p in w.preds[n].Where(p => p.type == n.type)) Flag(p);
                    else Flag(n);
                }
            }

            // ショップ同士は minShopDistance マス以上離す
            foreach (var shop in w.nodes.Where(n => n.type == TileType.Shop))
            {
                foreach (var kv in Distances(shop, s.minShopDistance - 1))
                {
                    if (kv.Key != shop && kv.Key.type == TileType.Shop)
                    {
                        if (!w.fixedNodes.Contains(kv.Key)) Flag(kv.Key);
                        else Flag(shop);
                    }
                }
            }

            // エリートの前後は戦闘・エリート・罠・ボスにしない
            foreach (var elite in w.nodes.Where(n => n.type == TileType.Elite))
            {
                // 前後のマスを引き直す（固定マスやボスならエリートのほうを引き直す）
                foreach (var nb in w.Neighbors(elite).Where(nb => BlocksElite(nb.type)))
                {
                    if (w.fixedNodes.Contains(nb)) Flag(elite);
                    else Flag(nb);
                }
            }

            // ボス直前は休憩にしない
            foreach (var p in w.preds[w.boss])
            {
                if (p.type == TileType.Rest) Flag(p);
            }
            return bad;
        }

        /// <summary>from から前向きにたどった歩数（maxDepth まで）。</summary>
        static Dictionary<TileNode, int> Distances(TileNode from, int maxDepth = int.MaxValue)
        {
            var dist = new Dictionary<TileNode, int> { [from] = 0 };
            var queue = new Queue<TileNode>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                if (dist[n] >= maxDepth) continue;
                foreach (var next in n.next)
                {
                    if (dist.ContainsKey(next)) continue;
                    dist[next] = dist[n] + 1;
                    queue.Enqueue(next);
                }
            }
            return dist;
        }

        // ---- 5. 検証 ----

        /// <summary>普通の賽（1〜6）だけで進み、分岐はランダムに選んだとき、ショップか休憩に1回も止まれない割合。</summary>
        public static float MissShopRestRate(TileNode start, TileNode boss, System.Random rng, int runs)
        {
            int miss = 0;
            for (int r = 0; r < runs; r++)
            {
                var node = start;
                bool good = false;
                while (node != boss && !node.IsEnd)
                {
                    node = BoardData.Advance(node, rng.Next(1, 7), null, (b, nexts) => nexts[rng.Next(nexts.Count)]);
                    if (node.type == TileType.Shop || node.type == TileType.Rest) good = true;
                }
                if (!good) miss++;
            }
            return runs > 0 ? miss / (float)runs : 0f;
        }
    }
}
