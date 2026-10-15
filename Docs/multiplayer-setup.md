# マルチプレイの準備（Photon Fusion 2）

オンラインの対戦は Photon Fusion 2（Shared Mode）で動きます。SDK と App ID は公開リポジトリに入れていないので、別の PC で開くときは次の準備が必要です。準備をしなくても、ゲーム本体と「AI と練習」は動きます（`SaiNoMichi.Net` だけがコンパイルできなくなります）。

## 1. App ID を用意する
1. [Photon のダッシュボード](https://dashboard.photonengine.com/) にログインする。
2. 「SaiNoMichi」（Fusion 2）の App ID を控える。なければ「Create a New Application」で、種類を Multiplayer Game・SDK を Fusion・バージョンを Fusion 2 にして作る。

## 2. SDK を入れる
1. [SDK のページ](https://doc.photonengine.com/fusion/v2/getting-started/sdk-download) から「Fusion SDK 2.1.x Stable」の .unitypackage をダウンロードする。
2. Unity の Assets → Import Package → Custom Package で取り込む（`Assets/Photon` に入る。git には入らない）。
3. Tools → Fusion → Fusion Hub を開き、App ID の欄に 1 の App ID を貼る（`Assets/Photon/Fusion/Resources/PhotonAppSettings.asset` に入る）。

## 3. 通信のコードを Fusion に知らせる
`Assets/Photon/Fusion/Resources/NetworkProjectConfig.fusion` の `AssembliesToWeave` に `"SaiNoMichi.Net"` を足す（RPC のコードを Fusion の weaver が作るため）。

```json
"AssembliesToWeave": [
    "Assembly-CSharp",
    "Assembly-CSharp-firstpass",
    "SaiNoMichi.Net"
],
```

## しくみ
- 通信のコードは `Assets/_Project/Scripts/Net/`（アセンブリ `SaiNoMichi.Net`）。ゲーム本体（`SaiNoMichi.Runtime`）は Fusion を知らず、`OnlineRace.Factory` を通して使う。
- 対戦「賽レース」：合言葉ごとの部屋（`sainomichi-race-合言葉`）に入り、名前・開始の合図・進み具合だけを static RPC で送り合う。盤面のシードは合言葉から決まる。
