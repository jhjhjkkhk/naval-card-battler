using UnityEngine;

// ============================================================================
// 落点散布：把「瞄准格」变成一片弹着区
//
// 规则（已确认）：
//   半径 R = 距离 × 0.02 格（上限 1.2 格，下限 0.05 格）
//   分布用高斯（中心密、边缘疏），而不是均匀洒
//   同一轮齐射，每发炮弹独立取一个落点
// ============================================================================
public static class SalvoCalculator
{
    /// <summary>散布半径（世界单位）</summary>
    /// <param name="hexSpacingWorld">一格中心到相邻格中心的世界距离（hexSize × √3）</param>
    /// <param name="baseHex">基础散布半径（格）</param>
    /// <param name="perHex">每 1 格距离额外增加多少散布（格）</param>
    /// <param name="maxHex">散布上限（格）</param>
    public static float ScatterRadiusWorld(float distanceHex, float hexSpacingWorld,
                                           float baseHex = 0.45f, float perHex = 0.08f, float maxHex = 2f)
    {
        float rHex = Mathf.Clamp(baseHex + distanceHex * perHex, 0.05f, maxHex);
        return rHex * hexSpacingWorld;
    }

    /// <summary>
    /// 取一个落点：以瞄准点为中心，按高斯分布散开。
    /// 用 Box-Muller 生成正态分布，长度再夹到 R 以内。
    /// </summary>
    public static Vector3 LandingPoint(Vector3 aimCenter, float radiusWorld, System.Random rng)
    {
        if (radiusWorld <= 0.001f) return aimCenter;

        // Box-Muller：两个均匀分布 → 一个标准正态
        double u1 = 1.0 - rng.NextDouble();     // (0,1]
        double u2 = rng.NextDouble();
        double mag = System.Math.Sqrt(-2.0 * System.Math.Log(u1));
        float gx = (float)(mag * System.Math.Cos(2.0 * System.Math.PI * u2));
        float gy = (float)(mag * System.Math.Sin(2.0 * System.Math.PI * u2));

        // 正态分布约 68% 落在 1σ 内，这里把 σ 设为 R/2，再裁到 R
        Vector3 offset = new Vector3(gx, 0f, gy) * (radiusWorld * 0.5f);
        if (offset.magnitude > radiusWorld)
            offset = offset.normalized * radiusWorld;

        return aimCenter + offset;
    }
}
