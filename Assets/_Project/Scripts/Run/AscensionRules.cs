using SaiNoMichi.Battle;

namespace SaiNoMichi.Run
{
    /// <summary>
    /// 難易度「段位」（仕様書 第12章。Slay the Spire のアセンション）。クリアすると次の段位が解放され、効果は累積する。
    /// 段位はひとりの旅（ふつうの旅）だけ。今日の旅・みんなで遊ぶときは 0。
    /// </summary>
    public static class AscensionRules
    {
        public const int Max = 10;

        // 仕様書 第12章の表の値
        public const int EliteHpPercent = 120;     // 段位1：エリートの HP +20%
        public const int ShopPricePercent = 110;   // 段位2：ショップの価格 +10%
        public const int UncommonWeightPercent = 85; // 段位2：アンコモンの出やすさ ×0.85
        public const int RareWeightPercent = 60;     // 段位2：レアの出やすさ ×0.6
        public const int StartMaxHpLoss = 8;       // 段位3：はじめの最大HP −8
        public const int BossHpPercent = 115;      // 段位4：ボスの HP +15%
        // 段位5：欠け賽を持ってスタート
        public const int RestHealPercent = 75;     // 段位6：休憩の回復量 −25%
        public const int BossHealPercent = 80;     // 段位6：ボスを倒したときの回復が最大HPの80%（ふだんは全回復）
        public const int NormalAttackBonus = 1;    // 段位7：通常の敵の攻撃 +1
        public const int TrapWeightPercent = 150;  // 段位8：罠マスの出現率 1.5倍
        // 段位9：ボスの手前の休憩・ショップが保証されない
        public const int HachimenPhase3Hp = 40;    // 段位10：八面の第3形態（HP40以下で、毎ラウンドダイス1個を封印）
        public const string HachimenId = "hachimen";
        public const string ChippedDiceId = "kake";

        /// <summary>その段位で加わる条件（1〜10）。</summary>
        public static string Condition(int level)
        {
            switch (level)
            {
                case 1: return "強敵の HP +20%";
                case 2: return "ショップの価格 +10%・報酬やショップでアンコモン・レアが出にくい";
                case 3: return $"はじめの最大HP −{StartMaxHpLoss}";
                case 4: return "ボスの HP +15%";
                case 5: return "欠け賽を持って旅に出る";
                case 6: return $"休憩の回復量 −25%・ボスを倒したときの回復が最大HPの{BossHealPercent}%";
                case 7: return "通常の敵の攻撃 +1";
                case 8: return "罠マスが1.5倍出る";
                case 9: return "ボスの手前の休憩・ショップがあるとは限らない";
                case 10: return "賽の神・八面が第3形態を持つ（HP40以下で、毎ラウンドダイス1個を封印）";
                default: return "";
            }
        }

        /// <summary>敵の HP の倍率（%）。段位1（強敵）・段位4（ボス）。</summary>
        public static int HpPercent(EnemyKind kind, int ascension)
        {
            if (kind == EnemyKind.Elite && ascension >= 1) return EliteHpPercent;
            if (kind == EnemyKind.Boss && ascension >= 4) return BossHpPercent;
            return 100;
        }

        /// <summary>段位2から：レア度の重み（コモン・アンコモン・レア）のうち、アンコモンとレアを減らす。ボスの報酬（レアだけ）はそのまま。</summary>
        public static int[] RarityWeights(int[] weights, int ascension)
        {
            if (ascension < 2 || weights == null || weights.Length < 3) return weights;
            return new[] { weights[0], weights[1] * UncommonWeightPercent / 100, weights[2] * RareWeightPercent / 100 };
        }

        /// <summary>敵の攻撃に足す値。段位7（通常の敵）。</summary>
        public static int AttackBonus(EnemyKind kind, int ascension) => kind == EnemyKind.Normal && ascension >= 7 ? NormalAttackBonus : 0;
    }
}
