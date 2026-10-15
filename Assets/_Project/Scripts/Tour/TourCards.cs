using System;
using System.Collections.Generic;
using System.Linq;

namespace SaiNoMichi.Tour
{
    /// <summary>
    /// 国めぐりの札（みんなで遊ぶとき専用の道具。桃鉄のカードにあたる）。黄マス・祠・札屋で手に入る。
    /// 相手を選ぶ札（足止め・盗み・呪い・身代わり）は、ほかの人に使う。
    /// </summary>
    public enum TourCardKind
    {
        Express,     // 急行札：この手番、ダイスを2個振って足した数だけ進む
        Stall,       // 足止め札：相手の次の移動の出目 −3（最低1）
        Thief,       // 盗人札：相手のゴールドを 15% 盗む
        Curse,       // 呪い札：相手のポーチに呪いのダイスを1個入れる
        Scapegoat,   // 身代わり札：自分についた厄神を、相手に押し付ける
        Courier,     // 飛脚札：好きな町へ飛んでいく（その町に止まる）
        Fortune,     // 福の札：ゴールドがもらえる（ターンが進むほど多い）
        Tailwind,    // 追い風札：この手番の出目 +3
        Swap,        // 入れ替え札：相手と居場所を入れ替える
        Gust,        // 天狗の団扇：相手を、どこかの町へ吹き飛ばす（桃鉄の「ぶっとび」風）
        Smith,       // 鍛冶札：自分のいちばん弱いダイスの、いちばん小さい面を +1
    }

    public static class TourCards
    {
        public static readonly TourCardKind[] All = (TourCardKind[])Enum.GetValues(typeof(TourCardKind));

        // TODO(仕様): 札の効き目・出やすさ・値段は仮
        public const int StallAmount = 3;
        public const int ThiefPercent = 15;
        public const int TailwindAmount = 3;
        public const int FortuneBase = 60;
        public const int FortunePerRound = 6;

        public static string Name(TourCardKind k)
        {
            switch (k)
            {
                case TourCardKind.Express: return "急行札";
                case TourCardKind.Stall: return "足止め札";
                case TourCardKind.Thief: return "盗人札";
                case TourCardKind.Curse: return "呪い札";
                case TourCardKind.Scapegoat: return "身代わり札";
                case TourCardKind.Courier: return "飛脚札";
                case TourCardKind.Fortune: return "福の札";
                case TourCardKind.Tailwind: return "追い風札";
                case TourCardKind.Swap: return "入れ替え札";
                case TourCardKind.Gust: return "天狗の団扇";
                case TourCardKind.Smith: return "鍛冶札";
                default: return k.ToString();
            }
        }

        public static string Description(TourCardKind k)
        {
            switch (k)
            {
                case TourCardKind.Express: return "この手番、ダイスを2個振って、出目を足した数だけ進む。";
                case TourCardKind.Stall: return $"相手の次の移動の出目 −{StallAmount}（最低1）。";
                case TourCardKind.Thief: return $"相手のゴールドを {ThiefPercent}% 盗む。";
                case TourCardKind.Curse: return "相手のポーチに呪いのダイスを1個入れる。";
                case TourCardKind.Scapegoat: return "自分についた厄神を、相手に押し付ける。";
                case TourCardKind.Courier: return "好きな町へ飛んでいき、その町に止まる（この手番は振らない）。";
                case TourCardKind.Fortune: return "ゴールドがもらえる（ターンが進むほど多い）。";
                case TourCardKind.Tailwind: return $"この手番の出目 +{TailwindAmount}。";
                case TourCardKind.Swap: return "相手と居場所を入れ替える。";
                case TourCardKind.Gust: return "相手を、どこかの町へ吹き飛ばす（行き先は運まかせ）。";
                case TourCardKind.Smith: return "自分のいちばん弱いダイスの、いちばん小さい面を +1 する。";
                default: return "";
            }
        }

        /// <summary>相手を選んで使う札か。</summary>
        public static bool NeedsTarget(TourCardKind k) => k == TourCardKind.Stall || k == TourCardKind.Thief || k == TourCardKind.Curse || k == TourCardKind.Scapegoat
            || k == TourCardKind.Swap || k == TourCardKind.Gust;

        /// <summary>振る前に使う札か（急行・追い風・飛脚は手番のはじめに使う）。どれも手番のはじめに使う。</summary>
        public static int Weight(TourCardKind k)
        {
            switch (k)
            {
                case TourCardKind.Courier: return 4;
                case TourCardKind.Gust: return 5;
                case TourCardKind.Swap: return 6;
                case TourCardKind.Scapegoat: return 6;
                case TourCardKind.Curse: return 8;
                case TourCardKind.Thief: return 10;
                default: return 12;
            }
        }

        /// <summary>札屋の値段。</summary>
        public static int Price(TourCardKind k)
        {
            switch (k)
            {
                case TourCardKind.Courier: return 200;
                case TourCardKind.Swap: return 150;
                case TourCardKind.Gust: return 150;
                case TourCardKind.Scapegoat: return 150;
                case TourCardKind.Thief: return 120;
                case TourCardKind.Express: return 100;
                default: return 80;
            }
        }

        /// <summary>重みつきで1枚選ぶ。</summary>
        public static TourCardKind Pick(Random rng)
        {
            int total = All.Sum(Weight);
            int r = rng.Next(total);
            foreach (var k in All)
            {
                if ((r -= Weight(k)) < 0) return k;
            }
            return TourCardKind.Fortune;
        }
    }
}
