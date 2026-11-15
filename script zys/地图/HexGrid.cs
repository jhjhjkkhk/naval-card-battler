using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 六边形网格（平顶 flat-top）
//
// 视觉：地图由一个个平顶六边形拼成
// 逻辑：每个六边形中心有一个"点"，船舰站在点上
//
// 坐标系：轴坐标 axial (q, r)，平顶朝向的邻居是 6 个方向：
//        东 (+1,0)  东北 (+1,-1)  西北 (0,-1)
//        西 (-1,0)  西南 (-1,+1)  东南 (0,+1)
//
// 用法：把本脚本挂在一个空物体上，调 mapWidth / mapHeight / hexSize，
//       场景里就能看到网格线。其他脚本通过 HexGrid.Instance 访问。
// ============================================================================
public class HexGrid : MonoBehaviour
{
    [Header("地图尺寸")]
    [Tooltip("横向格数")]
    public int mapWidth = 20;
    [Tooltip("纵向格数")]
    public int mapHeight = 20;
    [Tooltip("六边形外接圆半径（中心到顶点）；相邻两格中心距离 = hexSize * √3")]
    public float hexSize = 3f;

    [Header("调试")]
    [Tooltip("在 Scene 视图里画出网格线（默认关闭，避免和可达范围高亮混在一起）")]
    public bool showGizmos = false;

    // 平顶六边形的 6 个方向（下标 0~5 就是朝向索引）
    public static readonly Hex[] Directions =
    {
        new Hex( 1,  0),   // 0 东
        new Hex( 1, -1),   // 1 东北
        new Hex( 0, -1),   // 2 西北
        new Hex(-1,  0),   // 3 西
        new Hex(-1,  1),   // 4 西南
        new Hex( 0,  1),   // 5 东南
    };

