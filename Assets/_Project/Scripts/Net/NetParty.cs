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
    /// 通信の協力「賽の旅団」（Photon Fusion 2 の Shared Mode）。
    /// 部屋の入り方は対戦（NetRace）と同じ。始まったら、仲間の知らせ（PartyCommand）を部屋の代表に送り、
    /// 代表が通し番号を付けて全員に配る（代表が1人で順番を決めるので、全員が同じ順で受け取る）。
    /// </summary>
    public class NetParty : MonoBehaviour, IOnlineParty, INetworkRunnerCallbacks
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register()
        {
            OnlineParty.Factory = (code, name, kind) => Create(code, name, kind);
        }

        NetworkRunner runner;
        readonly Dictionary<PlayerRef, string> names = new Dictionary<PlayerRef, string>();
        readonly HashSet<PlayerRef> left = new HashSet<PlayerRef>();
        readonly List<int> order = new List<int>();   // 番号の順に並べた PlayerId（始まったときに代表が決める）
        int master = -1;                              // 部屋の代表の PlayerId（知らせの順番を決める人）
        int nextSeq;        // 代表だけ：次に付ける通し番号
        int expectedSeq;    // 次に受け取るはずの通し番号
        readonly SortedDictionary<int, PartyCommand> early = new SortedDictionary<int, PartyCommand>(); // 順番より先に届いた知らせ

        public string RoomCode { get; private set; }
        public string LocalName { get; private set; }
        public int Seed { get; private set; }
        public string Status { get; private set; } = "つないでいます……";
        public bool IsConnected => runner != null && runner.IsRunning;
        public bool IsHost => IsConnected && runner.IsSharedModeMasterClient;
        public bool Started { get; private set; }
        public bool Quick { get; private set; }
        /// <summary>遊び方（"party"：協力・"tour"：国めぐり）。部屋は遊び方ごとに別。</summary>
        public string Kind { get; private set; } = "party";

        public IReadOnlyList<string> Members => names.Where(kv => !left.Contains(kv.Key)).OrderBy(kv => kv.Key.PlayerId).Select(kv => kv.Value).ToList();

        public int LocalSlot => runner != null ? Math.Max(0, order.IndexOf(runner.LocalPlayer.PlayerId)) : 0;
        public IReadOnlyList<string> Names => order.Select(NameOf).ToList();

        string NameOf(int playerId) => names.Where(kv => kv.Key.PlayerId == playerId).Select(kv => kv.Value).FirstOrDefault() ?? $"旅人{playerId}";
        public int LeaderSlot => Math.Max(0, order.IndexOf(master));

        public event Action LobbyChanged;
        public event Action PartyStarted;
        public event Action<PartyCommand> CommandReceived;
        public event Action<int> MemberLeft;

        static NetParty Create(string code, string name, string kind)
        {
            var go = new GameObject($"NetParty {code ?? "quick"} {name}");
            DontDestroyOnLoad(go);
            var party = go.AddComponent<NetParty>();
            party.RoomCode = code;
            party.LocalName = name;
            party.Quick = code == null;
            party.Kind = string.IsNullOrEmpty(kind) ? "party" : kind;
            if (code != null) party.Seed = OnlineParty.SeedFromCode(code, party.Kind);
            party.Connect();
            return party;
        }

        async void Connect()
        {
            runner = gameObject.AddComponent<NetworkRunner>();
            runner.ProvideInput = false;
            runner.AddCallbacks(this);
            // TODO(仕様): 日本向けなので jp に固定（対戦と同じ）
            var appSettings = Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.GetCopy();
            appSettings.FixedRegion = "jp";
            var result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = Quick ? null : $"sainomichi-{Kind}-" + RoomCode,
                CustomLobbyName = Quick ? $"sainomichi-{Kind}-quick" : null,
                PlayerCount = OnlineParty.MaxPlayers,
                CustomPhotonAppSettings = appSettings,
            });
            if (this == null) return;
            if (!result.Ok)
            {
                Status = $"つながりませんでした（{result.ShutdownReason}）";
                LobbyChanged?.Invoke();
                return;
            }
            if (Quick && runner.SessionInfo != null)
            {
                RoomCode = "だれかと";
                Seed = OnlineParty.SeedFromCode(runner.SessionInfo.Name, Kind);
            }
            names[runner.LocalPlayer] = LocalName;
            Status = "つながりました";
            PartyRpc.RPC_PartyHello(runner, LocalName);
            LobbyChanged?.Invoke();
        }

        // ---- 部屋 ----

        public void StartParty()
        {
            if (!IsHost || Started) return;
            var members = names.Keys.Where(p => !left.Contains(p)).OrderBy(p => p.PlayerId).ToList();
            string list = string.Join(",", members.Select(p => p.PlayerId));
            PartyRpc.RPC_PartyStart(runner, Seed, list);
            OnStart(runner.LocalPlayer, list); // static RPC は送った本人には届かない
        }

        internal void OnHello(PlayerRef from, string name)
        {
            bool isNew = !names.ContainsKey(from);
            names[from] = string.IsNullOrEmpty(name) ? $"旅人{from.PlayerId}" : name;
            left.Remove(from);
            if (isNew && from != runner.LocalPlayer) PartyRpc.RPC_PartyHello(runner, LocalName);
            LobbyChanged?.Invoke();
        }

        internal void OnStart(PlayerRef from, string list)
        {
            if (Started) return;
            Started = true;
            master = from.PlayerId;
            order.Clear();
            foreach (var id in list.Split(','))
            {
                if (int.TryParse(id, out int playerId)) order.Add(playerId);
            }
            if (runner.IsSharedModeMasterClient && runner.SessionInfo != null) runner.SessionInfo.IsOpen = false;
            PartyStarted?.Invoke();
        }

        // ---- 知らせ ----

        public void Send(PartyCommandType type, int a = 0, int b = 0, string text = null)
        {
            if (!IsConnected || !Started) return;
            text = RaceProgress.Clip(text ?? "", 300);
            if (runner.LocalPlayer.PlayerId == master) Stamp(LocalSlot, type, a, b, text);
            else PartyRpc.RPC_PartyPropose(runner, (int)type, a, b, text); // 代表に送る（ほかの人は無視する）
        }

        /// <summary>代表から届いた案（代表だけが番号を付けて配る）。</summary>
        internal void OnPropose(PlayerRef from, int type, int a, int b, string text)
        {
            if (runner.LocalPlayer.PlayerId != master) return;
            int slot = order.IndexOf(from.PlayerId);
            if (slot < 0) return;
            Stamp(slot, (PartyCommandType)type, a, b, text);
        }

        void Stamp(int slot, PartyCommandType type, int a, int b, string text)
        {
            int seq = nextSeq++;
            PartyRpc.RPC_PartyCommand(runner, seq, slot, (int)type, a, b, text);
            OnCommand(new PartyCommand { seq = seq, slot = slot, type = type, a = a, b = b, text = text }); // 代表は自分で受け取る
        }

        /// <summary>通し番号の順に受け取る（順番より先に来たものは、前のものが届くまで待たせる）。</summary>
        internal void OnCommand(PartyCommand c)
        {
            if (c.seq < expectedSeq) return;
            early[c.seq] = c;
            while (early.TryGetValue(expectedSeq, out var next))
            {
                early.Remove(expectedSeq);
                expectedSeq++;
                // 受け取った側（画面）で例外が出ても、あとの知らせは止めない
                try { CommandReceived?.Invoke(next); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        public void Leave()
        {
            if (runner != null && runner.IsRunning) runner.Shutdown();
            Destroy(gameObject);
        }

        // ---- Fusion のコールバック ----

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) => LobbyChanged?.Invoke();

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            left.Add(player);
            int slot = order.IndexOf(player.PlayerId);
            if (Started && slot >= 0) MemberLeft?.Invoke(slot);
            // TODO(仕様): 代表が抜けたら、知らせの順番を決める人がいなくなる。いまは旅を続けられない（切れた扱い）
            if (Started && player.PlayerId == master) Status = "部屋の代表が抜けました";
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

    /// <summary>協力の RPC（static RPC。送った本人には届かない）。</summary>
    public class PartyRpc : SimulationBehaviour
    {
        static NetParty PartyOf(NetworkRunner runner) => runner != null ? runner.GetComponent<NetParty>() : null;

        static PlayerRef SourceOf(NetworkRunner runner, RpcInfo info) => info.Source.IsNone ? runner.LocalPlayer : info.Source;

        [Rpc]
        public static void RPC_PartyHello(NetworkRunner runner, string name, RpcInfo info = default)
        {
            PartyOf(runner)?.OnHello(SourceOf(runner, info), name);
        }

        [Rpc]
        public static void RPC_PartyStart(NetworkRunner runner, int seed, string order, RpcInfo info = default)
        {
            PartyOf(runner)?.OnStart(SourceOf(runner, info), order);
        }

        [Rpc]
        public static void RPC_PartyPropose(NetworkRunner runner, int type, int a, int b, string text, RpcInfo info = default)
        {
            PartyOf(runner)?.OnPropose(SourceOf(runner, info), type, a, b, text);
        }

        [Rpc]
        public static void RPC_PartyCommand(NetworkRunner runner, int seq, int slot, int type, int a, int b, string text, RpcInfo info = default)
        {
            PartyOf(runner)?.OnCommand(new PartyCommand { seq = seq, slot = slot, type = (PartyCommandType)type, a = a, b = b, text = text });
        }
    }
}
