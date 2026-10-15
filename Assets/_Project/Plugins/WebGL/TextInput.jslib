// ブラウザの入力欄で文字を打つ（Unity の WebGL の入力欄は日本語の変換（IME）が使えないため）。
// 画面の上に HTML の入力欄を重ねて出し、決定したら結果をしまう。Unity 側（WebTextInput.cs）は毎フレーム SaiNoMichi_TextInputState で決まったかを見て受け取る。
mergeInto(LibraryManager.library, {
  SaiNoMichi_OpenTextInput: function (titlePtr, valuePtr, maxLength) {
    var title = UTF8ToString(titlePtr);
    var value = UTF8ToString(valuePtr);
    window.saiNoMichiTextInput = { state: 0, value: "" };
    var old = document.getElementById("sainomichi-text-input");
    if (old) old.parentNode.removeChild(old);

    var wrap = document.createElement("div");
    wrap.id = "sainomichi-text-input";
    wrap.style.cssText = "position:fixed;left:0;top:0;right:0;bottom:0;background:rgba(0,0,0,0.6);display:flex;align-items:center;justify-content:center;z-index:99999";
    var box = document.createElement("div");
    box.style.cssText = "background:#1c120c;border:2px solid #d9ad52;border-radius:12px;padding:20px 24px;color:#f5ebd1;font-family:serif;text-align:center;box-shadow:0 6px 24px rgba(0,0,0,0.6)";
    var label = document.createElement("div");
    label.textContent = title;
    label.style.cssText = "margin-bottom:12px;font-size:18px;color:#ffd24d";
    var input = document.createElement("input");
    input.type = "text";
    input.value = value;
    if (maxLength > 0) input.maxLength = maxLength;
    input.style.cssText = "font-size:20px;padding:6px 10px;width:260px;border-radius:6px;border:1px solid #d9ad52;background:#f5ebd1;color:#2e1f14;outline:none";
    var row = document.createElement("div");
    row.style.cssText = "margin-top:14px;display:flex;gap:12px;justify-content:center";
    function makeButton(text, primary) {
      var b = document.createElement("button");
      b.textContent = text;
      b.style.cssText = "font-size:16px;padding:6px 18px;border-radius:6px;cursor:pointer;border:1px solid #d9ad52;" +
        (primary ? "background:#d9ad52;color:#2e1f14" : "background:#3a2a1e;color:#f5ebd1");
      return b;
    }
    var ok = makeButton("決定", true);
    var cancel = makeButton("やめる", false);

    function finish(accept) {
      if (wrap.parentNode) wrap.parentNode.removeChild(wrap);
      window.saiNoMichiTextInput = { state: accept ? 1 : 2, value: input.value };
      var canvas = document.querySelector("canvas");
      if (canvas) canvas.focus();
    }
    ok.onclick = function () { finish(true); };
    cancel.onclick = function () { finish(false); };
    // ゲームにキーが届かないようにする。変換中（isComposing）の Enter は確定に使うので、決定にしない
    input.addEventListener("keydown", function (e) {
      e.stopPropagation();
      if (e.key === "Enter" && !e.isComposing && e.keyCode !== 229) finish(true);
      else if (e.key === "Escape") finish(false);
    });
    input.addEventListener("keyup", function (e) { e.stopPropagation(); });
    input.addEventListener("keypress", function (e) { e.stopPropagation(); });

    row.appendChild(ok);
    row.appendChild(cancel);
    box.appendChild(label);
    box.appendChild(input);
    box.appendChild(row);
    wrap.appendChild(box);
    document.body.appendChild(wrap);
    setTimeout(function () { input.focus(); input.select(); }, 0);
  },

  // 0：まだ入力中／1：決定した／2：やめた
  SaiNoMichi_TextInputState: function () {
    var s = window.saiNoMichiTextInput;
    return s ? s.state : 2;
  },

  SaiNoMichi_TextInputValue: function () {
    var s = window.saiNoMichiTextInput;
    var str = s ? s.value : "";
    var size = lengthBytesUTF8(str) + 1;
    var buffer = _malloc(size);
    stringToUTF8(str, buffer, size);
    return buffer;
  }
});
