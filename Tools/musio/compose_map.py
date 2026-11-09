"""マップの BGM「旅路」：D の陽音階（D E G A B）。92 BPM、本編 32 小節をくり返す。
ハープを琴のように8分で刻み、木管が笛のように旋律を吹く。"""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mz
from musiclib import Part, write, PRE, POST

sys.stdout.reconfigure(encoding="utf-8")
BARS = 32
CHORDS = ("D D Bm Bm G G A A "      # A
          "G A D Bm G Em A A "      # B
          "Em Em G G Bm Bm A A "    # C
          "D D Bm Bm G G A A").split()  # D
assert len(CHORDS) == BARS

HARP = {
    "D": "D3 A3 D4 E4 A4 E4 D4 A3",
    "Bm": "B2 A3 B3 D4 E4 D4 B3 A3",
    "G": "G2 D3 G3 B3 D4 B3 G3 D3",
    "A": "A2 E3 A3 B3 E4 B3 A3 E3",
    "Em": "E3 B3 D4 E4 G4 E4 D4 B3",
}
PAD = {"D": "D3 A3 E4", "Bm": "B2 A3 D4", "G": "G2 D3 B3", "A": "A2 E3 B3", "Em": "E3 B3 G4"}
PAD_LOW = {"D": "D3 A3", "Bm": "B2 F#3", "G": "G2 D3", "A": "A2 E3", "Em": "E3 B3"}
BASS = {"D": ("D2", "A2"), "Bm": ("B1", "D2"), "G": ("G1", "D2"), "A": ("A1", "E2"), "Em": ("E2", "B2")}
TIMP = {"D": "D2", "Bm": "B1", "G": "G2", "A": "A2", "Em": "E2"}
TONGUE = {"Em": "E4 B4 G4 B4", "G": "G4 D5 B4 D5", "Bm": "B4 D5 A4 D5", "A": "A4 E5 B4 E5", "D": "D4 A4 E4 A4"}

PHRASE1 = [
    [("A4", 1.5), ("B4", 0.5), ("D5", 2)],
    [("E5", 1), ("D5", 0.5), ("B4", 0.5), ("A4", 2)],
    [("B4", 1.5), ("D5", 0.5), ("E5", 1), ("G5", 1)],
    [("E5", 1), ("D5", 1), ("B4", 2)],
    [("D5", 1.5), ("E5", 0.5), ("G5", 2)],
    [("A5", 1), ("G5", 0.5), ("E5", 0.5), ("D5", 2)],
    [("E5", 1.5), ("D5", 0.5), ("B4", 1), ("A4", 1)],
    [("B4", 1), ("D5", 1), ("E5", 2)],
]
PHRASE2 = [
    [("G5", 1.5), ("A5", 0.5), ("B5", 2)],
    [("A5", 1), ("B5", 0.5), ("A5", 0.5), ("E5", 2)],
    [("D5", 1), ("E5", 1), ("A5", 2)],
    [("G5", 1), ("E5", 1), ("D5", 1), ("B4", 1)],
    [("D5", 1.5), ("E5", 0.5), ("G5", 1), ("A5", 1)],
    [("B5", 2), ("A5", 1), ("G5", 1)],
    [("E5", 2), ("D5", 1), ("E5", 1)],
    [("A4", 3), (None, 1)],
]
STR_MELODY = [
    [("E4", 2), ("G4", 1), ("A4", 1)],
    [("B4", 3), ("A4", 1)],
    [("G4", 2), ("A4", 1), ("B4", 1)],
    [("D5", 4)],
    [("B4", 2), ("D5", 1), ("E5", 1)],
    [("D5", 2), ("B4", 1), ("A4", 1)],
    [("B4", 2), ("A4", 1), ("E4", 1)],
    [("A4", 4)],
]
COUNTER = [
    [(None, 2), ("B5", 2)],
    [("A5", 2), ("G5", 2)],
    [("B5", 4)],
    [("A5", 2), ("B5", 1), ("D6", 1)],
    [("D6", 4)],
    [("B5", 2), ("A5", 2)],
    [("E5", 4)],
    [("E5", 2), ("A5", 2)],
]


def octave_down(items):
    from musiclib import p
    return [((p(q) - 12) if q is not None else None, l) for q, l in items]


woods, strings, pizz, harp, toms, timp, glock, tongue = (Part() for _ in range(8))

