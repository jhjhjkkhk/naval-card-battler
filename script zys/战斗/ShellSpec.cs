using System;
using UnityEngine;

// ============================================================================
// 炮弹类型：同一门炮换弹种，穿深 / 伤害 / debuff 概率都不同
//
// 一期先做两种：
//   穿甲弹 AP：穿深高、伤害高、点火率低
//   高爆弹 HE：穿深低、伤害中等、点火/漏水率高
// ============================================================================
[Serializable]
public class ShellSpec
{
    public enum ShellType { AP, HE }

    public ShellType type = ShellType.AP;
    public string displayName = "穿甲弹";

    [Tooltip("穿深倍率：最终穿深 = 舰炮基础穿深 × 这个系数")]
    public float penetrationScale = 1f;

    [Tooltip("伤害倍率：最终伤害 = 舰炮基础伤害 × 这个系数")]
    public float damageScale = 1f;

    [Tooltip("转正角（度）：一期固定 0")]
    public float normalization = 0f;

    [Range(0f, 1f)] public float critChance = 0.15f;
    public float critMultiplier = 1.5f;

    [Tooltip("部位对应的 debuff 概率：甲板 / 舰桥 / 水线")]
    public float fireChanceDeck = 0.25f;
    public float fireChanceBridge = 0.10f;
    public float floodChanceWaterline = 0.30f;
    public float floodChanceNearMiss = 0.10f;

    // ---------------------------------------------------------------- 预设

    public static ShellSpec AP()
    {
        return new ShellSpec
        {
            type = ShellType.AP,
            displayName = "穿甲弹",
            penetrationScale = 1f,
            damageScale = 1f,
            normalization = 0f,
            critChance = 0.15f,
            critMultiplier = 1.5f,
            fireChanceDeck = 0.15f,
            fireChanceBridge = 0.05f,
            floodChanceWaterline = 0.30f,
            floodChanceNearMiss = 0.10f
        };
    }

    public static ShellSpec HE()
    {
        return new ShellSpec
        {
            type = ShellType.HE,
            displayName = "高爆弹",
            penetrationScale = 0.35f,
            damageScale = 0.7f,
            normalization = 0f,
            critChance = 0.08f,
            critMultiplier = 1.3f,
            fireChanceDeck = 0.40f,
            fireChanceBridge = 0.20f,
            floodChanceWaterline = 0.15f,
            floodChanceNearMiss = 0.15f
        };
    }

    public string Describe()
    {
        return displayName + "（穿深×" + penetrationScale.ToString("0.##")
             + " 伤害×" + damageScale.ToString("0.##") + "）";
    }
}
