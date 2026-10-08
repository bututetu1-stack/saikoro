// ブラウザのクリップボードに文字を書く（結果画面の「結果をコピー」）。
// unityroom は iframe の中なので navigator.clipboard が許可されないことがある。そのときは textarea ＋ execCommand で試す。
mergeInto(LibraryManager.library, {
  SaiNoMichi_CopyToClipboard: function (ptr) {
    var text = UTF8ToString(ptr);
    var ok = 0;
    try {
      var area = document.createElement("textarea");
      area.value = text;
      area.style.position = "fixed";
      area.style.left = "-9999px";
      document.body.appendChild(area);
      area.focus();
      area.select();
      ok = document.execCommand("copy") ? 1 : 0;
      document.body.removeChild(area);
    } catch (e) {
      ok = 0;
    }
    if (!ok && navigator.clipboard && navigator.clipboard.writeText) {
      try {
        navigator.clipboard.writeText(text);
        ok = 1;
      } catch (e) {
        ok = 0;
      }
    }
    // フォーカスをゲームに戻す
    var canvas = document.querySelector("canvas");
    if (canvas) canvas.focus();
    return ok;
  }
});
