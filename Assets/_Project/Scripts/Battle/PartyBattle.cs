using System;
using System.Collections.Generic;
using System.Linq;
using SaiNoMichi.Dice;
using SaiNoMichi.Effects;

namespace SaiNoMichi.Battle
{
    /// <summary>協力の戦闘に加わる1人ぶん（自分のラン・HP・ポーチ・レリック）。</summary>
    public struct PartyMember
    {
        public Combatant player;
        public DicePouch pouch;
        public EffectBus effects;
        public Run.RunState run;
        public Random rng;   // ダイスを振る乱数（各自のランの戦闘用）
    }

    /// <summary>
    /// 協力「賽の旅団」の戦闘（STS2 風）。全員が同じ敵と戦う。
    /// 1人ずつの戦闘（BattleState）は、ダイスを振る・割り振る・攻撃を当てる・敵の行動を受ける、を今までどおり行う。
    /// 敵の予告と、誰を狙うか（EnemyState.targetSlot）は、この戦闘が決める。
    /// ラウンドの流れ：全員が割り振りを終える → 席の順に攻撃 → 敵の毒 → 敵がそれぞれ狙った人へ行動 → ラウンド終了。
    /// 倒れた人はその戦闘では休み、勝てば HP 1 で起き上がる。全員倒れたら負け。
    /// </summary>
    public class PartyBattle
    {
        // TODO(仕様): 人数による敵の HP の倍率は仮（1人増えるごとに +70%）
        public const int HpPercentPerExtraMember = 70;
        public const string BannerRelicId = "ryodannohata";
        public const string CordRelicId = "yuihimo";
        // TODO(仕様): 結い紐の防御の量は仮（仲間1人につき）
        public const int CordBlockPerAlly = 3;

        public readonly List<EnemyState> enemies = new List<EnemyState>();
        readonly List<BattleState> members = new List<BattleState>();
        readonly Random rng;
        int round;

        public IReadOnlyList<BattleState> Members => members;
        public int Round => round;
        public BattleOutcome Outcome { get; private set; } = BattleOutcome.Ongoing;

        /// <param name="seed">敵の予告と狙いを決める乱数のシード（全員の端末で同じにする）。</param>
        public PartyBattle(IReadOnlyList<PartyMember> party, IReadOnlyList<EnemyData> enemyData, int seed, int enemyHpPercent = 100)
        {
            if (party == null || party.Count == 0) throw new ArgumentException("パーティーがいません。", nameof(party));
            if (enemyData == null || enemyData.Count == 0) throw new ArgumentException("敵がいません。", nameof(enemyData));
            rng = new Random(seed);

            // 敵を作る（種類ごとの倍率は先頭の人のランから。人数が多いほど HP を増やす）
            var leaderRun = party[0].run;
            var balance = leaderRun?.config?.enemyBalance;
            int layer = leaderRun != null ? leaderRun.LayerIndex : 0;
            int partyPercent = 100 + HpPercentPerExtraMember * (party.Count - 1);
            foreach (var data in enemyData)
            {
                int hpPercent = enemyHpPercent * partyPercent / 100;
                if (balance != null) hpPercent = hpPercent * balance.HpPercent(data.kind, layer) / 100;
                var e = new EnemyState(data, hpPercent);
                if (balance != null) e.attackPercent = balance.AttackPercent(data.kind, layer);
                enemies.Add(e);
            }

            foreach (var m in party)
            {
                var state = new BattleState(m.player, enemies, m.pouch, m.rng ?? new Random(seed), m.effects, m.run, false);
                state.partySeat = members.Count; // 受けるダメージの見込みは、自分を狙う敵だけで出す
                members.Add(state);
            }
            // 旅団の旗（みんなで遊ぶとき専用のレリック）：戦闘の始めに、仲間1人につき筋力+1
            // TODO(仕様): 量は仮
            foreach (var m in party)
            {
                if (m.run != null && m.run.Relics.Any(r => r != null && r.id == BannerRelicId)) m.player.strength += party.Count - 1;
                // 結い紐（みんなで遊ぶとき専用のレリック）：戦闘の始めに、仲間1人につき防御+3
                if (m.run != null && m.run.Relics.Any(r => r != null && r.id == CordRelicId)) m.player.block += CordBlockPerAlly * (party.Count - 1);
            }
            round = 1;
            PrepareIntents(0);
        }

        IEnumerable<int> ActiveSlots => Enumerable.Range(0, members.Count).Where(i => members[i].Outcome == BattleOutcome.Ongoing);

