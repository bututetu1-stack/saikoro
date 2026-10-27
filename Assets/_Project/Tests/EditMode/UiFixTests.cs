using NUnit.Framework;
using SaiNoMichi.UI;
using UnityEngine;

namespace SaiNoMichi.Tests
{
    /// <summary>2026-10-27 の感想への直し（シードの入力・浮かび上がりの途中で隠したとき）。</summary>
    public class UiFixTests
    {
        [Test]
        public void ParseSeed_ReadsHalfAndFullWidthDigits()
        {
            Assert.AreEqual(1234, StarterView.ParseSeed("1234"));
            Assert.AreEqual(1234, StarterView.ParseSeed("１２３４"), "全角の数字も読む");
            Assert.AreEqual(1234, StarterView.ParseSeed(" 1,234 "), "空白・カンマは無視");
            Assert.AreEqual(int.MaxValue, StarterView.ParseSeed(int.MaxValue.ToString()));
        }

        [Test]
        public void ParseSeed_RejectsBadInput()
        {
            Assert.IsNull(StarterView.ParseSeed(""));
            Assert.IsNull(StarterView.ParseSeed("abc"));
            Assert.IsNull(StarterView.ParseSeed("0"), "0 は使えない");
            Assert.IsNull(StarterView.ParseSeed("-5"));
            Assert.IsNull(StarterView.ParseSeed("99999999999"), "int に入らない");
        }

        [Test]
        public void FadeInGuard_RestoresWhenHiddenMidFade()
        {
            var go = new GameObject("View", typeof(RectTransform));
            try
            {
                var group = go.AddComponent<CanvasGroup>();
                var rt = (RectTransform)go.transform;
                rt.anchoredPosition = new Vector2(0, -12);
                group.alpha = 0f;
                var guard = go.AddComponent<UIAnim.FadeInGuard>();
                guard.group = group;
                guard.rt = rt;
                guard.home = Vector2.zero;
                guard.fading = true;

                guard.RestoreIfFading(); // 浮かび上がりの途中で隠された（プレイ中は OnDisable から呼ばれる。EditMode では OnDisable が呼ばれないので直接）

                Assert.AreEqual(1f, group.alpha, "透明のまま残らない");
                Assert.AreEqual(Vector2.zero, rt.anchoredPosition, "元の位置に戻る");
                Assert.IsFalse(guard.fading);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void FadeInGuard_DoesNothingAfterFinish()
        {
            var go = new GameObject("View", typeof(RectTransform));
            try
            {
                var group = go.AddComponent<CanvasGroup>();
                var guard = go.AddComponent<UIAnim.FadeInGuard>();
                guard.group = group;
                guard.fading = false;
                group.alpha = 0.3f; // ほかの演出が変えた透明度は、そのままにする
                guard.RestoreIfFading();
                Assert.AreEqual(0.3f, group.alpha, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DialogLabel_SplitsLongChoiceAtBracket()
        {
            Assert.AreEqual("行列についていく\n<size=80%>（次の 3 ターン、移動の出目+2）</size>", MapView.DialogLabel("行列についていく（次の 3 ターン、移動の出目+2）"));
            Assert.AreEqual("立ち去る", MapView.DialogLabel("立ち去る"));
            Assert.AreEqual("短い（10G）", MapView.DialogLabel("短い（10G）"), "短いものは分けない");
            Assert.AreEqual("一行目\n二行目（x）", MapView.DialogLabel("一行目\n二行目（x）"), "もう改行があれば、そのまま");
        }
    }
}
