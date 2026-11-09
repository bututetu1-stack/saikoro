"""曲を書く道具：音の名前・前後の小節（ループ用）・Musio への書き込み。"""
import json, os, random, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mz

NOTE = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}
PRE = 4    # ループの前に置く小節（本編の最後の4小節の写し。残響を本編の頭に持ち込むため）
POST = 4   # ループのあとに置く小節（本編の最初の4小節の写し）


def p(name):
    """'D4' 'Bb3' 'F#5' → MIDI 番号（C4 = 60）。"""
    if isinstance(name, int):
        return name
    letter = name[0]
    rest = name[1:]
    acc = 0
    while rest and rest[0] in "#b":
        acc += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 12 * (int(rest) + 1) + NOTE[letter] + acc


class Part:
    """1つのトラックの本編の音（小節は 1 から、拍は小節の中で 0 から）。"""

    def __init__(self, beats_per_bar=4):
        self.notes = []  # (abs_beat, pitch, length, velocity, articulation)
        self.bpb = beats_per_bar
        self.cc = []     # (abs_beat, value) dynamics

    def add(self, bar, beat, pitch, length, vel=90, art=None):
        self.notes.append(((bar - 1) * self.bpb + beat, p(pitch), length, vel, art))

    def seq(self, bar, items, vel=90, art=None):
        """items: [(pitch or None, length), ...] を小節 bar の頭から順に置く（None は休み）。"""
        beat = 0.0
        for pitch, length in items:
            if pitch is not None:
                if isinstance(pitch, (list, tuple)):
                    for q in pitch:
                        self.add(bar, beat, q, length * 0.98, vel, art)
                else:
                    self.add(bar, beat, pitch, length * 0.98, vel, art)
            beat += length

    def dyn(self, bar, beat, value):
        self.cc.append(((bar - 1) * self.bpb + beat, value))


def layout(part, main_bars, humanize=0, seed=1):
    """本編を、前に最後の PRE 小節・後に最初の POST 小節を足した曲の拍に並べ直す。"""
    rng = random.Random(seed)
    bpb = part.bpb
    total = main_bars * bpb
    out = []

    def emit(beat, n):
        _, pitch, length, vel, art = n
        v = max(1, min(127, vel + (rng.randint(-humanize, humanize) if humanize else 0)))
        d = {"pitch": pitch, "start": round(beat, 4), "length": round(length, 4), "velocity": v}
        if art:
            d["articulation"] = art
        out.append(d)

    for n in part.notes:
        b = n[0]
        emit(b + PRE * bpb, n)                                   # 本編
        if b >= total - PRE * bpb:
            emit(b - (total - PRE * bpb), n)                     # 前（本編の最後の写し）
        if b < POST * bpb:
            emit(b + PRE * bpb + total, n)                       # 後（本編の最初の写し）
    cc = []
    for b, v in part.cc:
        cc.append((b + PRE * bpb, v))
        if b >= total - PRE * bpb:
            cc.append((b - (total - PRE * bpb), v))
        if b < POST * bpb:
            cc.append((b + PRE * bpb + total, v))
    cc.sort()
    return out, cc


def write(track_id, part, main_bars, name, humanize=4, seed=1):
    notes, cc = layout(part, main_bars, humanize, seed)
    bars = PRE + main_bars + POST
    clip = mz.call("add_clip", {"track_id": track_id, "start_bar": 1, "bars": bars, "name": name})
    cid = clip["clip_id"] if isinstance(clip, dict) else str(clip)
    for i in range(0, len(notes), 400):
        mz.call("write_notes", {"clip_id": cid, "notes": notes[i:i + 400], "replace": False})
    if cc:
        # 同じ拍の点は最後のものだけ残す
        pts = {}
        for b, v in cc:
            pts[round(b, 4)] = v
        mz.call("draw_controller", {"clip_id": cid, "controller": "dynamics",
                                    "points": [{"beat": b, "value": v} for b, v in sorted(pts.items())], "curve": True})
    return cid, len(notes)
