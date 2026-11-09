"""新しい曲を作り、楽器のトラックを足して、各楽器の音域・奏法・鍵盤の割り当てを instruments_<name>.json に保存する。
py -3 setup_tracks.py SONGNAME key1 key2 ..."""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mz

sys.stdout.reconfigure(encoding="utf-8")
IDS = {
    "strings": "83b8481cfbff4bbbb04d37599d33065f",
    "strings_lo": "83b8481cfbff4bbbb04d37599d33065f",
    "spiccato": "83b8481cfbff4bbbb04d37599d33065f",
    "pizz": "83b8481cfbff4bbbb04d37599d33065f",
    "woods": "07f7c97b897a428b87d3f32758c4aa3a",
    "harp": "50f950950abc4251be89e5574777f16b",
    "toms": "60cba4b01f7248f9a4f9ad8f241f4b20",
    "perc": "c55e8eb2480b467888dd27a4edbe0f71",
    "brass": "b79a6f97610e4131a5686bb9908fd743",
    "tongue": "53ec970c30734c5c9539117189e0c2f4",
    "glock": "7d54331982354b88aa6559273bc47330",
    "piatti": "dfebe0a9ec3a4ebbbe90fc5035beb699",
    "bells": "015c6cccb47a4d0cbcf10320748fdafa",
}
song = sys.argv[1]
keys = sys.argv[2:]
here = os.path.dirname(os.path.abspath(__file__))
print(mz.call("new_song"))
tracks = {}
for k in keys:
    r = mz.call("add_track", {"instrument_id": IDS[k], "name": k})
    tid = r["track_id"] if isinstance(r, dict) else str(r)
    tracks[k] = tid
print(json.dumps(tracks))
info = {}
for k, tid in tracks.items():
    info[k] = mz.call("get_instrument", {"track_id": tid})
json.dump({"tracks": tracks, "info": info}, open(os.path.join(here, f"instruments_{song}.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(mz.call("save_song", {"as_name": song}))
