# Musio Studio で BGM を作る道具

マップ（`bgm_map.wav`）と戦闘（`bgm_battle.wav`）の BGM は、Musio Studio のエージェント機能（MCP。`127.0.0.1:47820/mcp`）に、ここのスクリプトから音符を書き込んで作った。

## 曲

| 曲 | ファイル | 中身 |
| --- | --- | --- |
| マップ「旅路」 | `compose_map.py` | D の陽音階（D E G A B）。92 BPM、本編 32 小節（約 83 秒）。ハープを琴のように刻み、木管が笛のように旋律を吹く |
| 戦闘「賽の勝負」 | `compose_battle.py` | D の陰音階（D Eb G A Bb）の旋律に短調の和音。152 BPM、本編 40 小節（約 63 秒）。弦のスピッカート・大太鼓・金管 |

Musio の手持ちの音源に和楽器はないので、ハープ・木管・Toms（大太鼓）で和の雰囲気を出している。

## 作り直し方

1. Musio Studio を起動する（設定の「エージェント」がオン。トークンは `%APPDATA%\Musio Studio\agent-token` から読む。リポジトリには入れない）。
2. `py -3 setup_tracks.py "SaiNoMichi Map" woods strings pizz harp toms perc glock tongue`（戦闘は `"SaiNoMichi Battle" woods brass strings spiccato harp toms perc piatti`）で曲とトラックを作る。作った曲は Musio Studio の Songs に残っているので、2回目からは `py -3 mz.py call open_song '{"song": "SaiNoMichi Map"}'` で開けばよい。
3. `py -3 compose_map.py`（または `compose_battle.py`）で音符を書き込む（前の音は消してから書く）。
4. Musio Studio で聴いて、音量を整え、WAV で書き出す（`py -3 mz.py call export_song '{"format": "wav", "name": "..."}'`）。
5. `py -3 loopcut.py 書き出し.wav bgm_map.wav 92 4 32`（戦闘は `152 4 40`）でループを切り出し、`Assets/_Project/Audio/BGM/` に置いて SaiNoMichi → Update Art。

## ループの作り方

曲は「本編の最後の4小節の写し・本編・本編の最初の4小節の写し」の順に書いてある（`musiclib.py` の `PRE` / `POST`）。書き出したあと本編だけを切り出すと、頭には最後の小節の残響が入り、終わりの先は頭と同じ音になるので、くり返しても途切れない。継ぎ目は 50ms だけ重ねてつなぐ。
