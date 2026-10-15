using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Core;
using SaiNoMichi.Run;
using UnityEditor;

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
            CollectionAssert.AreEqual(new[] { "bakuchi", "cho", "normal", "normal" }, Ids(new RunState(config, 3, null, 0, TravelerKind.Bakuto)));
            CollectionAssert.AreEqual(new[] { "normal", "normal", "normal" }, Ids(new RunState(config, 3, null, 0, TravelerKind.Saikushi)));
            CollectionAssert.AreEqual(new[] { "hifumi", "hifumi", "tate", "tate" }, Ids(new RunState(config, 3, null, 0, TravelerKind.Yamabushi)));
            var saifuri = new RunState(config, 3, config.starterChoices[0]);
            Assert.AreEqual(TravelerKind.Saifuri, saifuri.Traveler);
            Assert.AreEqual(config.startingDice.Count + 1, saifuri.pouch.All.Count());
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
        public void Saikushi_HasBladeOnSix()
        {
            var config = LoadConfig();
            var run = new RunState(config, 3, null, 0, TravelerKind.Saikushi);
            var first = run.pouch.All.First();
            var six = first.faces.First(f => f.value == 6);
            Assert.IsNotNull(six.engraving);
            Assert.AreEqual(Travelers.BladeEngravingId, six.engraving.id);
            Assert.AreEqual(1, run.pouch.All.Sum(d => d.faces.Count(f => f.engraving != null)), "刃は1つだけ");
        }

        [Test]
        public void Bakuto_GetsInterest()
        {
            var config = LoadConfig();
            var bakuto = new RunState(config, 3, null, 0, TravelerKind.Bakuto);
            bakuto.GainGold(200 - bakuto.Gold);
            Assert.AreEqual(20, bakuto.GainBattleInterest());
            Assert.AreEqual(220, bakuto.Gold);
            var saifuri = new RunState(config, 3);
            Assert.AreEqual(0, saifuri.GainBattleInterest());
        }

        [Test]
        public void Saikushi_FourForgeChoices()
        {
            var config = LoadConfig();
            Assert.AreEqual(3, new RunState(config, 3).CreateForgeOffer().Count);
            Assert.AreEqual(Travelers.SaikushiForgeChoices, new RunState(config, 3, null, 0, TravelerKind.Saikushi).CreateForgeOffer().Count);
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
    }
}
