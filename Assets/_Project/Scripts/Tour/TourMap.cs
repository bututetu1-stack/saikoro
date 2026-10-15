using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SaiNoMichi.Tour
{
    /// <summary>国めぐりのマスの種類。</summary>
    public enum TourTileKind
    {
        Town,      // 宿場町：名物（物件）を買える。サイコロ屋がある町も
        Blue,      // 青：ゴールドがもらえる
        Red,       // 赤：ゴールドを取られる
        Yellow,    // 黄：札（カード）がもらえる
        Battle,    // 戦闘：魔物と戦う
        Shrine,    // 祠：HP が回復し、ときどき札
        Event,     // 出来事：何かが起きる（みんなに関わることも）
    }

    /// <summary>国めぐりの盤面の1マス。道は向きがなく、どちらへも進める。</summary>
    public class TourNode
    {
        public int id;
        public TourTileKind kind;
        public Vector2 position;   // 盤面の上の位置（1 = マス1つぶんくらい）
        public int town = -1;      // 町なら町の番号
        public readonly List<TourNode> neighbors = new List<TourNode>();
        public override string ToString() => $"#{id}:{kind}";
    }

    /// <summary>町の名物（桃鉄の物件）。決算のたびに、値段の incomePercent% が持ち主に入る。</summary>
    public class TourProperty
    {
        public string name;
        public int price;
        public int incomePercent;
        public int owner = -1;     // 持ち主の番号（−1 はだれも持っていない）
        public int Income => price * incomePercent / 100;
    }

    /// <summary>宿場町。</summary>
    public class TourTown
    {
        public int index;
        public string name;
        public TourNode node;
        public bool diceShop;      // サイコロ屋がある
        public readonly List<TourProperty> properties = new List<TourProperty>();

        /// <summary>名物をすべて持っている人（独占。決算の収入が2倍）。いなければ −1。</summary>
        public int Monopolist => properties.Count > 0 && properties[0].owner >= 0 && properties.All(p => p.owner == properties[0].owner) ? properties[0].owner : -1;
    }

    /// <summary>国めぐりの盤面（町と、町をつなぐ道のマス）。</summary>
    public class TourMap
    {
        public readonly List<TourNode> nodes;
        public readonly List<TourTown> towns;

        public TourMap(List<TourNode> nodes, List<TourTown> towns)
        {
            this.nodes = nodes;
            this.towns = towns;
        }

        public TourNode Node(int id) => id >= 0 && id < nodes.Count && nodes[id].id == id ? nodes[id] : nodes.FirstOrDefault(n => n.id == id);

        /// <summary>from から、道をたどった最短の歩数（すべてのマスへ）。</summary>
        public Dictionary<TourNode, int> DistancesFrom(TourNode from)
        {
            var dist = new Dictionary<TourNode, int> { [from] = 0 };
            var queue = new Queue<TourNode>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                foreach (var m in n.neighbors)
                {
                    if (dist.ContainsKey(m)) continue;
                    dist[m] = dist[n] + 1;
                    queue.Enqueue(m);
                }
            }
            return dist;
        }

        public int Distance(TourNode a, TourNode b) => DistancesFrom(a).TryGetValue(b, out int d) ? d : int.MaxValue;

        /// <summary>
        /// steps 歩でちょうど止まれるマスと、そこまでの道（from は入らない）。来た道（cameFrom）をすぐには戻らない
        /// （行き止まりでは戻れる）。途中で stopAt（目的地）に入ったら、そこで止まる。
        /// </summary>
        public Dictionary<TourNode, List<TourNode>> Reach(TourNode from, TourNode cameFrom, int steps, TourNode stopAt = null)
        {
            var result = new Dictionary<TourNode, List<TourNode>>();
            if (steps <= 0)
            {
                result[from] = new List<TourNode>();
                return result;
            }
            // 状態 = (いまのマス, 1つ前のマス)。同じ状態は1回だけたどる（道筋は1つ覚えておけばよい）
            var frontier = new List<(TourNode node, TourNode prev, List<TourNode> path)> { (from, cameFrom, new List<TourNode>()) };
            for (int step = 1; step <= steps && frontier.Count > 0; step++)
            {
                var next = new List<(TourNode, TourNode, List<TourNode>)>();
                var seen = new HashSet<(TourNode, TourNode)>();
                foreach (var (node, prev, path) in frontier)
                {
                    var ways = node.neighbors.Where(n => n != prev).ToList();
                    if (ways.Count == 0 && prev != null) ways.Add(prev); // 行き止まりなら戻る
                    foreach (var n in ways)
                    {
                        if (!seen.Add((n, node))) continue;
                        var p = new List<TourNode>(path) { n };
                        if (n == stopAt || step == steps)
                        {
                            if (!result.ContainsKey(n)) result[n] = p;
                            continue;
                        }
                        next.Add((n, node, p));
                    }
                }
                frontier = next;
            }
            return result;
        }
    }
}
