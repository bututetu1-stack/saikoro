using System;
using SaiNoMichi.Run;

namespace SaiNoMichi.Multiplayer
{
    public enum RaceState
    {
        Running,
        Cleared,   // 第3層のボスを倒した
        Dead,      // 倒れた
    }

    /// <summary>
    /// 対戦「賽レース」で送り合う進み具合（同じシードの盤面なので、層とマスの番号で位置がわかる）。
    /// 通信で送る中身はこれだけ。各自のランの計算は各自の端末で行う。
    /// </summary>
    [Serializable]
    public struct RaceProgress
    {
        public string name;
        public int layer;         // 0 から数える
        public int tileId;        // いまいるマス（その層の盤面の番号）
        public int tilesToGoal;   // ボスまで最短何マス
        public int hp;
        public int maxHp;
        public int turn;
        public RaceState state;

        public static RaceProgress From(RunState run, string name)
        {
            var state = run.player.IsDead ? RaceState.Dead : RaceState.Running;
            return new RaceProgress
            {
                name = name,
                layer = run.LayerIndex,
                tileId = run.Current != null ? run.Current.id : 0,
                tilesToGoal = run.Current != null ? run.TilesToGoal : 0,
                hp = Math.Max(0, run.player.hp),
                maxHp = run.player.maxHp,
                turn = run.Turn,
                state = state,
            };
        }

        /// <summary>
        /// 順位を決めるための値（大きいほど前）。踏破した人が一番前（少ないターンほど前）、
        /// 次に先の層・ボスに近い人。倒れた人は、倒れた場所の進み具合で並べる。
        /// </summary>
        public long RankKey
        {
            get
            {
                if (state == RaceState.Cleared) return 1_000_000_000L - turn;
                return layer * 10_000L + (1_000 - Math.Min(1_000, tilesToGoal));
            }
        }
    }
}
