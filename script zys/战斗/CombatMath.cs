using UnityEngine;

// ============================================================================
// 射击系统的纯数学：全部是静态函数，方便单独测试和调数值
//
// 判定顺序：
//   命中率 → 门数 → 每发掷部位 → 跳弹 → 等效装甲 → 穿深 → 伤害 → debuff
// ============================================================================
public static class CombatMath
{
    // ---------------------------------------------------------------- 命中

    /// <summary>命中率 = max(1 - 距离 × 散布, 保底)</summary>
    public static float HitChance(float distanceHex, float dispersionPerHex, float minChance = 0.05f)
    {
        float c = 1f - Mathf.Max(distanceHex, 0f) * Mathf.Max(dispersionPerHex, 0f);
        return Mathf.Clamp(c, minChance, 1f);
    }

    /// <summary>命中发数 = floor(门数 × 命中率)，保底 1 发</summary>
    public static int ShellsHit(int barrels, float hitChance)
    {
        if (barrels <= 0) return 0;
        int n = Mathf.FloorToInt(barrels * Mathf.Clamp01(hitChance));
        return Mathf.Max(n, 1);
    }

    // ---------------------------------------------------------------- 部位

    /// <summary>近失率 = 10% + 20% × (距离 / 最大射程)</summary>
    public static float NearMissChance(float distanceHex, float maxRangeHex)
    {
        CombatSettings s = CombatSettings.Instance;
        if (maxRangeHex <= 0.01f) return s.nearMissBase;
        float t = Mathf.Clamp01(distanceHex / maxRangeHex);
        return s.nearMissBase + s.nearMissPerRange * t;
    }

    /// <summary>掷部位：近失按距离算，其余三个按 2:3:3 分摊</summary>
    public static HitZone RollZone(float nearMissChance, float roll01)
    {
        CombatSettings s = CombatSettings.Instance;

        float nm = Mathf.Clamp(nearMissChance, 0f, s.nearMissMax);
        if (roll01 < nm) return HitZone.NearMiss;

        // 剩余概率按权重分配（默认 甲板2 : 舰桥3 : 水线3）
        float total = s.zoneWeightDeck + s.zoneWeightBridge + s.zoneWeightWaterline;
        if (total <= 0.001f) total = 8f;

        float rest = 1f - nm;
        float deckEnd = nm + rest * (s.zoneWeightDeck / total);
        float bridgeEnd = deckEnd + rest * (s.zoneWeightBridge / total);

        if (roll01 < deckEnd) return HitZone.Deck;
        if (roll01 < bridgeEnd) return HitZone.Bridge;
        return HitZone.Waterline;
    }

    /// <summary>按部位取装甲厚度</summary>
    public static float ArmorForZone(ShipStats ship, HitZone zone)
    {
        switch (zone)
        {
            case HitZone.Deck: return ship.armorDeck;
            case HitZone.Bridge: return ship.armorTower;
            case HitZone.Waterline: return ship.armorBelt;
            default: return 0f;   // 近失不穿甲
        }
    }

    // ---------------------------------------------------------------- 角度与跳弹

    /// <summary>
    /// 迎弹角：0 = 擦弹（炮弹与船体平行），90 = 垂直命中。
    /// shellDir / hullForward 都是水平方向向量。
    /// </summary>
    public static float ImpactAngle(Vector3 shellDir, Vector3 hullForward)
    {
        shellDir.y = 0f; hullForward.y = 0f;
        if (shellDir.sqrMagnitude < 0.0001f || hullForward.sqrMagnitude < 0.0001f) return 90f;

        float between = Vector3.Angle(shellDir.normalized, hullForward.normalized); // 0~180
        if (between > 180f) between = 360f - between;

        // 炮弹与船身夹角越小 → 越接近擦弹 → α 越小
        float parallel = Mathf.Min(between, 180f - between);   // 0~90
        return Mathf.Clamp(90f - parallel, 0f, 90f);
    }

    /// <summary>跳弹概率：α<30 必弹；30~45 线性；≥45 不弹</summary>
    public static float RicochetChance(float angle)
    {
        CombatSettings s = CombatSettings.Instance;
        float lo = s.ricochetMinAngle;
        float hi = s.ricochetMaxAngle;

        if (angle < lo) return 1f;
        if (angle < hi) return (hi - angle) / Mathf.Max(hi - lo, 0.01f);
        return 0f;
    }

    // ---------------------------------------------------------------- 装甲与穿深

    /// <summary>等效装甲 = 装甲 / sin(α + 转正角)，sin 里夹到 90°</summary>
    public static float EffectiveArmor(float armor, float angle, float normalization)
    {
        if (armor <= 0f) return 0f;
        float a = Mathf.Clamp(angle + normalization, 1f, 90f);
        return armor / Mathf.Sin(a * Mathf.Deg2Rad);
    }

    /// <summary>穿深 = 基础穿深 × (1 - 衰减 × 距离/最大射程)</summary>
    public static float Penetration(float basePenetration, float distanceHex, float maxRangeHex)
    {
        if (maxRangeHex <= 0.01f) return basePenetration;
        float t = Mathf.Clamp01(distanceHex / maxRangeHex);
        return basePenetration * (1f - CombatSettings.Instance.penetrationDecay * t);
    }

    /// <summary>碾压：穿深 ≥ 3 × 装甲 → 无视跳弹强制击穿</summary>
    public static bool IsOvermatch(float penetration, float armor)
    {
        float factor = CombatSettings.Instance.overmatchFactor;
        return armor > 0f && penetration >= factor * armor;
    }

    // ---------------------------------------------------------------- 伤害

    /// <summary>伤害 = 基础 × (1 + 0.3×(穿深/等效装甲 - 1))，上限 2 倍</summary>
    public static float Damage(float baseDamage, float penetration, float effectiveArmor, float critMultiplier = 1f)
    {
        if (effectiveArmor <= 0.01f) return baseDamage * critMultiplier;

        CombatSettings s = CombatSettings.Instance;
        float ratio = penetration / effectiveArmor;
        float mult = 1f + s.damageRatioBonus * (ratio - 1f);
        mult = Mathf.Clamp(mult, 1f, s.damageCapMultiplier);
        return baseDamage * mult * critMultiplier;
    }

    // ---------------------------------------------------------------- 射界

    /// <summary>目标方位是否在射界内（相对舰艏的方位角，左右各 halfAngle）</summary>
    public static bool InFiringArc(float bearingFromBowDeg, float halfAngle = 150f)
    {
        float a = Mathf.Abs(Mathf.DeltaAngle(0f, bearingFromBowDeg));
        return a <= halfAngle;
    }
}
