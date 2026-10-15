using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = System.Random;

namespace SaiNoMichi.Tour
{
    /// <summary>
    /// 国めぐりの巨大マップを作る：町を格子状に（少しずらして）並べ、となりの町と道でつなぐ（いくつか道を抜き、斜めの道を足す）。
    /// 道には、町と町の距離に合わせてマスを並べる。同じシードなら同じ盤面。
    /// </summary>
    public static class TourMapGenerator
    {
        public static TourMap Generate(Random rng, TourSettings s)
        {
            int cols = Math.Max(2, s.townColumns), rows = Math.Max(2, s.townRows);
            var nodes = new List<TourNode>();
            var towns = new List<TourTown>();

            // 町の名前と名物
            var names = Shuffle(rng, s.townNames.Where(n => !string.IsNullOrEmpty(n)).ToList());
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var node = new TourNode
                    {
                        id = nodes.Count,
                        kind = TourTileKind.Town,
                        position = new Vector2(c * s.townSpacingX + Jitter(rng, 1.2f), r * s.townSpacingY + Jitter(rng, 1.0f) + (c % 2) * s.townSpacingY * 0.3f),
                    };
                    var town = new TourTown
                    {
                        index = towns.Count,
                        name = towns.Count < names.Count ? names[towns.Count] : $"宿場{towns.Count + 1}",
                        node = node,
                        diceShop = rng.Next(100) < s.diceShopPercent,
                    };
                    node.town = town.index;
                    nodes.Add(node);
                    towns.Add(town);
                }
            }
            // サイコロ屋が1つもないと困るので、少なくとも2つ
            while (towns.Count(t => t.diceShop) < Math.Min(2, towns.Count)) towns[rng.Next(towns.Count)].diceShop = true;
            foreach (var t in towns) AddProperties(rng, s, t);

            // 道：格子のとなり同士。いくつか抜き、斜めをいくつか足す（どの町からもどの町へも行けるまま）
            var edges = new List<(int a, int b)>();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    if (c + 1 < cols) edges.Add((i, i + 1));
                    if (r + 1 < rows) edges.Add((i, i + cols));
                }
            }
            foreach (var e in Shuffle(rng, edges.ToList()))
            {
                if (rng.Next(100) >= s.removeEdgePercent) continue;
                var without = edges.Where(x => x != e).ToList();
                if (Degree(without, e.a) < 2 || Degree(without, e.b) < 2 || !Connected(without, towns.Count)) continue;
                edges = without;
            }
            for (int r = 0; r + 1 < rows; r++)
            {
                for (int c = 0; c + 1 < cols; c++)
                {
                    if (rng.Next(100) >= s.diagonalPercent) continue;
                    int i = r * cols + c;
                    edges.Add(rng.Next(2) == 0 ? (i, i + cols + 1) : (i + 1, i + cols));
                }
            }

            // 道のマス
            int total = Math.Max(1, s.blueWeight + s.redWeight + s.yellowWeight + s.battleWeight + s.shrineWeight + s.eventWeight);
            foreach (var (a, b) in edges)
            {
                var from = towns[a].node;
                var to = towns[b].node;
                float length = Vector2.Distance(from.position, to.position);
                int count = Mathf.Clamp(Mathf.RoundToInt(length) - 1, 2, 9);
                // 少し曲げる（まっすぐだと単調なので）
                var normal = new Vector2(-(to.position - from.position).y, (to.position - from.position).x).normalized;
                float bend = Jitter(rng, 0.8f);
                var prev = from;
                for (int k = 1; k <= count; k++)
                {
                    float t = k / (float)(count + 1);
                    var node = new TourNode
                    {
                        id = nodes.Count,
                        kind = PickKind(rng, s, total),
                        position = Vector2.Lerp(from.position, to.position, t) + normal * bend * Mathf.Sin(t * Mathf.PI),
                    };
                    nodes.Add(node);
                    Link(prev, node);
                    prev = node;
                }
                Link(prev, to);
            }
            return new TourMap(nodes, towns);
        }

        static void AddProperties(Random rng, TourSettings s, TourTown town)
        {
            var pool = Shuffle(rng, s.propertyPool.ToList());
            // 大きな町（4つに1つ）は名物が1つ多い
            int count = Math.Min(pool.Count, s.propertiesPerTown + (rng.Next(4) == 0 ? 1 : 0));
            foreach (var p in pool.Take(count).OrderBy(p => p.price))
            {
                town.properties.Add(new TourProperty { name = p.name, price = p.price, incomePercent = p.incomePercent });
            }
        }

        static TourTileKind PickKind(Random rng, TourSettings s, int total)
        {
            int r = rng.Next(total);
            if ((r -= s.blueWeight) < 0) return TourTileKind.Blue;
            if ((r -= s.redWeight) < 0) return TourTileKind.Red;
            if ((r -= s.yellowWeight) < 0) return TourTileKind.Yellow;
            if ((r -= s.battleWeight) < 0) return TourTileKind.Battle;
            if ((r -= s.shrineWeight) < 0) return TourTileKind.Shrine;
            return TourTileKind.Event;
        }

        static void Link(TourNode a, TourNode b)
        {
            if (!a.neighbors.Contains(b)) a.neighbors.Add(b);
            if (!b.neighbors.Contains(a)) b.neighbors.Add(a);
        }

        static float Jitter(Random rng, float amount) => (float)(rng.NextDouble() * 2 - 1) * amount;

        static int Degree(List<(int a, int b)> edges, int i) => edges.Count(e => e.a == i || e.b == i);

        static bool Connected(List<(int a, int b)> edges, int count)
        {
            var seen = new HashSet<int> { 0 };
            var stack = new Stack<int>();
            stack.Push(0);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                foreach (var (a, b) in edges)
                {
                    int other = a == i ? b : b == i ? a : -1;
                    if (other >= 0 && seen.Add(other)) stack.Push(other);
                }
            }
            return seen.Count == count;
        }

        static List<T> Shuffle<T>(Random rng, List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }
    }
}
