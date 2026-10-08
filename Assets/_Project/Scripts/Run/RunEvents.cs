using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Board;
using SaiNoMichi.Core;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;

namespace SaiNoMichi.Run
{
    /// <summary>イベント（仕様書 第9章）。フェーズ1は5種。</summary>
    public enum EventKind
    {
        Gamble,          // 路地裏の賭場
        FallenDice,      // 落ちている賽
        OldShrine,       // 古びた祠
        FoxWedding,      // 狐の嫁入り
        IdatenFootprints // 韋駄天の足跡
    }

    /// <summary>イベントの数値（仕様書 第9章）。</summary>
    [Serializable]
    public class EventSettings
    {
        public List<EventKind> kinds = new List<EventKind>
        {
            EventKind.Gamble, EventKind.FallenDice, EventKind.OldShrine, EventKind.FoxWedding, EventKind.IdatenFootprints,
        };

        [UnityEngine.Header("路地裏の賭場")]
        public int gambleBet = 20;
        // TODO(仕様): 「当たれば40G」は賭け金込みの払い戻しと解釈（差し引き+20G）
        public int gamblePayout = 40;

        [UnityEngine.Header("落ちている賽")]
        public int fallenUncommonPercent = 50;

        [UnityEngine.Header("古びた祠")]
        public int shrineHpCost = 8;
        public int shrinePrayHeal = 5;

        [UnityEngine.Header("狐の嫁入り")]
        public int foxTurns = 3;
        public int foxMoveBonus = 2;
        // TODO(仕様): 「見送る：お守り1個」はお守りがフェーズ2なので、代わりにゴールド
        public int foxSeeOffGold = 15;

        [UnityEngine.Header("韋駄天の足跡")]
        public int idatenSteps = 3;
    }

    public struct GambleResult
    {
        public int value;
        public bool even;    // 振った出目が偶数（丁）か
        public bool win;
        public int payout;   // 当たりで得たゴールド
        public bool refreshed;
    }

    public partial class RunState
    {
        EventKind? lastEvent;

        /// <summary>
        /// イベントマスで起きるイベントを選ぶ（イベント用の乱数）。直前と同じイベントは続けない。
        /// TODO(仕様): 同じイベントが1ランに何度も出てよいかは未定。フェーズ1は「2回続けない」だけ
        /// </summary>
        public EventKind PickEvent()
        {
            var kinds = config.events.kinds.Distinct().ToList();
            if (kinds.Count > 1 && lastEvent.HasValue) kinds.Remove(lastEvent.Value);
            var kind = kinds[random.Event.Next(kinds.Count)];
            lastEvent = kind;
            return kind;
        }

        /// <summary>
        /// イベントの判定でダイスを1個振る。移動と同じく使用済みになり（最後の1個ならリフレッシュ）、振ったときの効果も働く。ターンは進まない。
        /// </summary>
        public int RollForEvent(DiceInstance die, out bool refreshed)
        {
            if (!pouch.All.Contains(die)) throw new ArgumentException("ポーチにないダイスです。", nameof(die));
            if (die.state != DiceState.Available) throw new InvalidOperationException($"使用可能でないダイスは使えません（{die.state}）。");

            int rolled = DiceRoller.Roll(die, random.Event, LastRolledValue, out int faceIndex);
            var ctx = effects.Fire(new EffectContext(Trigger.OnRoll) { run = this, player = player, dice = die, faceIndex = faceIndex, value = rolled },
                die, die.faces[faceIndex].engraving);
            refreshed = pouch.Use(die, ctx.keepAvailable);
            int value = Math.Max(0, ctx.value);
            LastRolledValue = value;
            return value;
        }

        // ---- 路地裏の賭場 ----

        public bool CanGamble => Gold >= config.events.gambleBet && pouch.AvailableCount > 0;

        /// <summary>賭け金を払い、丁（偶数）か半（奇数）を宣言してダイスを振る。0 は偶数。</summary>
        public GambleResult Gamble(DiceInstance die, bool betEven)
        {
            var s = config.events;
            if (!CanGamble) throw new InvalidOperationException("賭けられません。");
            SpendGold(s.gambleBet);
            var result = new GambleResult { value = RollForEvent(die, out bool refreshed), refreshed = refreshed };
            result.even = result.value % 2 == 0;
            result.win = result.even == betEven;
            if (result.win) result.payout = GainGold(s.gamblePayout);
            return result;
        }

