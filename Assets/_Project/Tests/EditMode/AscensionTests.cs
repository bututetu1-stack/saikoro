using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Run;
using UnityEditor;

namespace SaiNoMichi.Tests
{
    /// <summary>段位（仕様書 第12章）。</summary>
    public class AscensionTests
    {
        const string ConfigPath = "Assets/_Project/Data/GameConfig.asset";
        TestDice factory;

        [SetUp]
        public void SetUp() => factory = new TestDice();

        [TearDown]
        public void TearDown() => factory.DestroyAll();

        static GameConfig LoadConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config == null) Assert.Ignore("GameConfig がありません（SaiNoMichi/Build All Data を実行してください）。");
            return config;
        }

        static Intent Attack(int v) => new Intent(IntentType.Attack, v);

        EnemyData Enemy(EnemyKind kind, int hp, string id = "enemy")
        {
            var e = factory.Enemy(hp, Attack(5));
            e.kind = kind;
            e.id = id;
            return e;
        }

        static EnemyState Fight(RunState run, EnemyData enemy) =>
            new BattleState(run.player, new[] { enemy }, run.pouch, new System.Random(0), run.effects, run).enemies[0];

        [Test]
        public void Level3_LowerMaxHp_Level5_ChippedDice()
        {
            var config = LoadConfig();
            var a0 = new RunState(config, 5);
            var a3 = new RunState(config, 5, null, 3);
            var a5 = new RunState(config, 5, null, 5);
            Assert.AreEqual(0, a0.Ascension);
            Assert.AreEqual(a0.player.maxHp - AscensionRules.StartMaxHpLoss, a3.player.maxHp);
            Assert.AreEqual(a3.player.maxHp, a3.player.hp);
            Assert.IsFalse(a3.pouch.All.Any(d => d.data.id == AscensionRules.ChippedDiceId));
            Assert.IsTrue(a5.pouch.All.Any(d => d.data.id == AscensionRules.ChippedDiceId), "段位5：欠け賽を持って旅に出る");
        }

        [Test]
        public void Level1_EliteHp_Level4_BossHp()
        {
            var config = LoadConfig();
            int Hp(int asc, EnemyKind kind) => Fight(new RunState(config, 5, null, asc), Enemy(kind, 100)).maxHp;
            Assert.AreEqual(Hp(0, EnemyKind.Elite) * 120 / 100, Hp(1, EnemyKind.Elite), 1);
            Assert.AreEqual(Hp(0, EnemyKind.Boss), Hp(3, EnemyKind.Boss), "段位3まではボスは変わらない");
            Assert.AreEqual(Hp(0, EnemyKind.Boss) * 115 / 100, Hp(4, EnemyKind.Boss), 1);
            Assert.AreEqual(Hp(0, EnemyKind.Normal), Hp(10, EnemyKind.Normal), "通常の敵の HP は変わらない");
        }

        [Test]
        public void Level7_NormalEnemyAttackPlusOne()
        {
            var config = LoadConfig();
            int Value(int asc, EnemyKind kind) => Fight(new RunState(config, 5, null, asc), Enemy(kind, 50)).CurrentIntent.value;
            Assert.AreEqual(Value(6, EnemyKind.Normal) + AscensionRules.NormalAttackBonus, Value(7, EnemyKind.Normal));
            Assert.AreEqual(Value(0, EnemyKind.Elite), Value(7, EnemyKind.Elite), "強敵の攻撃は変わらない");
        }

        [Test]
        public void Level2_ShopPricesUp()
        {
            var config = LoadConfig();
            var cheap = new RunState(config, 9).CreateShop();
            var dear = new RunState(config, 9, null, 2).CreateShop();
            Assert.AreEqual(cheap.items.Count, dear.items.Count);
            for (int i = 0; i < cheap.items.Count; i++)
            {
                if (cheap.items[i].price <= 0) continue;
                Assert.GreaterOrEqual(dear.items[i].price, cheap.items[i].price);
            }
            Assert.Greater(dear.items.Sum(x => x.price), cheap.items.Sum(x => x.price));
            Assert.Greater(dear.RemovePrice, cheap.RemovePrice);
        }

        [Test]
        public void Level6_RestHealsLess()
        {
            var config = LoadConfig();
            var a5 = new RunState(config, 5, null, 5); // 最大HP は段位3から同じ
            var a6 = new RunState(config, 5, null, 6);
            Assert.AreEqual(a5.RestHealAmount * AscensionRules.RestHealPercent / 100, a6.RestHealAmount, 1);
            Assert.Less(a6.RestHealAmount, a5.RestHealAmount);
        }

        [Test]
        public void Level8_MoreTraps()
        {
            var config = LoadConfig();
            if (!config.useBranchingBoard) Assert.Ignore("分岐の盤面だけ");
            int Traps(int asc) => Enumerable.Range(1, 30).Sum(seed => RunState.BoardOfLayer(config, seed, 0, asc).tiles.Count(t => t.type == TileType.Trap));
            Assert.Greater(Traps(8), Traps(7));
        }

        [Test]
        public void Level9_BeforeBossNotGuaranteed()
        {
            var config = LoadConfig();
            if (!config.useBranchingBoard) Assert.Ignore("分岐の盤面だけ");
            bool AlwaysRest(int asc) => Enumerable.Range(1, 30).All(seed =>
            {
                var board = RunState.BoardOfLayer(config, seed, 0, asc);
                return board.tiles.Where(t => t.next.Contains(board.Goal)).All(t => t.type == TileType.Rest);
            });
            Assert.IsTrue(AlwaysRest(8), "段位8まではボスの手前は必ず休憩");
            Assert.IsFalse(AlwaysRest(9), "段位9では休憩とは限らない");
        }

        [Test]
        public void Level10_HachimenSealsEveryRoundWhenLow()
        {
            var config = LoadConfig();
            foreach (int asc in new[] { 9, 10 })
            {
                var run = new RunState(config, 5, null, asc);
                var boss = Enemy(EnemyKind.Boss, 200, AscensionRules.HachimenId);
                var battle = new BattleState(run.player, new[] { boss }, run.pouch, new System.Random(0), run.effects, run);
                battle.enemies[0].hp = AscensionRules.HachimenPhase3Hp;
                battle.Resolve();
                bool sealedAny = run.pouch.All.Any(d => d.state == DiceState.Sealed);
                Assert.AreEqual(asc >= 10, sealedAny, $"段位{asc}");
            }
        }

        [Test]
        public void Save_KeepsAscension()
        {
            var config = LoadConfig();
            var run = new RunState(config, 5, null, 4);
            var restored = RunState.Restore(config, RunSave.FromJson(run.CreateSave().ToJson()));
            Assert.AreEqual(4, restored.Ascension);
        }
    }
}
