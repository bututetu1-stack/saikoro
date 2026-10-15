using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Run;

namespace SaiNoMichi.UI
{
    /// <summary>実績を調べて、取れたら画面の上にお知らせを出す。</summary>
    public partial class GameController
    {
        /// <summary>旅が終わったとき（仲間から抜ける前）に調べて取る。初めて取ったものを返す（お知らせは結果の画面を出してから）。</summary>
        List<Achievements.Def> CollectRunAchievements(bool cleared)
        {
            var ids = new List<string>();
            if (run == null) return new List<Achievements.Def>();
            if (cleared)
            {
                ids.Add("clear");
                if (run.Turn <= Achievements.FastClearTurns) ids.Add("clear_fast");
                if (run.Ascension >= 5) ids.Add("asc5");
                if (run.Ascension >= 10) ids.Add("asc10");
                if (run.Traveler == TravelerKind.Bakuto) ids.Add("bakuto");
                if (run.Traveler == TravelerKind.Saikushi) ids.Add("saikushi");
                if (run.Traveler == TravelerKind.Yamabushi) ids.Add("yamabushi");
                if (dailyKey != null) ids.Add("daily");
                if (party != null) ids.Add("party");
            }
            if (run.pouch.IsFull) ids.Add("full_pouch");
            if (run.Relics.Count >= Achievements.ManyRelics) ids.Add("relics");
            if (run.stats.goldEarned >= Achievements.RichGold) ids.Add("rich");
            if (run.stats.engravings.Count >= Achievements.ManyEngravings) ids.Add("engrave");
            if (SeenAllEnemies()) ids.Add("enemies");
            return Achievements.UnlockAll(ids);
        }

        void ShowAchievements(List<Achievements.Def> got)
        {
            foreach (var a in got) StartCoroutine(RaceToast($"実績「{a.name}」を取った！"));
        }

        /// <summary>図鑑の敵をすべて見たか（図鑑と同じ一覧で数える）。</summary>
        bool SeenAllEnemies()
        {
            var enemies = config.battleEnemies.Concat(config.eliteEnemies);
            foreach (var layer in config.layers.Where(l => l != null))
                enemies = enemies.Concat(layer.battleEnemies).Concat(layer.eliteEnemies).Concat(layer.bossChoices);
            var names = enemies.Where(e => e != null).Select(e => e.displayName).Distinct().ToList();
            var seen = RunHistory.Seen(RunHistory.Category.Enemy);
            return names.Count > 0 && names.All(seen.Contains);
        }
    }
}
