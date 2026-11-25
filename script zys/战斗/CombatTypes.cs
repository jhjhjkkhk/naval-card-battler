using System;
using System.Collections.Generic;

// ============================================================================
// 战斗数据结构：一发炮弹的全部结果 / 一次齐射的汇总
// 放在这里方便别的地方引用（UI、日志、录像）
// ============================================================================

/// <summary>命中部位</summary>
public enum HitZone
{
    Deck,        // 甲板
    Bridge,      // 舰桥
    Waterline,   // 水线
    NearMiss     // 近失（落水）
}

/// <summary>单发炮弹的结局</summary>
public enum ShotOutcome
{
    NearMiss,        // 近失：落水爆炸，只有溅射
    Ricochet,        // 跳弹：弹开
    NoPenetration,   // 未击穿：打进装甲但没穿
    Penetration,     // 击穿
    Overmatch        // 碾压：穿深够大，无视跳弹直接击穿
}

/// <summary>debuff 类型</summary>
public enum DebuffType
{
    None,
    Fire,        // 起火：每回合 -5% 血
    Flooding,    // 漏水：每回合 -3% 血
    Engine,      // 轮机破损：速度 -40%，转弯半径 +50%
    Gun,         // 炮机受损：命中率 -30%
    Magazine     // 弹药库受损：本回合不能开炮，10% 殉爆
}

/// <summary>一发炮弹的完整结算结果</summary>
[Serializable]
public struct ShotResult
{
    public ShotOutcome outcome;
    public HitZone zone;
    public float impactAngle;       // 迎弹角 α（0=擦弹，90=垂直）
    public float armorThickness;    // 该部位的原始装甲
    public float effectiveArmor;    // 等效装甲
    public float penetration;       // 该距离的穿深
    public float damage;            // 造成的伤害
    public bool critical;           // 是否致命一击
    public DebuffType debuff;       // 附带 debuff（None = 没有）

    public string Describe()
    {
        string s = zone + " / " + outcome;
        if (outcome == ShotOutcome.Penetration || outcome == ShotOutcome.Overmatch)
            s += " / 穿深 " + penetration.ToString("0") + " vs 等效 " + effectiveArmor.ToString("0")
               + " / 伤害 " + damage.ToString("0");
        if (critical) s += " / 致命一击";
        if (debuff != DebuffType.None) s += " / " + debuff;
        return s;
    }
}

/// <summary>一次齐射的汇总</summary>
[Serializable]
public class SalvoResult
{
    public List<ShotResult> shots = new List<ShotResult>();
    public float totalDamage;
    public int hits;            // 命中船体（不含近失）
    public int nearMisses;
    public int penetrations;
    public int ricochets;
    public int criticals;

    public void Accumulate()
    {
        totalDamage = 0f;
        hits = nearMisses = penetrations = ricochets = criticals = 0;

        for (int i = 0; i < shots.Count; i++)
        {
            ShotResult s = shots[i];
            totalDamage += s.damage;

            if (s.zone == HitZone.NearMiss) nearMisses++;
            else hits++;

            if (s.outcome == ShotOutcome.Ricochet) ricochets++;
            if (s.outcome == ShotOutcome.Penetration || s.outcome == ShotOutcome.Overmatch) penetrations++;
            if (s.critical) criticals++;
        }
    }

    public string Summarize()
    {
        return "命中 " + hits + " / 近失 " + nearMisses
             + " / 击穿 " + penetrations + " / 跳弹 " + ricochets
             + " / 暴击 " + criticals + " / 总伤害 " + totalDamage.ToString("0");
    }
}
