# BGM の用意

ゲームは BGM を流す準備ができています。下のファイル名で `Assets/_Project/Audio/BGM` に入れ、Claude に「反映して」と頼めば鳴るようになります（**SaiNoMichi → Update Art** で取り込みます）。ないファイルの場面は無音のままです。

## 必要な曲

| ファイル名 | 場面 | 雰囲気 |
| --- | --- | --- |
| `bgm_title.mp3` | 始めの画面・設定 | 夕暮れの街道。旅立ちの前の静けさ |
| `bgm_map.mp3` | マップ（すごろく） | のんびりした旅。何度も聞いても疲れない |
| `bgm_battle.mp3` | 通常の戦闘・エリート | 軽快で勢いがある |
| `bgm_elite.mp3` | エリート戦（なくてもよい。ないときは通常の戦闘の曲） | 通常戦より強敵らしく |
| `bgm_boss.mp3` | ボス戦 | 重く、緊張感がある |
| `bgm_reward.mp3` | 戦闘に勝って報酬を選ぶ画面（ボスレリックを選ぶ画面も） | 勝利の余韻。明るく落ち着いた、短めでもよい |

あると層ごとに曲が変わるもの（なくてもよい。ないときは上の曲）：

| ファイル名 | 場面 |
| --- | --- |
| `bgm_map_2.mp3` / `bgm_battle_2.mp3` | 第2層「鍾乳洞」のマップ／戦闘 |
| `bgm_map_3.mp3` / `bgm_battle_3.mp3` | 第3層「鬼の城」のマップ／戦闘 |

## 曲の条件

- 形式は mp3 か ogg。**くり返して自然につながる曲**（ループ素材）がよい。
- 長さは 1〜3 分くらい。長すぎると WebGL の容量が増える（取り込み時に品質 50% で圧縮します）。
- 音量は曲どうしでそろっているとよい（ゲームの設定で全体の大きさは変えられます）。

## 入手の方法

### フリー素材を使う

和風の BGM が多いサイトの例です。**利用規約（クレジット表記の要不要、改変・ゲームへの組み込みの可否）は必ず各サイトで確認してください。** クレジットが必要なら、unityroom のゲーム説明欄に書きます。

- 魔王魂（和風・戦闘曲が多い）
- DOVA-SYNDROME（曲数が多く、ループ版がある曲も多い）
- 甘茶の音楽工房

### AI で作曲する（Suno・Udio など）

下の文言をそれぞれ貼り付けて作れます。**無料プランでは作った曲をゲームに使えない（商用・公開での利用が不可）サービスがあるので、規約を確認してください。** ループのつなぎ目が不自然なときは、曲の終わりを少し切ると自然になることがあります。

**bgm_title**
```
Japanese traditional instrumental, koto and shakuhachi, calm and nostalgic, sunset on an old highway, slow tempo 70 BPM, gentle taiko accents, no vocals, seamless loop, video game title screen music
```

**bgm_map**
```
Japanese folk instrumental, shamisen and shakuhachi with light percussion, relaxed traveling mood, walking on a countryside road, medium-slow tempo 90 BPM, cheerful but calm, no vocals, seamless loop, video game map music, not repetitive or tiring
```

**bgm_battle**
```
Japanese traditional battle music, fast shamisen riffs, taiko drums, shakuhachi melody, energetic and tense, 140 BPM, no vocals, seamless loop, roguelike video game battle theme
```

**bgm_boss**
```
Epic Japanese boss battle music, heavy taiko drums, dramatic shamisen and koto, ominous shakuhachi, dark and powerful, oni demon fight, 150 BPM, no vocals, seamless loop, video game boss theme
```

**bgm_map_2（鍾乳洞）／bgm_battle_2**
```
Japanese ambient instrumental, mysterious limestone cave, dripping water, echoing koto and low flute, cold and eerie, slow tempo, no vocals, seamless loop, video game exploration music
```
```
Japanese battle music in a dark cave, tense taiko and shamisen, echoing reverb, mysterious and fast, 140 BPM, no vocals, seamless loop, video game battle theme
```

**bgm_map_3（鬼の城）／bgm_battle_3**
```
Japanese dark instrumental, ominous oni castle at night, red moon, low taiko heartbeat, dissonant shamisen, tense and foreboding, slow tempo, no vocals, seamless loop, video game map music
```
```
Intense Japanese battle music inside a demon castle, aggressive taiko, distorted shamisen, wailing shakuhachi, dark and fast, 150 BPM, no vocals, seamless loop, video game battle theme
```

**bgm_reward**
```
Japanese traditional victory music, bright koto and shamisen, triumphant but calm, short fanfare feel then relaxed, celebrating after a battle, 100 BPM, no vocals, seamless loop, video game victory and reward screen music
```
