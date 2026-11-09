"""ボスの BGM「神前の大一番」：C の陰音階（C Db F G Ab）の旋律に短調の和音。160 BPM、本編 48 小節をくり返す。
低い金管が主題を吹き、弦が高く応え、終わりに全員で主題をもう一度。大太鼓とバスドラムで重く。"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mz
from musiclib import Part, write, PRE, POST, p

sys.stdout.reconfigure(encoding="utf-8")
BARS = 48
CHORDS = ("Cm DbC Cm DbC Cm DbC Ab G "     # A：刻みと低い打ち込み
          "Cm DbC Cm DbC Ab Fm G G "       # B：主題（低い金管）
          "Fm G Cm Cm Ab Db G G "          # C：弦が高く応える・木管の動き
          "Ab Db Fm G Ab Db G G "          # D：主題を高く（木管と弦）
          "Cm Cm Db Db Cm Cm G G "         # E：落として、ためる
          "Cm DbC Cm DbC Ab Fm G G").split()  # F：主題（全員）
assert len(CHORDS) == BARS

OSTINATO = {
    "Cm": "C2 C2 C3 C2 Db3 C2 C3 G2",
    "DbC": "C2 C2 Db3 C2 F3 C2 Db3 Ab2",
    "Ab": "Ab1 Ab1 Ab2 Ab1 C3 Ab1 Ab2 Eb2",
    "Fm": "F1 F1 F2 F1 Ab2 F1 F2 C2",
    "G": "G1 G1 G2 G1 Ab2 G1 G2 D2",
    "Db": "Db2 Db2 Db3 Db2 F3 Db2 Db3 Ab2",
}
ROOT = {"Cm": "C2", "DbC": "C2", "Ab": "Ab1", "Fm": "F1", "G": "G1", "Db": "Db2"}
PAD = {"Cm": "C3 Eb3 G3", "DbC": "Db3 F3 Ab3", "Ab": "C3 Eb3 Ab3", "Fm": "C3 F3 Ab3", "G": "D3 G3 B3", "Db": "Db3 F3 Ab3"}
STAB = {"Cm": "C2 G2 C3", "DbC": "Db2 Ab2 Db3", "Ab": "Ab1 Eb2 Ab2", "Fm": "F1 C2 F2", "G": "G1 D2 G2", "Db": "Db2 Ab2 Db3"}
ARP = {"Cm": "G5 C6 Eb6 G6", "DbC": "Ab5 Db6 F6 Ab6", "Ab": "Ab5 C6 Eb6 Ab6", "Fm": "F5 Ab5 C6 F6", "G": "G5 B5 D6 G6", "Db": "Ab5 Db6 F6 Ab6"}  # 木管の上限 B6 まで
TIMP = {"Cm": "C2", "DbC": "C2", "Ab": "Ab2", "Fm": "F2", "G": "G2", "Db": "Db2"}

THEME = [
    [("C4", 1.5), ("Db4", 0.5), ("C4", 1), ("G3", 1)],
    [("Ab3", 2), ("Db4", 1), ("F4", 1)],
    [("G4", 2), ("Ab4", 1), ("G4", 1)],
    [("F4", 1.5), ("Db4", 0.5), ("C4", 2)],
    [("C4", 1), ("Db4", 1), ("F4", 2)],
    [("Ab4", 2), ("G4", 1), ("F4", 1)],
    [("G4", 3), ("Ab4", 1)],
    [("G4", 4)],
]
ANSWER = [  # C：弦が高く応える
    [("C5", 2), ("Db5", 1), ("C5", 1)],
    [("G5", 3), ("F5", 1)],
    [("G5", 1), ("Ab5", 1), ("G5", 1), ("F5", 1)],
    [("Eb5", 2), ("C5", 2)],
    [("C5", 1), ("Eb5", 1), ("Ab5", 2)],
    [("F5", 1), ("Ab5", 1), ("Db6", 2)],
    [("C6", 2), ("B5", 2)],
    [("G5", 4)],
]
RUN = "C4 Db4 F4 G4 Ab4 C5 Db5 F5".split()
GLOOM = "C3 Db3 G3 Ab3 G3 Db3 C3 G2".split()  # E のハープ（低く不穏に）


def shift(items, semis):
    return [((p(q) + semis) if q is not None else None, l) for q, l in items]


woods, brass, strings, spic, harp, toms, timp, piatti, kit = (Part() for _ in range(9))

for bar in range(1, BARS + 1):
    ch = CHORDS[bar - 1]
    sec = (bar - 1) // 8   # 0:A 1:B 2:C 3:D 4:E 5:F
    i = (bar - 1) % 8
    # 低い弦の刻み
    if sec == 4:
        r = p(ROOT[ch]) + 12
        n = 8 if i < 6 else 16
        for k in range(n):
            spic.add(bar, k * 4 / n, r, 4 / n * 0.9, 84 + max(0, i - 4) * 8 + k, "Spiccato")
    else:
        for k, q in enumerate(OSTINATO[ch].split()):
            v = (116 if k in (0, 3, 6) else 90) + (6 if sec == 5 else 0)
            spic.add(bar, k * 0.5, q, 0.45, v, "Spiccato")
            if sec == 5:
                spic.add(bar, k * 0.5, p(q) + 12, 0.45, v - 10, "Spiccato")
    # 弦：和音の伸ばし
    if sec in (0, 4):
        strings.seq(bar, [([ROOT[ch], p(ROOT[ch]) + 12], 4)], 80)
    else:
        strings.seq(bar, [([ROOT[ch]] + PAD[ch].split(), 4)], 84)
    # 主題と応え
    if sec == 1:
        brass.seq(bar, THEME[i], 104)
    elif sec == 2:
        strings.seq(bar, ANSWER[i], 100)
        for k, q in enumerate((lambda t: [t[0], t[1], t[2], t[3], t[2], t[1], t[0], t[1]])(ARP[ch].split())):
            woods.add(bar, k * 0.5, q, 0.3, 92 if k % 2 == 0 else 78, "Staccatissimo")
    elif sec == 3:
        woods.seq(bar, shift(THEME[i], 12), 100)
        strings.seq(bar, shift(THEME[i], 12), 96)
        brass.seq(bar, [(PAD[ch].split(), 4)], 70)  # 主題を隠さないよう控えめ
    elif sec == 5:
        brass.seq(bar, THEME[i], 110)
        strings.seq(bar, shift(THEME[i], 12), 104)
        woods.seq(bar, shift(THEME[i], 24), 98)
    # 金管の低い打ち込み（A）
    if sec == 0:
        for b in (0, 1.5, 2.5):
            for q in STAB[ch].split():
                brass.add(bar, b, q, 0.3, 90 if b == 0 else 78, "Staccatissimo")
    if sec == 4 and i >= 6:
        brass.seq(bar, [("G1 D2 G2 B2".split(), 4)], 100)
    # 太鼓
    if sec == 4 and i < 4:
        toms.add(bar, 0, 60, 2, 120)
        toms.add(bar, 2, 67, 1, 100)
    elif sec == 4:
        n = 8 if i < 6 else 16
        for k in range(n):
            toms.add(bar, k * 4 / n, 60 if k % 2 == 0 else 64, 4 / n, 92 + max(0, i - 4) * 6 + k)
    elif i == 7:
        for b, q, v in [(0, 60, 124), (0.5, 64, 84), (1, 60, 104), (1.5, 64, 90)]:
            toms.add(bar, b, q, 0.5, v)
        for k in range(8):
            toms.add(bar, 2 + k * 0.25, 64 if k % 2 else 60, 0.25, 94 + k * 4)
    else:
        for b, q, v in [(0, 60, 127), (0.5, 64, 76), (1, 60, 100), (1.75, 64, 84), (2, 67, 118), (2.75, 64, 80), (3, 60, 104), (3.5, 64, 90)]:
            toms.add(bar, b, q, 0.5, v)
    # バスドラム
    if sec in (0, 1, 5) or (sec == 4 and i < 6):
        kit.add(bar, 0, 36, 1, 120 if i == 0 else 104, "Percussion")
    if sec == 5:
        kit.add(bar, 2, 36, 1, 100, "Percussion")
    # ティンパニ
    if i == 0 or (sec in (1, 5) and i % 2 == 0) or (sec == 3 and i % 4 == 0):
        timp.add(bar, 0, TIMP[ch], 1, 110 if i == 0 else 94, "Timpani")
    if sec == 4 and i == 7:
        for k in range(8):
            timp.add(bar, 2 + k * 0.25, "G2", 0.25, 80 + k * 6, "Timpani")
    # シンバル
    if i == 0 and sec in (1, 2, 3, 5):
        piatti.add(bar, 0, 62, 4, 116)
    if sec == 5 and i == 4:
        piatti.add(bar, 0, 62, 4, 104)
    # ハープ：各部の終わりに駆け上がり、D は分散和音、E は低く不穏に
    if i == 7 and sec != 4:
        for k, q in enumerate(RUN):
            harp.add(bar, 2 + k * 0.25, q, 0.25, 84 + k * 4)
    if sec == 3:
        for k, q in enumerate((PAD[ch].split() + [PAD[ch].split()[1]]) * 2):
            harp.add(bar, k * 0.5, p(q) + 12, 0.5, 66)
    if sec == 4 and i < 6:
        for k, q in enumerate(GLOOM):
            harp.add(bar, k * 0.5, q, 0.5, 70 if k == 0 else 58)

LEVELS = {  # 部ごとの (木管, 金管, 弦の伸ばし, スピッカート)
    0: (70, 90, 66, 96), 1: (78, 104, 84, 94), 2: (92, 88, 98, 94),
    3: (100, 66, 100, 96), 4: (60, 84, 46, 86), 5: (108, 112, 108, 106)}
for sec, (w, b, s, sp) in LEVELS.items():
    bar = sec * 8 + 1
    for part, v in ((woods, w), (brass, b), (strings, s), (spic, sp)):
        part.dyn(bar, 0, v)
        part.dyn(bar + 7, 3.5, v)
strings.dyn(33, 0, 44); strings.dyn(40, 3.5, 112)
spic.dyn(37, 0, 84); spic.dyn(40, 3.5, 114)
brass.dyn(39, 0, 64); brass.dyn(40, 3.5, 118)

song = mz.call("get_song")
tid = {t["name"]: t["track_id"] for t in song["tracks"]}
mz.call("set_song", {"name": "SaiNoMichi Boss", "tempo": 160, "key": "C minor", "mood": "Boss", "reverb_room": "Hall"})
for t in song["tracks"]:
    for c in t.get("clips", []):
        mz.call("delete_clip", {"clip_id": c["clip_id"]})

for name, part, hum in [("woods", woods, 4), ("brass", brass, 4), ("strings", strings, 3), ("spiccato", spic, 6),
                        ("harp", harp, 5), ("toms", toms, 5), ("perc", timp, 3), ("piatti", piatti, 2), ("kit", kit, 3)]:
    cid, n = write(tid[name], part, BARS, name, hum, seed=len(name) + 30)
    print(name, cid, n)

mz.call("set_track", {"track_id": tid["spiccato"], "articulation": "Spiccato"})
mz.call("set_track", {"track_id": tid["perc"], "articulation": "Timpani"})
mz.call("set_track", {"track_id": tid["kit"], "articulation": "Percussion"})
mz.call("set_sections", {"sections": [{"name": "Loop pre", "start_bar": 1, "bars": PRE}] +
                         [{"name": n, "start_bar": PRE + 1 + k * 8, "bars": 8} for k, n in enumerate("ABCDEF")] +
                         [{"name": "Loop post", "start_bar": PRE + BARS + 1, "bars": POST}]})
print(mz.call("save_song"))
