using System.Collections.Generic;
using UnityEngine;

// buff 条目：一张卡打在船上留下的持续效果
// 崩铁式：信息牌上显示一个小图标，点图标 → 屏幕前方弹出详情（名称 / 效果 / 剩余回合）
[System.Serializable]
public class BuffEntry
{
    public CardDefinition.CardType type;
    public string buffName;          // 显示名（取卡牌名）
    public int value;                // ★ 带符号：攻击+5 → 5；攻击-3 → -3
    public int remainingTurns;       // 剩余回合（0 = 即时效果，不占回合）
    public Sprite icon;              // 小图标
    public string description;       // 卡牌上自己写的描述

    public bool AffectsAttack  { get { return type == CardDefinition.CardType.AttackUp  || type == CardDefinition.CardType.AttackDown;  } }
    public bool AffectsDefense { get { return type == CardDefinition.CardType.DefenseUp || type == CardDefinition.CardType.DefenseDown; } }
    public bool IsInstant      { get { return type == CardDefinition.CardType.Heal; } }

    // 剩余回合怎么显示
    public string TurnsText { get { return IsInstant ? "即时生效" : remainingTurns + " 回合"; } }

    // 效果描述（value 已带符号，减益不用再加 "-"）
    public string EffectText
    {
        get
        {
            switch (type)
            {
                case CardDefinition.CardType.AttackUp:    return "攻击 +" + value;
                case CardDefinition.CardType.AttackDown:  return "攻击 " + value;
                case CardDefinition.CardType.DefenseUp:   return "防御 +" + value;
                case CardDefinition.CardType.DefenseDown: return "防御 " + value;
                case CardDefinition.CardType.Heal:        return "立即回复生命 " + value;
                default:                                  return "";
            }
        }
    }
}

// 船舰数值系统：攻击/防御/生命/敌我阵营 + 吃卡牌buff的结算
// 挂在每一艘船的最外层物体上（这个物体还需要一个 Collider 用于点击选中）
public class ShipStats : MonoBehaviour
{
    [Header("基本信息")]
    public string shipName = "战舰";
    public bool isEnemy = false;              // true = 敌方，false = 友方

    [Header("外观")]
    public Sprite shipIcon;                   // ★ 把这艘船的图片拖到这里，信息牌顶部会显示

    [Header("基础数值（不吃buff时的本来值）")]
    public int baseAttack = 10;
    public int baseDefense = 5;
    public int maxHp = 100;

    [Header("buff 设置")]
    public int buffDefaultTurns = 1;          // ★ 新 buff 默认持续几回合（1 = 下个己方回合开始时消失）

    [Header("运行时状态（开局自动填，不用管）")]
    public int currentHp;
    public List<BuffEntry> buffs = new List<BuffEntry>();   // ★ 当前挂着的 buff（信息牌图标栏读这个）
    public List<string> buffRecords = new List<string>();   // 吃过的buff流水记录（信息牌第3行显示用）

    [HideInInspector]
    public ShipInfoPanel panel;               // 头顶信息牌（开局自动创建）

    // 加成总量 = 所有生效中 buff 的合计（★ 改成动态算，不再是永久累加）
    public int bonusAttack  { get { return SumBuff(true); } }
    public int bonusDefense { get { return SumBuff(false); } }

    // 当前实际值 = 基础值 + 加成
    public int CurrentAttack  { get { return baseAttack + bonusAttack; } }
    public int CurrentDefense { get { return baseDefense + bonusDefense; } }

    int SumBuff(bool attack)
    {
        int sum = 0;
        for (int i = 0; i < buffs.Count; i++)
        {
            BuffEntry b = buffs[i];
            if (b == null) continue;
            if (attack  && b.AffectsAttack)  sum += b.value;
            if (!attack && b.AffectsDefense) sum += b.value;
        }
        return sum;
    }

