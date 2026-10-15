using System;
using System.Collections.Generic;

namespace SaiNoMichi.Multiplayer
{
    /// <summary>
    /// 試験用：通信なしで、通信の部屋のふりをする（送った知らせは通し番号を付けて自分に返す）。
    /// 仲間の知らせは Inject で入れる。エディタで、通信の国めぐり・協力の流れを1人で試すときに使う。
    /// </summary>
    public class LoopbackParty : IOnlineParty
    {
        readonly List<string> names;
        int seq;

        public LoopbackParty(int seed, IReadOnlyList<string> names, int localSlot = 0)
        {
            Seed = seed;
            this.names = new List<string>(names);
            LocalSlot = localSlot;
        }

        public string RoomCode => "試験";
        public string Status => "試験の部屋";
        public bool IsConnected => true;
        public bool IsHost => LocalSlot == 0;
        public IReadOnlyList<string> Members => names;
        public event Action LobbyChanged { add { } remove { } }
        public event Action PartyStarted { add { } remove { } }
        public void StartParty() { }

        public int Seed { get; }
        public int LocalSlot { get; }
        public IReadOnlyList<string> Names => names;
        public int LeaderSlot => 0;
        public event Action<PartyCommand> CommandReceived;
        public event Action<int> MemberLeft;

        public void Send(PartyCommandType type, int a = 0, int b = 0, string text = null) => Inject(LocalSlot, type, a, b, text);

        /// <summary>slot の人が送ったことにして、知らせを届ける。</summary>
        public void Inject(int slot, PartyCommandType type, int a = 0, int b = 0, string text = null)
        {
            CommandReceived?.Invoke(new PartyCommand { seq = seq++, slot = slot, type = type, a = a, b = b, text = text });
        }

        /// <summary>slot の人が抜けたことにする。</summary>
        public void Leave(int slot) => MemberLeft?.Invoke(slot);

        public void Leave() { }
    }
}
