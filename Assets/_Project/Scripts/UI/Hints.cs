namespace SaiNoMichi.UI
{
    /// <summary>はじめての場面で1回だけ出す案内（遊び方を読まずに始めた人向け）。出したかどうかは GameSettings に保存する。</summary>
    public static class Hints
    {
        public const string FirstMove = "move";
        public const string FirstBattle = "battle";
        public const string FirstRefresh = "refresh";
        public const string FirstShop = "shop";
        public const string RollMore = "rollmore";   // まだ振れるのに決定を押したとき

        static readonly string[] All = { FirstMove, FirstBattle, FirstRefresh, FirstShop, RollMore };

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
                        "1ラウンドに<color=#FFD24D>3個まで</color>ダイスを振れます。下のダイスをクリックして<color=#FFD24D>何個か選んでから</color>「振る」と、まとめて振れます。\n" +
                        "ダイスを上の場へドラッグして離しても振れます（1個ずつ振ってもよい）。\n" +
                        "出目ごとに「攻撃」か「防御」を選んで「決定」。敵の頭の上の予告で、次の行動がわかります。");
                case FirstRefresh:
                    return ("リフレッシュ",
                        "使用可能なダイスが0個になったので、使用済みのダイスが全部戻りました。\n" +
                        "強いダイスを移動に使うか、戦闘に取っておくか。このやりくりがこのゲームの中心です。");
                case RollMore:
                    return ("まだ振れます",
                        "このラウンドは、あと <color=#FFD24D>{0} 個</color>ダイスを振れます。\n" +
                        "振れるだけ振ってから「決定」すると、攻撃も防御も大きくなります。\n" +
                        "（使用済みのダイスは、使用可能なダイスがなくなると全部戻ります）");
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
