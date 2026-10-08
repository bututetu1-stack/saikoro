namespace SaiNoMichi.UI
{
    /// <summary>はじめての場面で1回だけ出す案内（遊び方を読まずに始めた人向け）。出したかどうかは GameSettings に保存する。</summary>
    public static class Hints
    {
        public const string FirstMove = "move";
        public const string FirstBattle = "battle";
        public const string FirstRefresh = "refresh";
        public const string FirstShop = "shop";

        static readonly string[] All = { FirstMove, FirstBattle, FirstRefresh, FirstShop };

        public static void ResetAll() => GameSettings.ResetHints(All);

        public static (string title, string body) Text(string id)
        {
            switch (id)
            {
                case FirstMove:
                    return ("はじめての移動",
                        "下のダイスにマウスを乗せると、止まれるマスが光ります。\n" +
                        "クリックで振ったら、止まるマスをクリックして進みます。進む前ならお守りも使えます。\n" +
                        "振ったダイスは「使用済み」になり、戦闘でも使えなくなります。");
                case FirstBattle:
                    return ("はじめての戦闘",
                        "ダイスを選んで（1ラウンド3個まで）「振る」。出目を「攻撃」か「防御」に割り振って「決定」。\n" +
                        "敵の頭の上の予告で、次に何をしてくるかがわかります。マウスを乗せると説明が出ます。");
                case FirstRefresh:
                    return ("リフレッシュ",
                        "使用可能なダイスが0個になったので、使用済みのダイスが全部戻りました。\n" +
                        "強いダイスを移動に使うか、戦闘に取っておくか。このやりくりがこのゲームの中心です。");
                case FirstShop:
                    return ("ショップ",
                        "品物を選んで「買う」。ダイスの削除もできます（弱いダイスを減らすと、強いダイスが回りやすくなる）。\n" +
                        "右上のお守りは、クリックで使う・捨てるを選べます。");
                default:
                    return ("", "");
            }
        }
    }
}
