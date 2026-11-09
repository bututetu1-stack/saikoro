"""戦闘の BGM「賽の勝負」：D の陰音階（D Eb G A Bb）の旋律に短調の和音。152 BPM、本編 40 小節をくり返す。
弦のスピッカートが8分で刻み、大太鼓（和太鼓の代わり）が打ち、木管と金管が旋律を受け渡す。"""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mz
from musiclib import Part, write, PRE, POST, p

sys.stdout.reconfigure(encoding="utf-8")
BARS = 40
CHORDS = ("Dm EbD Dm EbD Dm EbD Bb A "    # A：刻みだけ
          "Dm EbD Dm EbD Bb Gm A A "      # B：旋律1（木管）
          "Gm A Dm Dm Bb Eb A A "         # C：旋律2（金管）
          "Dm Dm Eb Eb Dm Dm A A "        # D：落として、ためる
          "Dm EbD Dm EbD Bb Gm A A").split()  # E：旋律1（全員）
assert len(CHORDS) == BARS

OSTINATO = {
    "Dm": "D3 D3 D4 D3 Eb4 D3 D4 A3",
    "EbD": "D3 D3 Eb4 D3 G4 D3 Eb4 Bb3",
    "Bb": "Bb2 Bb2 Bb3 Bb2 D4 Bb2 Bb3 F3",
    "Gm": "G2 G2 G3 G2 Bb3 G2 G3 D3",
    "A": "A2 A2 A3 A2 Bb3 A2 A3 E3",
    "Eb": "Eb3 Eb3 Eb4 Eb3 G4 Eb3 Eb4 Bb3",
}
ROOT = {"Dm": "D2", "EbD": "D2", "Bb": "Bb1", "Gm": "G2", "A": "A2", "Eb": "Eb2"}
PAD = {"Dm": "D3 F3 A3", "EbD": "Eb3 G3 Bb3", "Bb": "D3 F3 Bb3", "Gm": "D3 G3 Bb3", "A": "E3 A3 C#4", "Eb": "Eb3 G3 Bb3"}
STAB = {"Dm": "D4 F4 A4", "EbD": "Eb4 G4 Bb4", "Bb": "D4 F4 Bb4", "Gm": "D4 G4 Bb4", "A": "E4 A4 C#5", "Eb": "Eb4 G4 Bb4"}
TIMP = {"Dm": "D2", "EbD": "D2", "Bb": "D2", "Gm": "G2", "A": "A2", "Eb": "Eb2"}

MEL1 = [
    [("D5", 1), ("Eb5", 0.5), ("D5", 0.5), ("A4", 2)],
    [("Bb4", 1), ("A4", 0.5), ("G4", 0.5), ("Eb5", 2)],
    [("D5", 1), ("A4", 1), ("Bb4", 1), ("D5", 1)],
    [("Eb5", 1.5), ("D5", 0.5), ("Eb5", 1), ("G5", 1)],
    [("A5", 2), ("G5", 1), ("Eb5", 1)],
    [("D5", 1.5), ("Eb5", 0.5), ("D5", 1), ("Bb4", 1)],
    [("A4", 3), ("Bb4", 1)],
    [("A4", 3.5), (None, 0.5)],
]
MEL2 = [
    [("G4", 1.5), ("A4", 0.5), ("Bb4", 2)],
    [("A4", 1), ("Bb4", 1), ("A4", 2)],
    [("D5", 2), ("A4", 1), ("Bb4", 1)],
    [("A4", 2), ("G4", 1), ("Eb4", 1)],
    [("D4", 1.5), ("Eb4", 0.5), ("G4", 2)],
    [("G4", 1), ("Bb4", 1), ("G4", 1), ("Eb4", 1)],
    [("A4", 2), ("Bb4", 1), ("A4", 1)],
    [("A4", 4)],
]
RUN = "D4 Eb4 G4 A4 Bb4 D5 Eb5 G5".split()  # 陰音階の駆け上がり（琴の流し）


def shift(items, semis):
    return [((p(q) + semis) if q is not None else None, l) for q, l in items]


woods, brass, strings, spic, harp, toms, timp, piatti = (Part() for _ in range(8))