        // ---- 落ちている賽 ----

        /// <summary>拾う：報酬に出るコモンのダイスから1つ（受け取るかは画面で。満杯なら入れ替え）。</summary>
        public DiceData FallenDiceCommon()
        {
            var commons = config.rewardDicePool.Where(d => d != null && d.rarity == Rarity.Common).ToList();
            return commons.Count > 0 ? commons[random.Event.Next(commons.Count)] : null;
        }

        /// <summary>
        /// よく調べる：50%でアンコモンのダイス（受け取るかは画面で）、50%で欠け賽（呪い。その場でポーチに入る）。
        /// 欠け賽のときは cursed が入り、ポーチが満杯なら入らない（cursed は null のまま、curseRejected が true）。
        /// </summary>
        public DiceData ExamineFallenDice(out DiceInstance cursed, out bool curseRejected)
        {
            cursed = null;
            curseRejected = false;
            if (random.Event.Next(100) < config.events.fallenUncommonPercent)
            {
                var uncommons = config.rewardDicePool.Where(d => d != null && d.rarity == Rarity.Uncommon).ToList();
                if (uncommons.Count > 0) return uncommons[random.Event.Next(uncommons.Count)];
            }
            var curse = config.curseDice;
            if (curse == null) return null;
            // TODO(仕様): ポーチが満杯のときは欠け賽が入らない（罠の呪いと同じ扱い）
            if (pouch.IsFull) curseRejected = true;
            else cursed = AddDice(curse);
            return null;
        }

        /// <summary>呪いのダイス（欠け賽）を押し付ける（敵の「呪い」など）。ポーチが満杯なら入らず null。</summary>
        public DiceInstance ForceCurse()
        {
            var curse = config.curseDice != null ? config.curseDice : config.curseDicePool.FirstOrDefault(d => d != null);
            if (curse == null || pouch.IsFull) return null;
            return AddDice(curse);
        }

        // ---- 古びた祠 ----

        /// <summary>祠で付けられる刻印（ランダム）。HP は付けたときに払う。</summary>
        public EngravingData ShrineEngraving()
        {
            var pool = config.engravingPool.Where(e => e != null).ToList();
            return pool.Count > 0 ? pool[random.Event.Next(pool.Count)] : null;
        }

        /// <summary>HP を払える（払っても倒れない）か。</summary>
        public bool CanPayShrine => player.hp > config.events.shrineHpCost;

        public void ShrineEngrave(DiceInstance die, int faceIndex, EngravingData engraving)
        {
            if (!CanPayShrine) throw new InvalidOperationException("HP が足りません。");
            ApplyEngraving(die, faceIndex, engraving);
            player.LoseHp(config.events.shrineHpCost);
        }

        /// <summary>お参り：HP を回復。実際に回復した量を返す。</summary>
        public int ShrinePray()
        {
            int before = player.hp;
            player.Heal(config.events.shrinePrayHeal);
            return player.hp - before;
        }

        // ---- 狐の嫁入り ----

        /// <summary>移動の出目に足す数が残っているターン数（狐の嫁入り）。</summary>
        public int MoveBonusTurns { get; private set; }
        public int MoveBonus { get; private set; }

        public void FollowFox()
        {
            MoveBonusTurns = config.events.foxTurns;
            MoveBonus = config.events.foxMoveBonus;
        }

        public int SeeOffFox() => GainGold(config.events.foxSeeOffGold);

        // ---- 韋駄天の足跡 ----

        /// <summary>ダイスを振らずに steps 歩進む移動を始める（韋駄天の足跡）。進み方は StepMove、終わりは FinishMove。</summary>
        public MoveInProgress BeginForcedMove(int steps)
        {
            return new MoveInProgress
            {
                dice = null,
                faceIndex = -1,
                value = steps,
                remaining = steps,
                from = Current,
                availableDiceBefore = new List<DiceInstance>(pouch.Available),
            };
        }
    }
}
