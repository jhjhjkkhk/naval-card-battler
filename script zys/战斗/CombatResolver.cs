using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 齐射结算：把「命中 → 部位 → 跳弹 → 穿深 → 伤害 → debuff」串起来
//
// 关键设计：
//   · 纯函数式，不改动任何船只状态（伤害由调用方扣）
//   · 随机数由外部传入（System.Random），同一颗种子 → 同样的结果
//     → 演算期之后要"1:1 复刻"，这个必须做到
// ============================================================================
public static class CombatResolver
{
    /// <summary>
    /// 结算一次齐射。
    /// </summary>
    /// <param name="attacker">开火的船</param>
    /// <param name="target">被打的船</param>
    /// <param name="shellDir">炮弹飞行方向（水平）</param>
    /// <param name="hullForward">目标船艏朝向（水平）</param>
    /// <param name="distanceHex">命中瞬间的距离（格）</param>
    /// <param name="shell">弹种</param>
    /// <param name="rng">随机源（同一 seed 可复现）</param>
    public static SalvoResult ResolveSalvo(
        ShipStats attacker,
        ShipStats target,
        Vector3 shellDir,
        Vector3 hullForward,
        float distanceHex,
        ShellSpec shell,
        System.Random rng)
    {
        SalvoResult salvo = new SalvoResult();
        if (attacker == null || target == null || shell == null || rng == null) return salvo;

        int barrels = attacker.TotalBarrels;
        float hitChance = CombatMath.HitChance(distanceHex, attacker.dispersionPerHex);
        int shells = CombatMath.ShellsHit(barrels, hitChance);

        float nearMissChance = CombatMath.NearMissChance(distanceHex, attacker.maxRangeHex);

        // 该距离的穿深（同一次齐射里所有炮弹相同）
        float penetration = CombatMath.Penetration(
            attacker.basePenetration * shell.penetrationScale,
            distanceHex, attacker.maxRangeHex);

        float impactAngle = CombatMath.ImpactAngle(shellDir, hullForward);

        for (int i = 0; i < shells; i++)
        {
            ShotResult shot = new ShotResult();
            shot.impactAngle = impactAngle;
            shot.penetration = penetration;

            // ---------- 部位 ----------
            shot.zone = CombatMath.RollZone(nearMissChance, (float)rng.NextDouble());

            // ---------- 近失 ----------
            if (shot.zone == HitZone.NearMiss)
            {
                shot.outcome = ShotOutcome.NearMiss;
                shot.effectiveArmor = 0f;
                shot.damage = target.maxHp * CombatSettings.Instance.nearMissSplashRatio;      // 固定 1% 溅射

                if ((float)rng.NextDouble() < shell.floodChanceNearMiss)
                    shot.debuff = DebuffType.Flooding;

                salvo.shots.Add(shot);
                continue;
            }

            // ---------- 装甲与跳弹 ----------
            shot.armorThickness = CombatMath.ArmorForZone(target, shot.zone);
            shot.effectiveArmor = CombatMath.EffectiveArmor(
                shot.armorThickness, impactAngle, shell.normalization);

            bool overmatch = CombatMath.IsOvermatch(penetration, shot.armorThickness);

            if (!overmatch)
            {
                float ricochetChance = CombatMath.RicochetChance(impactAngle);
                if (ricochetChance > 0f && (float)rng.NextDouble() < ricochetChance)
                {
                    shot.outcome = ShotOutcome.Ricochet;
                    shot.damage = 0f;
                    salvo.shots.Add(shot);
                    continue;
                }
            }

            // ---------- 穿深判定 ----------
            if (!overmatch && penetration < shot.effectiveArmor)
            {
                shot.outcome = ShotOutcome.NoPenetration;
                shot.damage = 0f;
                salvo.shots.Add(shot);
                continue;
            }

            shot.outcome = overmatch ? ShotOutcome.Overmatch : ShotOutcome.Penetration;

            // ---------- 伤害 ----------
            bool critical = (float)rng.NextDouble() < shell.critChance;
            shot.critical = critical;

            float critMult = critical ? shell.critMultiplier : 1f;
            shot.damage = CombatMath.Damage(
                attacker.baseDamage * shell.damageScale,
                penetration, shot.effectiveArmor, critMult);

            // ---------- debuff ----------
            shot.debuff = SelectDebuff(shot.zone, shell, rng);
            if (critical && shot.debuff == DebuffType.None)
                shot.debuff = SelectDebuff(shot.zone, shell, rng);   // 暴击必带一个

            salvo.shots.Add(shot);
        }

        salvo.Accumulate();
        return salvo;
    }

