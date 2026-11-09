"""強敵の BGM「鬼気」：E の陰音階（E F A B C）の旋律に短調の和音。138 BPM、本編 40 小節をくり返す。
ふつうの戦闘より低く重く：低い弦の刻み・3+3+2 の金管の打ち込み・重い太鼓。旋律は弦がオクターブで歌う。"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mz
from musiclib import Part, write, PRE, POST, p

sys.stdout.reconfigure(encoding="utf-8")
BARS = 40
CHORDS = ("Em FE Em FE Em FE C B "      # A：刻みと打ち込み
          "Em FE Em FE C Am B B "       # B：旋律（弦）
          "Am B Em Em C F B B "         # C：木管の細かい動き・金管の呼びかけ
          "Em Em F F Em Em B B "        # D：ためる
          "Em FE Em FE C Am B B").split()  # E：旋律（金管と弦）
assert len(CHORDS) == BARS

OSTINATO = {
    "Em": "E2 E2 E3 E2 F3 E2 E3 B2",
    "FE": "E2 E2 F3 E2 A3 E2 F3 C3",
    "F": "F2 F2 F3 F2 A3 F2 F3 C3",
    "C": "C2 C2 C3 C2 E3 C2 C3 G2",
    "Am": "A1 A1 A2 A1 C3 A1 A2 E2",
    "B": "B1 B1 B2 B1 C3 B1 B2 F#2",
}
ROOT = {"Em": "E2", "FE": "E2", "F": "F2", "C": "C2", "Am": "A1", "B": "B1"}
PAD = {"Em": "E3 G3 B3", "FE": "F3 A3 C4", "F": "F3 A3 C4", "C": "E3 G3 C4", "Am": "E3 A3 C4", "B": "F#3 B3 D#4"}
STAB = {"Em": "E3 B3 E4", "FE": "F3 C4 F4", "F": "F3 C4 F4", "C": "C3 G3 C4", "Am": "A2 E3 A3", "B": "B2 F#3 B3"}
WOOD_FIG = {"Em": "E5 B5 E6 B5 F5 B5 E6 B5", "FE": "F5 C6 F6 C6 E5 C6 F6 C6", "F": "F5 C6 F6 C6 A5 C6 F6 C6",
            "C": "E5 G5 C6 G5 E5 G5 C6 G5", "Am": "A5 C6 E6 C6 A5 C6 E6 C6", "B": "B5 D#6 F#6 D#6 B5 D#6 F#6 D#6"}
TIMP = {"Em": "E2", "FE": "E2", "F": "F2", "C": "C2", "Am": "A2", "B": "B2"}

THEME = [
    [("B4", 1), ("E5", 1), ("F5", 1.5), ("E5", 0.5)],
    [("C5", 2), ("B4", 1), ("A4", 1)],
    [("B4", 1), ("E5", 1), ("B5", 2)],
    [("A5", 1), ("F5", 1), ("E5", 1), ("C5", 1)],
    [("E5", 1.5), ("F5", 0.5), ("E5", 1), ("C5", 1)],
    [("A4", 2), ("C5", 1), ("E5", 1)],
    [("E5", 1), ("C5", 1), ("B4", 2)],   # B の和音（F#）とぶつかる F は避ける
    [("B4", 3.5), (None, 0.5)],
]
CALL = [  # C の金管の呼びかけ（ホルン風）
    [("E4", 2), ("A4", 2)],
    [("B4", 3), (None, 1)],
    [("E4", 1), ("B4", 1), ("C5", 2)],
    [("B4", 4)],
    [("C5", 2), ("B4", 1), ("A4", 1)],
    [("A4", 2), ("C5", 2)],
    [("B4", 4)],
    [("B4", 2), (None, 2)],
]
RUN = "E4 F4 A4 B4 C5 E5 F5 A5".split()


def shift(items, semis):
    return [((p(q) + semis) if q is not None else None, l) for q, l in items]


woods, brass, strings, spic, harp, toms, timp, piatti = (Part() for _ in range(8))

for bar in range(1, BARS + 1):
    ch = CHORDS[bar - 1]
    sec = (bar - 1) // 8   # 0:A 1:B 2:C 3:D 4:E
    i = (bar - 1) % 8
    # 低い弦の刻み（D の後半は16分で詰める）
    if sec == 3 and i >= 4:
        r = p(ROOT[ch]) + 12
        n = 8 if i < 6 else 16
        for k in range(n):
            spic.add(bar, k * 4 / n, r, 4 / n * 0.9, 86 + (i - 4) * 8 + k, "Spiccato")
    else:
        for k, q in enumerate(OSTINATO[ch].split()):
            spic.add(bar, k * 0.5, q, 0.45, (114 if k in (0, 3, 6) else 88) + (6 if sec == 4 else 0), "Spiccato")
    # 弦：和音の伸ばし。B と E は旋律もオクターブで
    if sec == 0:
        strings.seq(bar, [([ROOT[ch], p(ROOT[ch]) + 12], 4)], 80)
    else:
        strings.seq(bar, [([ROOT[ch]] + PAD[ch].split(), 4)], 84)
    if sec in (1, 4):
        strings.seq(bar, THEME[i], 100)
        strings.seq(bar, shift(THEME[i], -12), 96)
    # 金管：A と E は 3+3+2 の打ち込み、C は呼びかけ、E は旋律も
    if sec in (0, 4):
        for b in (0, 1.5, 3):
            for q in STAB[ch].split():
                brass.add(bar, b, q, 0.3, (112 if b == 0 else 100) - (20 if sec == 0 else 0), "Staccatissimo")  # A は控えめ
    if sec == 2:
        brass.seq(bar, CALL[i], 100)
    if sec == 4:
        brass.seq(bar, shift(THEME[i], -12), 104)
    if sec == 3 and i >= 6:
        brass.seq(bar, [("B1 F#2 B2".split(), 4)], 100)
    # 木管：C は細かい8分の動き（短く）、E は旋律を高く
    if sec == 2:
        for k, q in enumerate(WOOD_FIG[ch].split()):
            woods.add(bar, k * 0.5, q, 0.3, 92 if k % 2 == 0 else 78, "Staccatissimo")
    if sec == 4:
        woods.seq(bar, shift(THEME[i], 12), 96)
    # 太鼓：重い半分の速さ
    if sec == 3 and i < 4:
        toms.add(bar, 0, 60, 2, 118)
        toms.add(bar, 2, 67, 1, 96)
    elif sec == 3:
        n = 8 if i < 6 else 16
        for k in range(n):
            toms.add(bar, k * 4 / n, 60 if k % 2 == 0 else 64, 4 / n, 92 + (i - 4) * 6 + k)
    elif i == 7:
        for b, q, v in [(0, 60, 124), (0.75, 64, 80)]:
            toms.add(bar, b, q, 0.5, v)
        for k in range(8):
            toms.add(bar, 2 + k * 0.25, 64 if k % 2 else 60, 0.25, 92 + k * 4)
    else:
        for b, q, v in [(0, 60, 127), (0.75, 64, 72), (1.5, 60, 96), (2, 67, 116), (3, 60, 100), (3.5, 64, 88)]:
            toms.add(bar, b, q, 0.5, v)
    # ティンパニ
    if i == 0 or (sec in (1, 4) and i % 2 == 0):
        timp.add(bar, 0, TIMP[ch], 1, 108 if i == 0 else 92, "Timpani")
    if sec == 3 and i == 7:
        for k in range(8):
            timp.add(bar, 2 + k * 0.25, "B2", 0.25, 80 + k * 6, "Timpani")
    # シンバル
    if i == 0 and sec in (1, 2, 4):
        piatti.add(bar, 0, 62, 4, 114)
    # ハープ：各部の終わりに陰音階の駆け上がり、B は低い分散和音
    if i == 7:
        for k, q in enumerate(RUN):
            harp.add(bar, 2 + k * 0.25, q, 0.25, 84 + k * 4)
    if sec == 1:
        for k, q in enumerate((PAD[ch].split() + [PAD[ch].split()[0]]) * 2):
            harp.add(bar, k * 0.5, q, 0.5, 58)

LEVELS = {  # 部ごとの (木管, 金管, 弦の伸ばし, スピッカート)
    0: (70, 78, 66, 94), 1: (80, 90, 92, 92), 2: (88, 100, 70, 92), 3: (60, 84, 46, 86), 4: (100, 110, 104, 104)}
for sec, (w, b, s, sp) in LEVELS.items():
    bar = sec * 8 + 1
    for part, v in ((woods, w), (brass, b), (strings, s), (spic, sp)):
        part.dyn(bar, 0, v)
        part.dyn(bar + 7, 3.5, v)
strings.dyn(25, 0, 44); strings.dyn(32, 3.5, 112)
spic.dyn(29, 0, 84); spic.dyn(32, 3.5, 114)
brass.dyn(31, 0, 64); brass.dyn(32, 3.5, 118)

song = mz.call("get_song")
tid = {t["name"]: t["track_id"] for t in song["tracks"]}
mz.call("set_song", {"name": "SaiNoMichi Elite", "tempo": 138, "key": "E minor", "mood": "Menace", "reverb_room": "Hall"})
for t in song["tracks"]:
    for c in t.get("clips", []):
        mz.call("delete_clip", {"clip_id": c["clip_id"]})

for name, part, hum in [("woods", woods, 4), ("brass", brass, 4), ("strings", strings, 3), ("spiccato", spic, 6),
                        ("harp", harp, 5), ("toms", toms, 5), ("perc", timp, 3), ("piatti", piatti, 2)]:
    cid, n = write(tid[name], part, BARS, name, hum, seed=len(name) + 20)
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
