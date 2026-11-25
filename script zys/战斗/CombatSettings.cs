using UnityEngine;

// ============================================================================
// 战斗数值总表（所有写死在代码里的常量都搬到这里，可在 Inspector 调）
//
// 用法：场景里建个空物体，挂上本组件，改数值即可。
// 没挂也不会崩：会自动创建一个使用默认值的隐藏实例。
// ============================================================================
public class CombatSettings : MonoBehaviour
{
    static CombatSettings instance;
    static CombatSettings fallback;

    public static CombatSettings Instance
    {
        get
        {
            if (instance != null) return instance;

            // 场景里找一次
            instance = FindObjectOfType<CombatSettings>();
            if (instance != null) return instance;

            // 还找不到就用隐藏的默认实例
            if (fallback == null)
            {
                GameObject go = new GameObject("CombatSettings (defaults)");
                fallback = go.AddComponent<CombatSettings>();
                fallback.hideFlags = HideFlags.HideAndDontSave;
            }
            return fallback;
        }
    }

    void Awake()
    {
        if (instance == null) instance = this;
    }

    // ------------------------------------------------------------------ 跳弹与穿深

    [Header("跳弹")]
    [Tooltip("小于这个角度强制跳弹（度）")]
    public float ricochetMinAngle = 30f;

    [Tooltip("大于这个角度必定不跳弹（度）")]
    public float ricochetMaxAngle = 45f;

    [Header("碾压")]
    [Tooltip("穿深 ≥ 这个倍数 × 装甲 → 无视跳弹强制击穿")]
    public float overmatchFactor = 3f;

    [Header("穿深衰减")]
    [Tooltip("在最大射程处损失多少穿深（0.6 = 剩 40%）")]
    public float penetrationDecay = 0.6f;

    // ------------------------------------------------------------------ 伤害

    [Header("伤害")]
    [Tooltip("穿深/等效装甲 每超出 1 点，伤害 +30%")]
    public float damageRatioBonus = 0.3f;

    [Tooltip("伤害上限 = 基础伤害 × 这个倍数")]
    public float damageCapMultiplier = 2f;

    // ------------------------------------------------------------------ 部位与近失

    [Header("部位概率")]
    [Tooltip("近失基础率")]
    public float nearMissBase = 0.10f;

    [Tooltip("距离每达到最大射程的 100%，近失率额外 +这个值")]
    public float nearMissPerRange = 0.20f;

    [Tooltip("近失率上限")]
    public float nearMissMax = 0.90f;

    [Tooltip("甲板权重")]
    public float zoneWeightDeck = 2f;

    [Tooltip("舰桥权重")]
    public float zoneWeightBridge = 3f;

    [Tooltip("水线权重")]
    public float zoneWeightWaterline = 3f;

    [Header("近失")]
    [Tooltip("近失溅射 = 目标最大血 × 这个比例")]
    public float nearMissSplashRatio = 0.01f;

    // ------------------------------------------------------------------ debuff 概率（部位决定）

    [Header("debuff 概率（按部位）")]
    public float bridgeGunChance = 0.25f;
    public float waterlineEngineChance = 0.15f;
    public float waterlineMagazineChance = 0.08f;

    [Tooltip("弹药库殉爆概率")]
    public float magazineDetonateChance = 0.10f;

    // ------------------------------------------------------------------ debuff 效果

    [Header("debuff 效果")]
    [Tooltip("debuff 持续回合")]
    public int debuffTurns = 4;

    [Tooltip("起火：每回合扣最大血的百分比")]
    public float fireDamagePerTurn = 0.05f;

    [Tooltip("漏水：每回合扣最大血的百分比")]
    public float floodDamagePerTurn = 0.03f;

    [Tooltip("持续伤害最低值")]
    public int minDotDamage = 1;

    [Tooltip("轮机破损：航速倍率")]
    public float engineSpeedMultiplier = 0.6f;

    [Tooltip("炮机受损：散布倍率")]
    public float gunScatterMultiplier = 1.3f;

    // ------------------------------------------------------------------ 沉没

    [Header("沉没表现")]
    public float sinkDuration = 3.5f;
    public float sinkDepth = 14f;
    public float sinkTiltDegrees = 35f;

    // ------------------------------------------------------------------ 特效

    [Header("特效")]
    [Tooltip("一轮齐射里同类特效最多几个")]
    public int maxFxPerSalvo = 2;
}