    void Awake()
    {
        currentHp = maxHp;                    // 开局满血
        panel = ShipInfoPanel.Create(this);   // 自动在头顶造一块信息牌
    }

    // 尝试把一张卡打到此船：合规并生效返回 true；不合规返回 false 并给出原因
    // 这个版本默认"施法者是玩家"
    public bool TryApplyCard(CardDefinition card, out string failReason)
    {
        return TryApplyCard(card, false, out failReason);
    }

    // 带"施法者阵营"的版本：casterIsEnemy = true 表示出牌的是敌方 AI，false 表示玩家
    // 规则统一成：增益卡只能打自己人，减益卡只能打敌人（双方通用）
    public bool TryApplyCard(CardDefinition card, bool casterIsEnemy, out string failReason)
    {
        failReason = "";
        if (card == null) { failReason = "这张卡没有配置数据"; return false; }

        // 这艘船和出牌的人是不是同一边的？
        bool sameSide = (casterIsEnemy == isEnemy);
        string sideName = isEnemy ? "敌方" : "友方";

        if (card.IsBuff && !sameSide)
        { failReason = "增益卡只能对己方船舰使用（" + shipName + " 是" + sideName + "）"; return false; }
        if (!card.IsBuff && sameSide)
        { failReason = "减益卡只能对敌方船舰使用（" + shipName + " 是" + sideName + "）"; return false; }

        // 结算效果
        switch (card.type)
        {
            case CardDefinition.CardType.AttackUp:
                AddBuff(card, card.value);
                break;
            case CardDefinition.CardType.DefenseUp:
                AddBuff(card, card.value);
                break;
            case CardDefinition.CardType.AttackDown:
                AddBuff(card, -card.value);
                break;
            case CardDefinition.CardType.DefenseDown:
                AddBuff(card, -card.value);
                break;
            case CardDefinition.CardType.Heal:
                // 满血就别浪费了：直接拒绝，卡牌收回手里
                if (currentHp >= maxHp) { failReason = shipName + " 生命值已满，无需治疗"; return false; }
                int healed = Mathf.Min(card.value, maxHp - currentHp);  // 回血不超上限
                currentHp += healed;
                AddBuff(card, healed, 0);   // ★ 即时效果：留一个图标供查看，不占回合
                break;
        }
        return true;
    }

    // 挂一条 buff 上来（同种卡打两张 = 两个图标，能直观看出叠了几层）
    // ★ 持续回合：优先用卡牌自己的 durationTurns，卡牌没设（<=0）才用 buffDefaultTurns
    void AddBuff(CardDefinition card, int signedValue, int turns = -1)
    {
        if (turns < 0)
            turns = (card != null && card.durationTurns > 0) ? card.durationTurns : buffDefaultTurns;

        buffs.Add(new BuffEntry
        {
            type           = card.type,
            buffName       = card.cardName,
            value          = signedValue,
            remainingTurns = turns,
            icon           = card.icon,
            description    = card.description
        });

        buffRecords.Add(card.cardName);
    }

    // ★ 我方回合一开始调用（TurnManager 负责调）：所有 buff 剩余回合 -1，归零的消除
    public void TickBuffs()
    {
        for (int i = buffs.Count - 1; i >= 0; i--)
        {
            BuffEntry b = buffs[i];
            if (b == null) { buffs.RemoveAt(i); continue; }

            b.remainingTurns--;
            if (b.remainingTurns <= 0)
            {
                Debug.Log("[buff] " + shipName + " 的「" + b.buffName + "」到期消失");
                buffs.RemoveAt(i);
            }
        }
    }

    // 数值格式化：没加成 → "10"；有增益 → "15(10+5)"；被减益 → "7(10-3)"
    public static string FormatStat(int baseValue, int bonus)
    {
        if (bonus == 0) return baseValue.ToString();
        string sign = bonus > 0 ? "+" : "";
        return (baseValue + bonus) + "(" + baseValue + sign + bonus + ")";
    }
}