for bar in range(1, BARS + 1):
    ch = CHORDS[bar - 1]
    sec = (bar - 1) // 8  # 0:A 1:B 2:C 3:D
    i = (bar - 1) % 8
    # ハープ（琴の代わり）：8分の分散和音
    hv = [70, 80, 72, 86][sec]
    for k, q in enumerate(HARP[ch].split()):
        harp.add(bar, k * 0.5, q, 0.5, hv + (8 if k == 0 else 0))
    # 弦：和音の伸ばし（C では旋律を弾くので低い2音だけ）
    pad = (PAD_LOW if sec == 2 else PAD)[ch]
    strings.seq(bar, [(pad.split(), 4)], 80)
    # 旋律
    if sec == 0:
        woods.seq(bar, PHRASE1[i], 84)
    elif sec == 1:
        woods.seq(bar, PHRASE2[i], 92)
    elif sec == 2:
        strings.seq(bar, STR_MELODY[i], 96)
        woods.seq(bar, COUNTER[i], 70)
    else:
        woods.seq(bar, PHRASE1[i], 100)
        strings.seq(bar, octave_down(PHRASE1[i]), 96)
    # ピチカートの低音（B と D）
    if sec in (1, 3):
        r, s = BASS[ch]
        pizz.seq(bar, [(r, 1), (None, 0.5), (s, 0.5), (r, 1), (None, 0.5), (s, 0.5)], 92 if sec == 3 else 84, "Pizzicato")
    # 太鼓（B と D は控えめに。各部の終わりに小さなフィル）
    if sec in (1, 3):
        toms.add(bar, 0, 60, 1, 92 if sec == 3 else 84)
        toms.add(bar, 2.5, 64, 0.5, 58)
        toms.add(bar, 3, 60, 1, 74)
    if i == 7:
        for k, (b, q, v) in enumerate([(2, 64, 60), (2.5, 64, 70), (3, 60, 82), (3.5, 60, 94)]):
            toms.add(bar, b, q, 0.5, v)
    # ティンパニ：各部の頭、B と D の終わりに連打
    if i == 0:
        timp.add(bar, 0, TIMP[ch], 2, [80, 96, 86, 104][sec], "Timpani")
    if i == 7 and sec in (1, 3):
        for k in range(4):
            timp.add(bar, 3 + k * 0.25, "A2", 0.25, 70 + k * 10, "Timpani")
    # 鉄琴のきらめき（各部の4小節目と8小節目の後半）
    if i in (3, 7):
        for k, q in enumerate(["D6", "E6", "G6", "A6"] if i == 3 else ["A6", "G6", "E6", "D6"]):
            glock.add(bar, 2 + k * 0.5, q, 0.5, 56 + k * 4)
    # スリットドラム：C の4分の刻み
    if sec == 2:
        for k, q in enumerate(TONGUE[ch].split()):
            tongue.add(bar, k, q, 1, 74 if k == 0 else 62)

# 強弱（CC1）
for sec, (w, s) in enumerate([(64, 52), (82, 70), (58, 84), (96, 88)]):
    b = sec * 8 + 1
    woods.dyn(b, 0, w)
    strings.dyn(b, 0, s)
    woods.dyn(b + 7, 3.5, w)
    strings.dyn(b + 7, 3.5, s)
# D の終わりは少し引いて、A の静かな頭へつなぐ
strings.dyn(31, 0, 88)
strings.dyn(32, 3.5, 60)
woods.dyn(32, 3.5, 70)

song = mz.call("get_song")
tid = {t["name"]: t["track_id"] for t in song["tracks"]}
mz.call("set_song", {"name": "SaiNoMichi Map", "tempo": 92, "key": "D major", "mood": "Journey", "reverb_room": "Hall"})
# 既にある音（やり直したとき）を消す
for t in song["tracks"]:
    for c in t.get("clips", []):
        mz.call("delete_clip", {"clip_id": c["clip_id"]})

for name, part, hum in [("woods", woods, 4), ("strings", strings, 3), ("pizz", pizz, 5), ("harp", harp, 6),
                        ("toms", toms, 4), ("perc", timp, 3), ("glock", glock, 4), ("tongue", tongue, 5)]:
    cid, n = write(tid[name], part, BARS, name, hum, seed=len(name))
    print(name, cid, n)

mz.call("set_track", {"track_id": tid["pizz"], "articulation": "Pizzicato"})
mz.call("set_track", {"track_id": tid["perc"], "articulation": "Timpani"})
mz.call("set_sections", {"sections": [
    {"name": "Loop pre", "start_bar": 1, "bars": PRE},
    {"name": "A", "start_bar": PRE + 1, "bars": 8},
    {"name": "B", "start_bar": PRE + 9, "bars": 8},
    {"name": "C", "start_bar": PRE + 17, "bars": 8},
    {"name": "D", "start_bar": PRE + 25, "bars": 8},
    {"name": "Loop post", "start_bar": PRE + BARS + 1, "bars": POST},
]})
print(mz.call("save_song"))
