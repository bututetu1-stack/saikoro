# Musio Studio で BGM を作る道具

マップ・戦闘・強敵・ボスの BGM（`bgm_map.wav`・`bgm_battle.wav`・`bgm_elite.wav`・`bgm_boss.wav`）は、Musio Studio のエージェント機能（MCP。`127.0.0.1:47820/mcp`）に、ここのスクリプトから音符を書き込んで作った。

## 曲

| 曲 | ファイル | 中身 |
| --- | --- | --- |
| マップ「旅路」 | `compose_map.py` | D の陽音階（D E G A B）。92 BPM、本編 32 小節（約 83 秒）。ハープを琴のように刻み、木管が笛のように旋律を吹く |
| 戦闘「賽の勝負」 | `compose_battle.py` | D の陰音階（D Eb G A Bb）の旋律に短調の和音。152 BPM、本編 40 小節（約 63 秒）。弦のスピッカート・大太鼓・金管 |
| 強敵「鬼気」 | `compose_elite.py` | E の陰音階（E F A B C）。138 BPM、本編 40 小節（約 70 秒）。低い弦の刻み・3+3+2 の金管の打ち込み・重い太鼓、旋律は弦がオクターブで歌う |
| ボス「神前の大一番」 | `compose_boss.py` | C の陰音階（C Db F G Ab）。160 BPM、本編 48 小節（72 秒）。低い金管の主題に弦が高く応え、最後に全員で主題。バスドラム（オーケストラ打楽器の 36 番）も足す |

Musio の手持ちの音源に和楽器はないので、ハープ・木管・Toms（大太鼓）で和の雰囲気を出している。

## 作り直し方

1. Musio Studio を起動する（設定の「エージェント」がオン。トークンは `%APPDATA%\Musio Studio\agent-token` から読む。リポジトリには入れない）。
2. `py -3 setup_tracks.py "SaiNoMichi Map" woods strings pizz harp toms perc glock tongue`（戦闘・強敵は `"SaiNoMichi Battle" woods brass strings spiccato harp toms perc piatti`、ボスはそれに `kit` を足す）で曲とトラックを作る。作った曲は Musio Studio の Songs に残っているので、2回目からは `py -3 mz.py call open_song '{"song": "SaiNoMichi Map"}'` で開けばよい。
3. `py -3 compose_map.py`（`compose_battle.py`・`compose_elite.py`・`compose_boss.py`）で音符を書き込む（前の音は消してから書く）。
4. Musio Studio で聴いて、音量を整え（`py -3 mix.py '{"woods": [-2, 0.15, 0.3]}' 5,13` で音量・定位・残響を決めて部ごとに測る）、WAV で書き出す（`py -3 mz.py call export_song '{"format": "wav", "name": "..."}'`）。
5. `py -3 loopcut.py 書き出し.wav bgm_map.wav 92 4 32`（戦闘 `152 4 40`、強敵 `138 4 40 4 1.0`、ボス `160 4 48 4 1.0`。最後の数は音量を足す dB）でループを切り出し、`Assets/_Project/Audio/BGM/` に置いて SaiNoMichi → Update Art。4曲とも約 −17 LUFS にそろえてある。

## ループの作り方

曲は「本編の最後の4小節の写し・本編・本編の最初の4小節の写し」の順に書いてある（`musiclib.py` の `PRE` / `POST`）。書き出したあと本編だけを切り出すと、頭には最後の小節の残響が入り、終わりの先は頭と同じ音になるので、くり返しても途切れない。継ぎ目は 50ms だけ重ねてつなぐ。
