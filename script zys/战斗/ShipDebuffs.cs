using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 舰船 debuff 系统（独立组件，不碰 ShipStats）
//
// 五个 debuff：
//   Fire     起火：每回合 -5% 最大血（持续 4 回合）
//   Flooding 漏水：每回合 -3% 最大血
//   Engine   轮机破损：航速 ×0.6，散布变大
//   Gun      炮机受损：散布 ×1.3
//   Magazine 弹药库受损：本回合不能开火（殉爆由结算时单独判）
//
// 结算时机：我方回合开始（TurnManager 调 TickAll）
// ============================================================================
public class ShipDebuffs : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public DebuffType type;
        public int turns;
    }

    [Header("当前 debuff")]
    public List<Entry> active = new List<Entry>();

    public const int DefaultTurns = 4;

    // ------------------------------------------------------------------ 获取

    public static ShipDebuffs Get(ShipStats ship)
    {
        if (ship == null) return null;
        ShipDebuffs d = ship.GetComponent<ShipDebuffs>();
        if (d == null) d = ship.gameObject.AddComponent<ShipDebuffs>();
        return d;
    }

    public bool Has(DebuffType t)
    {
        for (int i = 0; i < active.Count; i++) if (active[i].type == t) return true;
        return false;
    }

    public Entry Find(DebuffType t)
    {
        for (int i = 0; i < active.Count; i++) if (active[i].type == t) return active[i];
        return null;
    }

    // ------------------------------------------------------------------ 施加

    public void Apply(DebuffType t, int turns = DefaultTurns)
    {
        if (t == DebuffType.None) return;

        Entry e = Find(t);
        if (e != null)
        {
            e.turns = Mathf.Max(e.turns, turns);      // 同类刷新，不叠加
        }
        else
        {
            active.Add(new Entry { type = t, turns = turns });
        }

        Debug.Log("[debuff] " + name + " 获得 " + t + "（" + turns + " 回合）");
    }

    // ------------------------------------------------------------------ 影响

    /// <summary>航速倍率</summary>
    public float SpeedMultiplier { get { return Has(DebuffType.Engine) ? 0.6f : 1f; } }

    /// <summary>散布倍率（炮机受损 → 打得更散）</summary>
    public float ScatterMultiplier { get { return Has(DebuffType.Gun) ? 1.3f : 1f; } }

    /// <summary>本回合不能开火</summary>
    public bool CannotFire { get { return Has(DebuffType.Magazine); } }

    // ------------------------------------------------------------------ 结算

    /// <summary>我方回合开始：先扣持续伤害，再减回合数</summary>
    public void TickTurn()
    {
        ShipStats stats = GetComponent<ShipStats>();
        if (stats == null) return;

        float loss = 0f;

        if (Has(DebuffType.Fire)) loss += stats.maxHp * 0.05f;
        if (Has(DebuffType.Flooding)) loss += stats.maxHp * 0.03f;

        if (loss > 0f && stats.currentHp > 0)
        {
            int dmg = Mathf.Max(1, Mathf.RoundToInt(loss));
            stats.currentHp = Mathf.Max(0, stats.currentHp - dmg);
            Debug.Log("[debuff] " + stats.shipName + " 持续伤害 " + dmg
                      + " → HP " + stats.currentHp + "/" + stats.maxHp);

            if (stats.currentHp <= 0) ShipSink.Sink(stats, "持续伤害");
        }

        // 回合数递减，归零移除
        for (int i = active.Count - 1; i >= 0; i--)
        {
            active[i].turns--;
            if (active[i].turns <= 0)
            {
                Debug.Log("[debuff] " + name + " 的 " + active[i].type + " 结束");
                active.RemoveAt(i);
            }
        }
    }

    /// <summary>全场所有船的 debuff 结算（TurnManager 调用）</summary>
    public static void TickAll()
    {
        foreach (ShipDebuffs d in FindObjectsOfType<ShipDebuffs>())
            if (d != null) d.TickTurn();
    }

    public string Describe()
    {
        if (active.Count == 0) return "";
        string s = "";
        for (int i = 0; i < active.Count; i++)
            s += (i > 0 ? "  " : "") + active[i].type + "(" + active[i].turns + ")";
        return s;
    }
}