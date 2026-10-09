using System;
using System.Collections.Generic;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 画面で使う絵と効果音の一覧。絵がまだないものは null のままでよく、その場合は各画面が仮の図形で表示する（音は鳴らない）。
    /// </summary>
    [CreateAssetMenu(menuName = "SaiNoMichi/UI Art", fileName = "UIArt")]
    public class UIArt : ScriptableObject
    {
        [Serializable]
        public struct EnemySprite
        {
            public string enemyId;
            public Sprite sprite;
        }

        [Header("背景")]
        public Sprite mapBackground;
        public Sprite battleBackground;
        [Tooltip("始めの画面の背景（bg_title）。なければマップの背景")]
        public Sprite titleBackground;
        [Tooltip("第2層・第3層の背景（要素1が第2層）。ないときは上の背景を使う")]
        public Sprite[] layerMapBackgrounds = new Sprite[3];
        public Sprite[] layerBattleBackgrounds = new Sprite[3];

        public Sprite MapBackgroundFor(int layer) => Pick(layerMapBackgrounds, layer) ?? mapBackground;
        public Sprite BattleBackgroundFor(int layer) => Pick(layerBattleBackgrounds, layer) ?? battleBackground;
        static Sprite Pick(Sprite[] list, int layer) => list != null && layer > 0 && layer < list.Length && list[layer] != null ? list[layer] : null;

        [Header("マップ")]
        public Sprite player;
        public Sprite tileStart;
        public Sprite tileEmpty;
        public Sprite tileBattle;
        public Sprite tileRest;
        public Sprite tileBoss;
        public Sprite tileEvent;
        public Sprite tileTrap;
        public Sprite tileTreasure;
        public Sprite tileShop;
        public Sprite tileForge;
        public Sprite tileElite;
        public Sprite tileShrine;
        public Sprite tileCheckpoint;
        public Sprite tileTeahouse;
        public Sprite tileDiceHall;

        [Header("ダイス")]
        public Sprite faceBlank;
        [Tooltip("1〜6 の目。要素0が1の目")]
        public Sprite[] faces = new Sprite[6];

        [Header("戦闘")]
        public List<EnemySprite> enemies = new List<EnemySprite>();
        public Sprite intentAttack;
        public Sprite intentBlock;
        public Sprite intentBuff;
        public Sprite intentMulti;
        public Sprite intentDebuff;
        public Sprite intentSeal;
        public Sprite intentDice;
        [Tooltip("フェーズ2の予告（ないときは近い絵で代わりに出す）")]
        public Sprite intentPoison;
        public Sprite intentCharge;
        public Sprite intentCurse;

        [Header("状態・特性の印（ないときは漢字1文字で出す。防御は intentBlock を使う）")]
        public Sprite statusStrength;
        public Sprite statusWeak;
        public Sprite statusVulnerable;
        public Sprite statusFrail;
        public Sprite statusPoison;
        public Sprite statusFortify;
        public Sprite statusBind;
        public Sprite traitThorns;
        public Sprite traitWall;
        public Sprite traitAlly;
        public Sprite traitEnrage;
        public Sprite traitPhase;
        public Sprite traitInvert;

        [Header("エフェクト")]
        public Sprite fxSlash;
        public Sprite fxBlock;
        public Sprite fxHit;

        [Header("効果音（Audio/SE の se_*.mp3 などを Update Art で取り込む）")]
        public List<SoundEntry> sounds = new List<SoundEntry>();

        [Header("BGM（Audio/BGM の bgm_*.mp3 などを Update Art で取り込む。ないときは無音）")]
        public AudioClip bgmTitle;
        public AudioClip bgmMap;
        public AudioClip bgmBattle;
        [Tooltip("エリート戦。ないときは通常の戦闘の曲")]
        public AudioClip bgmElite;
        public AudioClip bgmBoss;
        [Tooltip("第2層・第3層のマップと戦闘の曲（要素1が第2層）。ないときは上の曲")]
        public AudioClip[] layerBgmMap = new AudioClip[3];
        public AudioClip[] layerBgmBattle = new AudioClip[3];

        public AudioClip BgmFor(BgmScene scene, int layer)
        {
            switch (scene)
            {
                case BgmScene.Title: return bgmTitle != null ? bgmTitle : bgmMap;
                case BgmScene.Map: return PickClip(layerBgmMap, layer) ?? bgmMap;
                case BgmScene.Battle: return PickClip(layerBgmBattle, layer) ?? bgmBattle;
                case BgmScene.Elite: return bgmElite != null ? bgmElite : (PickClip(layerBgmBattle, layer) ?? bgmBattle);
                case BgmScene.Boss: return bgmBoss != null ? bgmBoss : (PickClip(layerBgmBattle, layer) ?? bgmBattle);
                default: return null;
            }
        }

        static AudioClip PickClip(AudioClip[] list, int layer) => list != null && layer > 0 && layer < list.Length && list[layer] != null ? list[layer] : null;

        public Sprite TileSprite(TileNode tile)
        {
            if (tile.id == 0) return tileStart;
            switch (tile.type)
            {
                case TileType.Battle: return tileBattle;
                case TileType.Rest: return tileRest;
                case TileType.Boss: return tileBoss;
                case TileType.Empty: return tileEmpty;
                case TileType.Event: return tileEvent;
                case TileType.Trap: return tileTrap;
                case TileType.Treasure: return tileTreasure;
                case TileType.Shop: return tileShop;
                case TileType.Forge: return tileForge;
                case TileType.Elite: return tileElite;
                case TileType.Shrine: return tileShrine;
                case TileType.Checkpoint: return tileCheckpoint;
                case TileType.Teahouse: return tileTeahouse;
                case TileType.DiceHall: return tileDiceHall;
                default: return null; // 絵がまだないマスは仮の図形

            }
        }

        /// <summary>出目の絵。1〜6 は目の絵、それ以外（0 や 7 以上）は null（無地の面に数字を重ねる）。</summary>
        public Sprite FaceSprite(int value)
        {
            if (faces == null || value < 1 || value > faces.Length) return null;
            return faces[value - 1];
        }

        public Sprite EnemySpriteFor(EnemyData enemy)
        {
            if (enemy == null) return null;
            foreach (var e in enemies)
            {
                if (e.enemyId == enemy.id) return e.sprite;
            }
            return null;
        }

        public Sprite IntentSprite(IntentType type)
        {
            switch (type)
            {
                case IntentType.Attack: return intentAttack;
                case IntentType.Block: return intentBlock;
                case IntentType.Buff: return intentBuff;
                case IntentType.MultiAttack: return intentMulti != null ? intentMulti : intentAttack;
                case IntentType.Debuff: return intentDebuff;
                case IntentType.Seal: return intentSeal;
                case IntentType.DiceRoll: return intentDice != null ? intentDice : intentAttack;
                case IntentType.ResetDice: return intentDice;
                case IntentType.MirrorAttack: return intentAttack;
                case IntentType.Poison: return intentPoison != null ? intentPoison : intentDebuff;
                case IntentType.Vulnerable:
                case IntentType.Frail:
                case IntentType.Bind: return intentDebuff;
                case IntentType.Curse: return intentCurse != null ? intentCurse : intentSeal;
                case IntentType.Charge: return intentCharge != null ? intentCharge : intentBuff;
                case IntentType.RewriteFate: return intentCurse != null ? intentCurse : intentSeal;
                case IntentType.Invert: return intentCurse != null ? intentCurse : intentSeal;
                default: return null;
            }
        }
    }
}