    /// <summary>
    /// 单发结算（落点制）：已经知道落点落在哪艘船上，只判这一发。
    /// 返回 null 表示落空（附近没有船）。
    /// </summary>
    public static ShotResult? ResolveSingleShell(
        ShipStats attacker,
        ShipStats target,
        Vector3 shellDir,
        Vector3 hullForward,
        float distanceHex,
        ShellSpec shell,
        System.Random rng)
    {
        if (attacker == null || target == null || shell == null || rng == null) return null;

        ShotResult shot = new ShotResult();
        shot.impactAngle = CombatMath.ImpactAngle(shellDir, hullForward);
        shot.penetration = CombatMath.Penetration(
            attacker.basePenetration * shell.penetrationScale,
            distanceHex, attacker.maxRangeHex);

        // 近失：落点在船旁边的格子
        shot.zone = HitZone.NearMiss;
        shot.outcome = ShotOutcome.NearMiss;
        shot.damage = target.maxHp * CombatSettings.Instance.nearMissSplashRatio;
        if ((float)rng.NextDouble() < shell.floodChanceNearMiss)
            shot.debuff = DebuffType.Flooding;
        return shot;
    }

    /// <summary>
    /// 单发命中结算（落点正好在船上）：判部位 / 跳弹 / 穿深 / 伤害 / debuff
    /// </summary>
    public static ShotResult ResolveHit(
        ShipStats attacker,
        ShipStats target,
        Vector3 shellDir,
        Vector3 hullForward,
        float distanceHex,
        ShellSpec shell,
        System.Random rng)
    {
        ShotResult shot = new ShotResult();
        shot.impactAngle = CombatMath.ImpactAngle(shellDir, hullForward);
        shot.penetration = CombatMath.Penetration(
            attacker.basePenetration * shell.penetrationScale,
            distanceHex, attacker.maxRangeHex);

        // 部位：按距离决定近失率，但这里已经确定打中船体，只在三个部位里选
        float nm = CombatMath.NearMissChance(distanceHex, attacker.maxRangeHex);
        float roll = nm + (float)rng.NextDouble() * (1f - nm);
        shot.zone = CombatMath.RollZone(nm, roll);
        if (shot.zone == HitZone.NearMiss) shot.zone = HitZone.Waterline;

        shot.armorThickness = CombatMath.ArmorForZone(target, shot.zone);
        shot.effectiveArmor = CombatMath.EffectiveArmor(
            shot.armorThickness, shot.impactAngle, shell.normalization);

        bool overmatch = CombatMath.IsOvermatch(shot.penetration, shot.armorThickness);

        if (!overmatch)
        {
            float ricochet = CombatMath.RicochetChance(shot.impactAngle);
            if (ricochet > 0f && (float)rng.NextDouble() < ricochet)
            {
                shot.outcome = ShotOutcome.Ricochet;
                return shot;
            }
        }

        if (!overmatch && shot.penetration < shot.effectiveArmor)
        {
            shot.outcome = ShotOutcome.NoPenetration;
            return shot;
        }

        shot.outcome = overmatch ? ShotOutcome.Overmatch : ShotOutcome.Penetration;

        bool critical = (float)rng.NextDouble() < shell.critChance;
        shot.critical = critical;
        float mult = critical ? shell.critMultiplier : 1f;
        shot.damage = CombatMath.Damage(
            attacker.baseDamage * shell.damageScale,
            shot.penetration, shot.effectiveArmor, mult);

        shot.debuff = SelectDebuff(shot.zone, shell, rng);
        if (critical && shot.debuff == DebuffType.None)
            shot.debuff = SelectDebuff(shot.zone, shell, rng);

        return shot;
    }

    /// <summary>按命中部位和弹种挑 debuff</summary>
    static DebuffType SelectDebuff(HitZone zone, ShellSpec shell, System.Random rng)
    {
        CombatSettings cfg = CombatSettings.Instance;
        float r = (float)rng.NextDouble();

        switch (zone)
        {
            case HitZone.Deck:
                if (r < shell.fireChanceDeck) return DebuffType.Fire;
                return DebuffType.None;

            case HitZone.Bridge:
                if (r < shell.fireChanceBridge) return DebuffType.Fire;
                if (r < shell.fireChanceBridge + cfg.bridgeGunChance) return DebuffType.Gun;
                return DebuffType.None;

            case HitZone.Waterline:
                if (r < shell.floodChanceWaterline) return DebuffType.Flooding;
                if (r < shell.floodChanceWaterline + cfg.waterlineEngineChance) return DebuffType.Engine;
                if (r < shell.floodChanceWaterline + cfg.waterlineEngineChance + cfg.waterlineMagazineChance) return DebuffType.Magazine;
                return DebuffType.None;

            default:
                return DebuffType.None;
        }
    }
}