for bar in range(1, BARS + 1):
    ch = CHORDS[bar - 1]
    sec = (bar - 1) // 8   # 0:A 1:B 2:C 3:D 4:E
    i = (bar - 1) % 8
    # スピッカートの刻み
    if sec == 3 and i < 4:
        r = ROOT[ch]
        for k in range(8):
            spic.add(bar, k * 0.5, p(r) + 12, 0.45, 96 if k % 2 == 0 else 80, "Spiccato")
    elif sec == 3 and i >= 6:
        r = ROOT[ch]
        for k in range(16):
            spic.add(bar, k * 0.25, p(r) + 12, 0.22, 84 + (i - 6) * 16 + k, "Spiccato")
    else:
        for k, q in enumerate(OSTINATO[ch].split()):
            spic.add(bar, k * 0.5, q, 0.45, (112 if k in (0, 3, 6) else 90) + (6 if sec == 4 else 0), "Spiccato")
    # 弦の伸ばし：A は低音だけ、ほかは和音
    if sec == 0:
        strings.seq(bar, [([ROOT[ch], p(ROOT[ch]) + 12], 4)], 80)
    else:
        strings.seq(bar, [([ROOT[ch]] + PAD[ch].split(), 4)], 84)
    # 旋律
    if sec == 1:
        woods.seq(bar, MEL1[i], 96)
    elif sec == 2:
        brass.seq(bar, MEL2[i], 100)
        woods.seq(bar, shift(MEL2[i], 12), 88)
    elif sec == 4:
        woods.seq(bar, MEL1[i], 104)
        brass.seq(bar, shift(MEL1[i], -12), 104)
        strings.seq(bar, shift(MEL1[i], -12), 96)
    # 金管の短い打ち込み（A：変わり目、E：裏拍）
    if sec == 0 and ch in ("EbD", "Bb", "A"):
        for b in (0, 2.5):
            for q in STAB[ch].split():
                brass.add(bar, b, q, 0.3, 108, "Staccatissimo")
    if sec == 3 and i >= 6:
        brass.seq(bar, [("A2 E3 A3".split(), 4)], 100)  # ためる
    # 太鼓
    if sec == 3 and i < 4:
        toms.add(bar, 0, 60, 1, 110)
        toms.add(bar, 2.5, 64, 0.5, 80)
    elif sec == 3 and i < 6:
        for k in range(8):
            toms.add(bar, k * 0.5, 60 if k % 2 == 0 else 64, 0.5, 90 + k * 3)
    elif sec == 3:
        for k in range(16):
            toms.add(bar, k * 0.25, 60 if k % 4 == 0 else 64, 0.25, 90 + (i - 6) * 16 + k)
    elif i == 7:
        for b, q, v in [(0, 60, 120), (0.75, 64, 80), (1.5, 64, 92)]:
            toms.add(bar, b, q, 0.5, v)
        for k in range(8):
            toms.add(bar, 2 + k * 0.25, 64 if k % 2 else 60, 0.25, 90 + k * 4)
    else:
        for b, q, v in [(0, 60, 122), (0.75, 64, 80), (1.5, 64, 92), (2, 60, 110), (3, 60, 104), (3.5, 64, 86)]:
            toms.add(bar, b, q, 0.5, v)
    # ティンパニ
    if sec in (1, 4) or i == 0:
        timp.add(bar, 0, TIMP[ch], 1, 104 if i == 0 else 88, "Timpani")
    if sec == 3 and i == 7:
        for k in range(8):
            timp.add(bar, 2 + k * 0.25, "A2", 0.25, 80 + k * 6, "Timpani")
    # シンバル：B・C・E の頭、D の終わり
    if i == 0 and sec in (1, 2, 4):
        piatti.add(bar, 0, 62, 4, 112)
    # ハープ：各部の終わりに陰音階の駆け上がり、C は高い分散和音
    if i == 7:
        for k, q in enumerate(RUN):
            harp.add(bar, 2 + k * 0.25, q, 0.25, 84 + k * 4)
    if sec == 2:
        for k, q in enumerate(STAB[ch].split() * 2 + STAB[ch].split()[:2]):
            harp.add(bar, k * 0.5, p(q) + 12, 0.5, 76)

# 強弱（CC1）
LEVELS = {  # 部ごとの (木管, 金管, 弦の伸ばし, スピッカート)
    0: (70, 96, 64, 92), 1: (96, 90, 82, 92), 2: (62, 104, 86, 94), 3: (60, 80, 44, 84), 4: (108, 108, 100, 104)}
for sec, (w, b, s, sp) in LEVELS.items():
    bar = sec * 8 + 1
    for part, v in ((woods, w), (brass, b), (strings, s), (spic, sp)):
        part.dyn(bar, 0, v)
        part.dyn(bar + 7, 3.5, v)
# D はためて大きく
strings.dyn(25, 0, 40); strings.dyn(32, 3.5, 112)
spic.dyn(29, 0, 80); spic.dyn(32, 3.5, 112)
brass.dyn(31, 0, 60); brass.dyn(32, 3.5, 118)

song = mz.call("get_song")
tid = {t["name"]: t["track_id"] for t in song["tracks"]}
mz.call("set_song", {"name": "SaiNoMichi Battle", "tempo": 152, "key": "D minor", "mood": "Battle", "reverb_room": "Hall"})
for t in song["tracks"]:
    for c in t.get("clips", []):
        mz.call("delete_clip", {"clip_id": c["clip_id"]})

for name, part, hum in [("woods", woods, 4), ("brass", brass, 4), ("strings", strings, 3), ("spiccato", spic, 6),
                        ("harp", harp, 5), ("toms", toms, 5), ("perc", timp, 3), ("piatti", piatti, 2)]:
    cid, n = write(tid[name], part, BARS, name, hum, seed=len(name) + 10)
    print(name, cid, n)

mz.call("set_track", {"track_id": tid["spiccato"], "articulation": "Spiccato"})
mz.call("set_track", {"track_id": tid["perc"], "articulation": "Timpani"})
mz.call("set_sections", {"sections": [
    {"name": "Loop pre", "start_bar": 1, "bars": PRE},
    {"name": "A", "start_bar": PRE + 1, "bars": 8},
    {"name": "B", "start_bar": PRE + 9, "bars": 8},
    {"name": "C", "start_bar": PRE + 17, "bars": 8},
    {"name": "D", "start_bar": PRE + 25, "bars": 8},
    {"name": "E", "start_bar": PRE + 33, "bars": 8},
    {"name": "Loop post", "start_bar": PRE + BARS + 1, "bars": POST},
]})
print(mz.call("save_song"))
