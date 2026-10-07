using System;
using System.Collections.Generic;
using SaiNoMichi.Battle;
using SaiNoMichi.Board;
using UnityEngine;

namespace SaiNoMichi.UI
{
    /// <summary>
    /// 画面で使う絵の一覧。絵がまだないものは null のままでよく、その場合は各画面が仮の図形で表示する。
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

        [Header("エフェクト")]
        public Sprite fxSlash;
        public Sprite fxBlock;
        public Sprite fxHit;

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
                default: return null;
            }
        }
    }
}
