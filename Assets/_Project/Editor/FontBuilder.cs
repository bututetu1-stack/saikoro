using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace SaiNoMichi.EditorTools
{
    /// <summary>
    /// 日本語の TMP フォント（動的に文字を足していく）を作る。ブラウザ（WebGL）では OS のフォントが使えないので、
    /// プロジェクトに入れたフォント（Zen角ゴシックNew、SIL Open Font License）から作り、Resources に置く。
    /// </summary>
    public static class FontBuilder
    {
        // 開発者の要望で、味のある明朝に（Zen Old Mincho Bold。SIL OFL。Fonts/ZenOldMincho-OFL.txt）。前は Zen角ゴシックNew
        const string SourcePath = "Assets/_Project/Fonts/ZenOldMincho-Bold.ttf";
        const string OutputPath = "Assets/_Project/Resources/SaiNoMichiFont.asset";

        [MenuItem("SaiNoMichi/Build Font")]
        public static void Build()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
            if (font == null)
            {
                Debug.LogError($"[賽ノ道] フォント {SourcePath} がありません。");
                return;
            }
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Resources")) AssetDatabase.CreateFolder("Assets/_Project", "Resources");
            AssetDatabase.DeleteAsset(OutputPath);

            // 使う文字だけ、遊んでいるときに足していく（動的）。atlas が埋まったら次の atlas を作る
            // 文字が粗く（ガビガビに）見えたので、大きく描いて余白（padding）も広くする。
            // 余白を 6→12 にしたので、同じ太さの縁取りは outlineWidth を約半分（×0.54）にする（UI の各所はそう直してある）
            var asset = TMP_FontAsset.CreateFontAsset(font, 90, 12, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            asset.name = "SaiNoMichiFont";
            AssetDatabase.CreateAsset(asset, OutputPath);
            // atlas の画像とマテリアルは、フォントの資産の中に一緒にしまう
            foreach (var tex in asset.atlasTextures)
            {
                tex.name = "SaiNoMichiFont Atlas";
                AssetDatabase.AddObjectToAsset(tex, asset);
            }
            // 縁取りがきれいに出る、ふつうの（Mobile でない）SDF のシェーダーにする
            var shader = Shader.Find("TextMeshPro/Distance Field");
            if (shader != null) asset.material.shader = shader;
            asset.material.name = "SaiNoMichiFont Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[賽ノ道] 日本語フォント {OutputPath} を作りました。");
        }
    }
}
