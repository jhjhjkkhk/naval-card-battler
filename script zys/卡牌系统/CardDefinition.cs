using UnityEngine;

// 卡牌定义：挂在"每一张卡牌预制体"上——这张卡叫什么、什么效果、数值多少
// 做一张新卡 = 克隆一份卡牌预制体 → 改这里的信息（顺便可以给牌面换颜色/图案）
public class CardDefinition : MonoBehaviour
{
    public enum CardType            // 卡牌效果类型
    {
        AttackUp,     // 增益：攻击提升（只能给友方）
        DefenseUp,    // 增益：防御提升（只能给友方）
        Heal,         // 增益：回复生命，不超过上限（只能给友方）
        AttackDown,   // 减益：攻击降低（只能给敌方）
        DefenseDown,  // 减益：防御降低（只能给敌方）
        Draw          // 功能牌：从牌库抽 N 张（value = 抽几张），不挑目标
    }

    // 稀有度：越往下越稀有，在牌库里放的份数越少 → 抽到的概率越低
    public enum Rarity
    {
        Common,     // 普通
        Rare,       // 稀有
        Epic,       // 史诗
        Legendary   // 传说
    }

    [Header("卡牌信息")]
    public string cardName = "新卡牌";           // 卡牌名字（buff名，会印在牌面上）
    public CardType type = CardType.AttackUp;   // 效果类型
    public int value = 5;                       // 效果数值
    public Rarity rarity = Rarity.Common;       // 稀有度（决定这张牌在牌库里放几份）
    public Sprite icon;                         // ★ buff 图标：打出去后显示在船舰信息牌的状态栏里
    [TextArea] public string description;       // ★ 详情描述（留空就用下面自动生成的那句）

    [Header("持续时间")]
    [Tooltip("这张卡的 buff 持续几回合（1 = 出牌方的下个回合开始时消失）。填 0 → 用 ShipStats 上的默认值")]
    public int durationTurns = 1;   // ★ 每张卡可以不一样：强力卡设 2~3，普通卡设 1

    // 每种稀有度的代表色（给牌面染色用，一眼看出稀有度）
    public Color RarityColor
    {
        get
        {
            switch (rarity)
            {
                case Rarity.Rare: return new Color(0.30f, 0.55f, 1.00f); // 蓝
                case Rarity.Epic: return new Color(0.65f, 0.35f, 0.95f); // 紫
                case Rarity.Legendary: return new Color(1.00f, 0.72f, 0.20f); // 金
                default: return new Color(0.85f, 0.85f, 0.85f); // 普通：灰白
            }
        }
    }

    // 是增益卡吗（攻击/防御/回血）？减益卡返回 false
    public bool IsBuff
    {
        get { return type == CardType.AttackUp || type == CardType.DefenseUp || type == CardType.Heal; }
    }
}