    public static HexGrid Instance { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    // ------------------------------------------------------------------ 坐标转换

    /// <summary>格子中心的世界坐标</summary>
    public Vector3 HexToWorld(Hex h)
    {
        Vector3 local = new Vector3(
            hexSize * 1.5f * h.q,
            0f,
            hexSize * Mathf.Sqrt(3f) * (h.r + h.q * 0.5f));
        return transform.TransformPoint(local);
    }

    /// <summary>世界坐标落在哪个格子里</summary>
    public Hex WorldToHex(Vector3 world)
    {
        Vector3 p = transform.InverseTransformPoint(world);
        float q = (2f / 3f * p.x) / hexSize;
        float r = (-1f / 3f * p.x + Mathf.Sqrt(3f) / 3f * p.z) / hexSize;
        return RoundToHex(q, r);
    }

    /// <summary>浮点轴坐标 → 最近的整数格（立方坐标四舍五入）</summary>
    public static Hex RoundToHex(float q, float r)
    {
        float s = -q - r;
        int rq = Mathf.RoundToInt(q);
        int rr = Mathf.RoundToInt(r);
        int rs = Mathf.RoundToInt(s);

        float dq = Mathf.Abs(rq - q);
        float dr = Mathf.Abs(rr - r);
        float ds = Mathf.Abs(rs - s);

        if (dq > dr && dq > ds) rq = -rr - rs;
        else if (dr > ds)       rr = -rq - rs;

        return new Hex(rq, rr);
    }

    // ------------------------------------------------------------------ 查询

    /// <summary>是否在地图范围内（矩形地图，q ∈ [0,width)，r ∈ [0,height)）</summary>
    public bool InBounds(Hex h)
    {
        return h.q >= 0 && h.q < mapWidth && h.r >= 0 && h.r < mapHeight;
    }

    /// <summary>第 i 个邻居（i = 0~5）</summary>
    public static Hex Neighbor(Hex h, int direction)
    {
        return h + Directions[((direction % 6) + 6) % 6];
    }

    /// <summary>6 个邻居（越界的不返回）</summary>
    public IEnumerable<Hex> Neighbors(Hex h)
    {
        for (int i = 0; i < 6; i++)
        {
            Hex n = Neighbor(h, i);
            if (InBounds(n)) yield return n;
        }
    }

    /// <summary>两格之间的步数（六边形距离）</summary>
    public static int Distance(Hex a, Hex b)
    {
        int dq = a.q - b.q;
        int dr = a.r - b.r;
        int ds = a.s - b.s;
        return (Mathf.Abs(dq) + Mathf.Abs(dr) + Mathf.Abs(ds)) / 2;
    }

    /// <summary>以 center 为中心、半径 radius 的一圈格子</summary>
    public IEnumerable<Hex> Ring(Hex center, int radius)
    {
        if (radius <= 0) { yield return center; yield break; }

        Hex h = center + Directions[4] * radius;
        for (int i = 0; i < 6; i++)
        {
            for (int j = 0; j < radius; j++)
            {
                if (InBounds(h)) yield return h;
                h = Neighbor(h, i);
            }
        }
    }

    /// <summary>半径 radius 内的所有格子（含 center）</summary>
    public IEnumerable<Hex> Range(Hex center, int radius)
    {
        for (int radius_i = 0; radius_i <= radius; radius_i++)
            foreach (Hex h in Ring(center, radius_i))
                yield return h;
    }

    /// <summary>地图上所有格子</summary>
    public IEnumerable<Hex> AllHexes()
    {
        for (int q = 0; q < mapWidth; q++)
            for (int r = 0; r < mapHeight; r++)
                yield return new Hex(q, r);
    }

    // ------------------------------------------------------------------ 朝向

    /// <summary>方向格 → 世界空间水平方向单位向量</summary>
    public Vector3 DirectionToWorld(Hex dir)
    {
        Vector3 a = HexToWorld(dir);
        Vector3 b = HexToWorld(new Hex(0, 0));
        return (a - b).normalized;
    }

    /// <summary>世界方向向量 → 最接近的朝向索引 0~5</summary>
    public int HeadingFromVector(Vector3 v)
    {
        if (v.sqrMagnitude < 0.0001f) return 0;
        v.y = 0f;
        v.Normalize();

        int best = 0;
        float bestDot = float.NegativeInfinity;
        for (int i = 0; i < 6; i++)
        {
            float dot = Vector3.Dot(v, DirectionToWorld(Directions[i]));
            if (dot > bestDot) { bestDot = dot; best = i; }
        }
        return best;
    }

    /// <summary>从 from 到 to 的朝向索引</summary>
    public int HeadingTo(Hex from, Hex to)
    {
        return HeadingFromVector(HexToWorld(to) - HexToWorld(from));
    }

    // ------------------------------------------------------------------ 调试绘制

    void OnDrawGizmos()
    {
        if (!showGizmos) return;

        Gizmos.color = new Color(0.2f, 0.9f, 0.9f, 0.25f);

        for (int q = 0; q < mapWidth; q++)
        {
            for (int r = 0; r < mapHeight; r++)
            {
                Vector3 c = HexToWorld(new Hex(q, r));
                Vector3 prev = Vector3.zero;
                for (int i = 0; i <= 6; i++)
                {
                    // 平顶六边形：顶点在 0°、60°、120°…（顶点朝 +X）
                    float angle = Mathf.Deg2Rad * (60f * i);
                    Vector3 p = c + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * hexSize;
                    if (i > 0) Gizmos.DrawLine(prev, p);
                    prev = p;
                }
            }
        }
    }
}

// ============================================================================
// 格子结构体：轴坐标 (q, r)
// ============================================================================
[Serializable]
public struct Hex : IEquatable<Hex>
{
    public int q;
    public int r;

    public Hex(int q, int r)
    {
        this.q = q;
        this.r = r;
    }

    public int s { get { return -q - r; } }

    public static Hex operator +(Hex a, Hex b) { return new Hex(a.q + b.q, a.r + b.r); }
    public static Hex operator -(Hex a, Hex b) { return new Hex(a.q - b.q, a.r - b.r); }
    public static Hex operator *(Hex a, int k) { return new Hex(a.q * k, a.r * k); }

    public static bool operator ==(Hex a, Hex b) { return a.q == b.q && a.r == b.r; }
    public static bool operator !=(Hex a, Hex b) { return !(a == b); }

    public bool Equals(Hex other) { return q == other.q && r == other.r; }
    public override bool Equals(object obj) { return obj is Hex && Equals((Hex)obj); }
    public override int GetHashCode() { return (q * 397) ^ r; }
    public override string ToString() { return "(" + q + "," + r + ")"; }
}
