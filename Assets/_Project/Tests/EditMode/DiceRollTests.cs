using System.Linq;
using NUnit.Framework;
using SaiNoMichi.Core;

namespace SaiNoMichi.Tests
{
    public class DiceRollTests
    {
        TestDice factory;

        [SetUp]
        public void SetUp() => factory = new TestDice();

        [TearDown]
        public void TearDown() => factory.DestroyAll();

        [Test]
        public void Roll_AlwaysReturnsOneOfTheFaces()
        {
            var die = factory.High();
            var rng = new System.Random(1);

            for (int i = 0; i < 200; i++)
            {
                CollectionAssert.Contains(new[] { 4, 5, 6 }, die.Roll(rng));
            }
        }

        [Test]
        public void Roll_HitsEveryFaceIndex()
        {
            var die = factory.Normal();
            var rng = new System.Random(2);

            var seen = Enumerable.Range(0, 300).Select(_ => die.RollFaceIndex(rng)).Distinct().OrderBy(x => x);

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, seen);
        }

        [Test]
        public void RunRandom_SameSeed_GivesSameSequences()
        {
            var r1 = new RunRandom(12345);
            var r2 = new RunRandom(12345);

            Assert.AreEqual(r1.Map.Next(), r2.Map.Next());
            Assert.AreEqual(r1.Battle.Next(), r2.Battle.Next());
            Assert.AreEqual(r1.Reward.Next(), r2.Reward.Next());
        }

        [Test]
        public void RunRandom_BattleUsage_DoesNotShiftRewardStream()
        {
            var r1 = new RunRandom(777);
            var r2 = new RunRandom(777);

            for (int i = 0; i < 10; i++) r1.Battle.Next(); // 戦闘で余計に振っても

            Assert.AreEqual(r2.Reward.Next(), r1.Reward.Next()); // 報酬は変わらない
        }
    }
}
