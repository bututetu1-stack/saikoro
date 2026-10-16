using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;
using SaiNoMichi.Run;
using UnityEditor;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    /// <summary>旅人（仕様書 第12章）。</summary>
    public class TravelerTests
    {
        const string ConfigPath = "Assets/_Project/Data/GameConfig.asset";

        static GameConfig LoadConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null) Assert.Ignore("GameConfig がありません（SaiNoMichi/Build All Data を実行してください）。");
            return config;
        }

        static string[] Ids(RunState run) => run.pouch.All.Select(d => d.data.id).OrderBy(x => x).ToArray();

        [Test]
        public void StartingDice_ByTraveler()
        {
            var config = LoadConfig();
            CollectionAssert.AreEqual(new[] { "bakuchi", "ikasama", "normal", "normal" }, Ids(new RunState(config, 3, null, 0, TravelerKind.Bakuto)));
            CollectionAssert.AreEqual(new[] { "hari", "karakuri", "normal", "normal" }, Ids(new RunState(config, 3, null, 0, TravelerKind.Saikushi)));
            CollectionAssert.AreEqual(new[] { "hifumi", "hifumi", "shakujou", "tate" }, Ids(new RunState(config, 3, null, 0, TravelerKind.Yamabushi)));
            var saifuri = new RunState(config, 3, config.starterChoices[0]);
            Assert.AreEqual(TravelerKind.Saifuri, saifuri.Traveler);
            Assert.AreEqual(config.startingDice.Count + 1, saifuri.pouch.All.Count());
            Assert.AreEqual(1, saifuri.pouch.All.Count(d => d.data.id == Travelers.SaifuriDiceId), "賽振りは普通の賽の1個が旅賽に");
            Assert.IsFalse(config.rewardDicePool.Any(d => config.travelerDicePool.Contains(d)), "旅人専用のダイスは報酬・ショップに出ない");
        }

        [Test]
        public void Traveler_IgnoresStarter_AndKeepsAscension()
        {
            var config = LoadConfig();
            var run = new RunState(config, 3, config.starterChoices[0], 5, TravelerKind.Bakuto);
            Assert.AreEqual(5, run.pouch.All.Count(), "博徒の4個＋段位5の欠け賽（スターターは入らない）");
            Assert.IsTrue(run.pouch.All.Any(d => d.data.id == AscensionRules.ChippedDiceId));
        }

        [Test]
        public void Saikushi_KarakuriHasCrushOnSixAndGuardOnOne()
        {
            var config = LoadConfig();
            var run = new RunState(config, 3, null, 0, TravelerKind.Saikushi);
            var karakuri = run.pouch.All.First(d => d.data.id == Travelers.SaikushiDiceId);
            Assert.AreEqual(Travelers.CrushEngravingId, karakuri.faces.First(f => f.value == 6).engraving.id);
            Assert.AreEqual(Travelers.GuardEngravingId, karakuri.faces.First(f => f.value == 1).engraving.id);
            Assert.AreEqual(2, run.pouch.All.Sum(d => d.faces.Count(f => f.engraving != null)), "刻印は崩しと堅の2つだけ");
        }

        // ---- 役割分担（2026-10-17）：上位互換にならず、ビルドの向きが変わる ----

        static EnemyData TestEnemy()
        {
            var e = ScriptableObject.CreateInstance<EnemyData>();
            e.id = "test";
            e.displayName = "test";
            e.maxHp = 200;
            e.pattern = new System.Collections.Generic.List<Intent> { new Intent(IntentType.Attack, 1) };
            return e;
        }

        static DiceInstance Fixed(int v)
        {
            var d = ScriptableObject.CreateInstance<DiceData>();
            d.id = "fixed" + v;
            d.displayName = d.id;
            d.faceValues = new[] { v, v, v, v, v, v };
            return new DiceInstance(d);
        }

        /// <summary>travel の旅人で、出目 v のダイスを assignment に置いたときの値。</summary>
        static int ValueFor(GameConfig config, TravelerKind traveler, int v, Assignment assignment)
        {
            var run = new RunState(config, 3, null, 0, traveler);
            var die = Fixed(v);
            run.pouch.ForceAdd(die);
            var enemy = TestEnemy();
            var battle = new BattleState(run.player, enemy, run.pouch, new System.Random(0), run.effects, run);
            var r = battle.Roll(die);
            int value = battle.EffectiveValue(r, assignment);
            Object.DestroyImmediate(enemy);
            Object.DestroyImmediate(die.data);
            return value;
        }

        [Test]
        public void Bakuto_BigDiceHitHarder_Only()
        {
            var config = LoadConfig();
            Assert.AreEqual(6 + Travelers.BakutoBigBonus, ValueFor(config, TravelerKind.Bakuto, 6, Assignment.Attack), "博徒：6の目の攻撃 +3");
            Assert.AreEqual(5, ValueFor(config, TravelerKind.Bakuto, 5, Assignment.Attack), "5以下の目はそのまま");
            Assert.AreEqual(6, ValueFor(config, TravelerKind.Bakuto, 6, Assignment.Block), "防御はそのまま");
            Assert.AreEqual(6, ValueFor(config, TravelerKind.Saifuri, 6, Assignment.Attack));
        }

        [Test]
        public void Yamabushi_BlockPlusOne_Only()
        {
            var config = LoadConfig();
            Assert.AreEqual(2 + Travelers.YamabushiBlockBonus, ValueFor(config, TravelerKind.Yamabushi, 2, Assignment.Block), "小さい目の防御 +1");
            Assert.AreEqual(5, ValueFor(config, TravelerKind.Yamabushi, 5, Assignment.Block), "大きい目はそのまま");
            Assert.AreEqual(3, ValueFor(config, TravelerKind.Yamabushi, 3, Assignment.Attack), "攻撃はそのまま");
        }

        [Test]
        public void Yamabushi_SharesBlockWithParty()
        {
            var config = LoadConfig();
            var yama = new RunState(config, 3, null, 0, TravelerKind.Yamabushi);
            var ally = new RunState(config, 4);
            var die = Fixed(6);
            yama.pouch.ForceAdd(die);
            var enemy = TestEnemy();
            var party = new PartyBattle(new[]
            {
                new PartyMember { player = yama.player, pouch = yama.pouch, effects = yama.effects, run = yama, rng = new System.Random(1) },
                new PartyMember { player = ally.player, pouch = ally.pouch, effects = ally.effects, run = ally, rng = new System.Random(2) },
            }, new[] { enemy }, 5);
            var m = party.Members[0];
            m.Assign(m.Roll(die), Assignment.Block);
            var u = party.Use(0);
            Assert.AreEqual(6, u.blockGained, "6（大きい目は +1 なし）");
            Assert.AreEqual(6 * Travelers.YamabushiSharePercent / 100, party.Members[1].player.block, "仲間にも半分");
            Object.DestroyImmediate(enemy);
            Object.DestroyImmediate(die.data);
        }

        [Test]
        public void Saikushi_GivesMoreDebuffs()
        {
            var config = LoadConfig();
            Assert.AreEqual(Travelers.SaikushiDebuffBonus, new RunState(config, 3, null, 0, TravelerKind.Saikushi).DebuffBonus);
            Assert.AreEqual(0, new RunState(config, 3, null, 0, TravelerKind.Bakuto).DebuffBonus);

            var run = new RunState(config, 3, null, 0, TravelerKind.Saikushi);
            var rider = ScriptableObject.CreateInstance<AttackRiderEffect>();
            rider.trigger = Trigger.OnAttackResolve;
            rider.rider = AttackRider.Vulnerable;
            rider.amount = 1;
            var die = Fixed(3);
            die.data.effects = new System.Collections.Generic.List<EffectSO> { rider };
            run.pouch.ForceAdd(die);
            var enemy = TestEnemy();
            var battle = new BattleState(run.player, enemy, run.pouch, new System.Random(0), run.effects, run);
            battle.Assign(battle.Roll(die), Assignment.Attack);
            battle.Use();
            Assert.AreEqual(1 + Travelers.SaikushiDebuffBonus, battle.enemy.vulnerable, "弱体1 → 細工師は2");
            Object.DestroyImmediate(rider);
            Object.DestroyImmediate(enemy);
            Object.DestroyImmediate(die.data);
        }

        [Test]
        public void Saifuri_HealsOnRefresh_OthersDoNot()
        {
            var config = LoadConfig();
            foreach (var traveler in new[] { TravelerKind.Saifuri, TravelerKind.Yamabushi })
            {
                var run = new RunState(config, 3, null, 0, traveler);
                run.player.hp = 40;
                foreach (var d in run.pouch.Available.ToList()) run.pouch.Use(d, false); // 最後の1個でリフレッシュ
                Assert.AreEqual(traveler == TravelerKind.Saifuri ? 40 + Travelers.SaifuriRefreshHeal : 40, run.player.hp, traveler.ToString());
            }
        }

        [Test]
        public void SecondTraits_InterestForgeRestStartBlock()
        {
            var config = LoadConfig();
            var bakuto = new RunState(config, 3, null, 0, TravelerKind.Bakuto);
            bakuto.GainGold(200 - bakuto.Gold);
            Assert.AreEqual(10, bakuto.GainBattleInterest(), "博徒：持ち金の5%");
            bakuto.GainGold(1000);
            Assert.AreEqual(Travelers.BakutoInterestMax, bakuto.GainBattleInterest(), "最大15G");
            Assert.AreEqual(0, new RunState(config, 3).GainBattleInterest());
            Assert.AreEqual(Travelers.SaikushiForgeChoices, new RunState(config, 3, null, 0, TravelerKind.Saikushi).CreateForgeOffer().Count);
            Assert.AreEqual(3, new RunState(config, 3, null, 0, TravelerKind.Bakuto).CreateForgeOffer().Count);
            var saifuri = new RunState(config, 3);
            var yama = new RunState(config, 3, null, 0, TravelerKind.Yamabushi);
            Assert.Greater(saifuri.RestHealAmount, yama.RestHealAmount, "賽振りは休憩の回復が多い");
        }

        [Test]
        public void RestBoth_IsARelicNow()
        {
            var config = LoadConfig();
            var yama = new RunState(config, 3, null, 0, TravelerKind.Yamabushi);
            Assert.IsFalse(yama.HasRule(RunRule.RestBoth), "山伏でも、休憩で両方はできない");
            var oi = config.relicPool.FirstOrDefault(r => r != null && r.id == "gyoujanooi");
            Assert.IsNotNull(oi, "レリック「行者の笈」");
            var run = new RunState(config, 3);
            run.AddRelic(oi);
            Assert.IsTrue(run.HasRule(RunRule.RestBoth));
        }

        [Test]
        public void Save_KeepsTraveler()
        {
            var config = LoadConfig();
            var run = new RunState(config, 5, null, 2, TravelerKind.Yamabushi);
            var restored = RunState.Restore(config, RunSave.FromJson(run.CreateSave().ToJson()));
            Assert.AreEqual(TravelerKind.Yamabushi, restored.Traveler);
            Assert.AreEqual(2, restored.Ascension);
        }

        // ---- 古の神（STS2 のエンシェント風。2026-10-17） ----

        [Test]
        public void Ancient_OffersThreeFromItsOwnPool_FromLayer2()
        {
            var config = LoadConfig();
            var run = new RunState(config, 3);
            Assert.IsNull(run.AncientName, "第1層は旅立ちの祝福があるので出ない");
            run.AdvanceLayer();
            Assert.AreEqual("竜神", run.AncientName);
            var offer = run.CreateAncientOffer();
            Assert.AreEqual(3, offer.Count);
            Assert.IsTrue(offer.All(r => config.ancientRelicPool.Contains(r)));
            Assert.IsFalse(offer.Any(r => config.relicPool.Contains(r) || config.bossRelicPool.Contains(r)), "報酬・ショップ・ボスには出ない");
            run.AddRelic(offer[0]);
            Assert.IsFalse(run.CreateAncientOffer(20).Contains(offer[0]), "持っているものは出ない");
        }
    }
}
