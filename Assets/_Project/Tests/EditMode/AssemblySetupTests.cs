using NUnit.Framework;

namespace SaiNoMichi.Tests
{
    // アセンブリ定義とテストランナーの配線を確かめるだけのテスト。
    // ステップ2で本物のテストが入ったら削除してよい。
    public class AssemblySetupTests
    {
        [Test]
        public void TestRunnerIsWired()
        {
            Assert.Pass();
        }
    }
}
