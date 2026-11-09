"""開いている曲の音量を決めて、部ごとに測る。py -3 mix.py '{"woods": [-2, 0.15, 0.3], ...}' START1,START2,...
値は [音量dB, 定位, 残響]。null はそのまま。"""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mz

sys.stdout.reconfigure(encoding="utf-8")
settings = json.loads(sys.argv[1])
starts = [int(x) for x in sys.argv[2].split(",")] if len(sys.argv) > 2 and sys.argv[2] else []
song = mz.call("get_song")
tid = {t["name"]: t["track_id"] for t in song["tracks"]}
for name, vals in settings.items():
    args = {"track_id": tid[name]}
    for key, v in zip(("volume_db", "pan", "space"), vals):
        if v is not None:
            args[key] = v
    mz.call("set_track", args)
for start in starts:
    r = mz.call("measure_mix", {"start_bar": start, "bars": 8})
    print("bars", start, {t["name"]: round(t["rms_db"], 1) for t in r["tracks_playing"]},
          {k: round(v, 1) for k, v in r["mix"].items()})
mz.call("save_song")