        /// <summary>
        /// 仲間にも効くお守り（分け合いの薬・結びの守り）を、使った人の席から、ほかのまだ戦っている仲間に効かせる。
        /// 使った本人への効果は RunState.UseCharm で済ませておく。効いた仲間の数を返す。
        /// </summary>
        public int ShareCharm(int seat, Run.CharmData charm)
        {
            if (charm == null || !charm.AffectsParty) return 0;
            int count = 0;
            foreach (int i in ActiveSlots)
            {
                if (i == seat) continue;
                var p = members[i].player;
                if (charm.kind == Run.CharmKind.HealParty) p.Heal(charm.amount);
                else p.block += charm.amount;
                count++;
            }
            return count;
        }

        /// <summary>敵の予告を出し、それぞれ誰を狙うかを決める（倒れていない人の中から）。</summary>
        void PrepareIntents(int lastAttackTotal)
        {
            var active = ActiveSlots.ToList();
            foreach (var e in enemies.Where(e => !e.IsDead))
            {
                e.PrepareIntent(round, rng, lastAttackTotal);
                e.targetSlot = active.Count > 0 ? active[rng.Next(active.Count)] : 0;
            }
        }

        /// <summary>
        /// 全員の割り振りで1ラウンドを進める。席の順に1人ずつの結果を返す（このラウンドに戦っていない人は null）。
        /// </summary>
        public IReadOnlyList<RoundResult?> Resolve()
        {
            if (Outcome != BattleOutcome.Ongoing) throw new InvalidOperationException("戦闘は終わっています。");
            var works = new BattleState.RoundWork[members.Count];
            var active = ActiveSlots.ToList();
            var aliveAtStart = enemies.Select(e => !e.IsDead).ToArray();

            // 席の順に攻撃（前の人が倒した敵には、あとの人の攻撃は当たらない）
            foreach (int i in active) works[i] = members[i].AttackPhase();

            // 敵の毒は1回だけ（結果は全員の記録に写す）
            var poison = enemies.Select((e, i) => new EnemyRoundInfo { enemy = e, index = i, hpBefore = e.hp, intent = e.CurrentIntent }).ToList();
            members[active[0]].EnemyPoisonPhase(poison);
            foreach (int i in active)
            {
                foreach (var info in works[i].infos)
                {
                    info.poisonDamage = poison[info.index].poisonDamage;
                    info.diedOfPoison = poison[info.index].diedOfPoison;
                    info.strengthGained += poison[info.index].strengthGained;
                }
            }

            // 棘で倒れた人
            foreach (int i in active)
            {
                if (members[i].player.IsDead) members[i].SetOutcome(BattleOutcome.Defeat);
            }

            if (!enemies.Any(e => !e.IsDead))
            {
                foreach (int i in active) if (members[i].Outcome == BattleOutcome.Ongoing) members[i].SetOutcome(BattleOutcome.Victory);
            }
            else
            {
                // 敵が並び順に、狙った人へ行動する（狙った人が倒れていたら、残っている人のうち席の若い人へ）
                for (int k = 0; k < enemies.Count; k++)
                {
                    var e = enemies[k];
                    if (e.IsDead || !aliveAtStart[k]) continue;
                    int slot = e.targetSlot;
                    if (slot < 0 || slot >= members.Count || members[slot].Outcome != BattleOutcome.Ongoing)
                    {
                        var stillUp = ActiveSlots.ToList();
                        if (stillUp.Count == 0) break;
                        slot = stillUp[0];
                    }
                    var info = works[slot] != null ? works[slot].infos[k] : null;
                    if (info == null) continue;
                    members[slot].ActOn(info);
                }
            }

            var results = new RoundResult?[members.Count];
            foreach (int i in active) results[i] = members[i].FinishPlayerRound(works[i]);
            members[active[0]].EndEnemyRound();

            if (members.All(m => m.Outcome == BattleOutcome.Defeat))
            {
                Outcome = BattleOutcome.Defeat;
            }
            else if (!enemies.Any(e => !e.IsDead))
            {
                Outcome = BattleOutcome.Victory;
                // 倒れていた人は HP 1 で起き上がる（勝ちに数える。報酬も各自で受け取る）
                foreach (var m in members)
                {
                    if (m.player.IsDead) m.player.hp = 1;
                    m.SetOutcome(BattleOutcome.Victory);
                }
            }
            else
            {
                // 次のラウンド：敵の行動の順番を進めて、予告と狙いを決める（写し鏡は全員の攻撃の合計を写す）
                int attackTotal = active.Sum(i => members[i].LastAttack);
                foreach (var e in enemies.Where(e => !e.IsDead)) e.AdvancePattern();
                round++;
                foreach (int i in ActiveSlots.ToList()) members[i].NextRound();
                PrepareIntents(attackTotal);
            }
            return results;
        }
    }
}
