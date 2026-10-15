using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using Fusion.Sockets;
using SaiNoMichi.Multiplayer;
using UnityEngine;

namespace SaiNoMichi.Net
{
    /// <summary>
    /// 通信の対戦「賽レース」（Photon Fusion 2 の Shared Mode）。
    /// 合言葉ごとの部屋に入り、名前・開始の合図・進み具合だけを RPC で送り合う。ランの計算は各自の端末で行う。
    /// 盤面のシードは合言葉から決まる（OnlineRace.SeedFromCode）ので、同じ部屋なら全員同じ盤面になる。
    /// </summary>
    public class NetRace : MonoBehaviour, IOnlineRace, INetworkRunnerCallbacks
    {
        /// <summary>起動時に、ゲーム本体へ「通信の対戦を作る方法」を知らせる。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register()
        {
            OnlineRace.Factory = (code, name) => Create(code, name);
        }

        NetworkRunner runner;
        readonly Dictionary<PlayerRef, string> names = new Dictionary<PlayerRef, string>();
        readonly Dictionary<PlayerRef, RaceProgress> progress = new Dictionary<PlayerRef, RaceProgress>();
        readonly HashSet<PlayerRef> left = new HashSet<PlayerRef>();

        public string RoomCode { get; private set; }
        public string LocalName { get; private set; }
        public int Seed { get; private set; }
        public string Status { get; private set; } = "つないでいます……";
        public bool IsConnected => runner != null && runner.IsRunning;
        public bool IsHost => IsConnected && runner.IsSharedModeMasterClient;
        public bool Started { get; private set; }

        public IReadOnlyList<string> Members => names.Where(kv => !left.Contains(kv.Key)).OrderBy(kv => kv.Key.PlayerId).Select(kv => kv.Value).ToList();

        public IReadOnlyList<RaceProgress> Rivals => progress
            .Where(kv => runner == null || kv.Key != runner.LocalPlayer)
            .Select(kv => kv.Value)
            .ToList();

        public event Action Changed;
        public event Action LobbyChanged;
        public event Action RaceStarted;

        static NetRace Create(string code, string name)
        {
            var go = new GameObject($"NetRace {code} {name}"); // 名前を人ごとに変える（同じエディタで2人分つなぐ試験のとき、Fusion が作る場面の名前がぶつからないように）
            DontDestroyOnLoad(go);
            var race = go.AddComponent<NetRace>();
            race.RoomCode = code;
            race.LocalName = name;
            race.Seed = OnlineRace.SeedFromCode(code);
            race.Connect();
            return race;
        }

        async void Connect()
        {
            runner = gameObject.AddComponent<NetworkRunner>();
            runner.ProvideInput = false;
            runner.AddCallbacks(this);
            // 地域を決めておく（決めないと、つなぐたびに近い地域を選び、同じ合言葉でも別の地域の部屋に入ってしまう）
            // TODO(仕様): 日本向けなので jp に固定
            var appSettings = Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.GetCopy();
            appSettings.FixedRegion = "jp";
            var result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = "sainomichi-race-" + RoomCode,
                PlayerCount = OnlineRace.MaxPlayers,
                CustomPhotonAppSettings = appSettings,
            });
            if (this == null) return; // つないでいる間に抜けた
            if (!result.Ok)
            {
                Status = $"つながりませんでした（{result.ShutdownReason}）";
                LobbyChanged?.Invoke();
                return;
            }
            names[runner.LocalPlayer] = LocalName;
            Status = "つながりました";
            RaceRpc.RPC_Hello(runner, LocalName);
            LobbyChanged?.Invoke();
        }

        // ---- 送る ----

        public void StartRace()
        {
            if (!IsHost || Started) return;
            RaceRpc.RPC_Start(runner, Seed);
            OnStart(); // static RPC は送った本人には届かないので、自分はここで始める
        }

        public void Report(RaceProgress local)
        {
            if (!IsConnected) return;
            local.name = LocalName;
            progress[runner.LocalPlayer] = local;
            RaceRpc.RPC_Progress(runner, local.layer, local.tileId, local.tilesToGoal, local.hp, local.maxHp, local.turn, (int)local.state);
        }

        public void Leave()
        {
            if (runner != null && runner.IsRunning) runner.Shutdown();
            Destroy(gameObject);
        }

        // ---- 受け取る（RaceRpc から） ----

        internal void OnHello(PlayerRef from, string name)
        {
            bool isNew = !names.ContainsKey(from);
            names[from] = string.IsNullOrEmpty(name) ? $"旅人{from.PlayerId}" : name;
            left.Remove(from);
            // あとから来た人にも自分の名前が届くように、知らない人から挨拶が来たら挨拶を返す
            if (isNew && from != runner.LocalPlayer) RaceRpc.RPC_Hello(runner, LocalName);
            LobbyChanged?.Invoke();
        }

        internal void OnStart()
        {
            if (Started) return;
            Started = true;
            RaceStarted?.Invoke();
        }

        internal void OnProgress(PlayerRef from, RaceProgress p)
        {
            if (from == runner.LocalPlayer) return;
            p.name = names.TryGetValue(from, out var n) ? n : $"旅人{from.PlayerId}";
            progress[from] = p;
            Changed?.Invoke();
        }

        // ---- Fusion のコールバック ----

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) => LobbyChanged?.Invoke();

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            left.Add(player);
            // レース中に抜けた人は、その場で倒れた扱いにする（順位の一覧に残す）
            if (progress.TryGetValue(player, out var p) && p.state == RaceState.Running)
            {
                p.state = RaceState.Dead;
                progress[player] = p;
                Changed?.Invoke();
            }
            LobbyChanged?.Invoke();
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Status = $"切れました（{shutdownReason}）";
            LobbyChanged?.Invoke();
        }

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            Status = $"切れました（{reason}）";
            LobbyChanged?.Invoke();
        }

        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            Status = $"つながりませんでした（{reason}）";
            LobbyChanged?.Invoke();
        }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
    }

    /// <summary>対戦の RPC（オブジェクトを置かずに送れる static RPC。Fusion の weaver が送受信のコードを足す）。</summary>
    public class RaceRpc : SimulationBehaviour
    {
        static NetRace RaceOf(NetworkRunner runner) => runner != null ? runner.GetComponent<NetRace>() : null;

        [Rpc]
        public static void RPC_Hello(NetworkRunner runner, string name, RpcInfo info = default)
        {
            RaceOf(runner)?.OnHello(info.Source.IsNone ? runner.LocalPlayer : info.Source, name);
        }

        [Rpc]
        public static void RPC_Start(NetworkRunner runner, int seed, RpcInfo info = default) // 引数なしの static RPC はサーバーに切られた（ServerLogic）ので、シードを付けて送る
        {
            RaceOf(runner)?.OnStart();
        }

        [Rpc]
        public static void RPC_Progress(NetworkRunner runner, int layer, int tileId, int tilesToGoal, int hp, int maxHp, int turn, int state, RpcInfo info = default)
        {
            RaceOf(runner)?.OnProgress(info.Source.IsNone ? runner.LocalPlayer : info.Source, new RaceProgress
            {
                layer = layer, tileId = tileId, tilesToGoal = tilesToGoal, hp = hp, maxHp = maxHp, turn = turn, state = (RaceState)state,
            });
        }
    }
}
