using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 攻击范围可视化（线框版）
//
// 为什么用 LineRenderer 而不是网格：
//   网格在 HDRP 里做半透明总是出问题（灰白实心块 / z-fighting），
//   而 LineRenderer 用同一套材质在移动虚线上已经验证正常。
//
// 显示内容：
//   · 绿线 = 可瞄准的格子边框（射程内 + 射界内）
//   · 红线 = 当前攻击点
//   · 圆心默认在「移动指令的终点」（先移动再攻击），没移动时就在船身上
// ============================================================================
public class AttackRangeVisualizer : MonoBehaviour
{
    [Header("高度")]
    public float seaLevel = 10f;
    public float heightAboveSea = 1.0f;

    [Header("外观")]
    public Color rangeColor = new Color(0.35f, 1f, 0.55f, 1f);
    public Color aimColor = new Color(1f, 0.12f, 0.08f, 1f);   // 纯红、不透明
    public float lineWidth = 0.55f;
    [Tooltip("六边形边框缩进比例（1 = 正好贴格边）")]
    public float inset = 0.94f;

    [Header("射界")]
    [Tooltip("左右各多少度（150 = 正前正后各有 30° 盲区）")]
    public float halfArc = 150f;

    [Tooltip("攻击范围 = 移动范围 + 这个圈数")]
    public int extraRings = 2;

    HexGrid grid;
    Material rangeMat, aimMat;

    readonly List<LineRenderer> rangeLines = new List<LineRenderer>();

    Mesh aimMesh;
    MeshFilter aimFilter;
    MeshRenderer aimRenderer;
    readonly HashSet<Hex> validHexes = new HashSet<Hex>();

    void Awake()
    {
        grid = HexGrid.Instance != null ? HexGrid.Instance : FindObjectOfType<HexGrid>();
        rangeMat = CombatVisualUtil.CreateUnlit(rangeColor);
        aimMat = CombatVisualUtil.CreateUnlit(aimColor);

        // 攻击点：红色实心六边形（比线框醒目）
        aimMesh = new Mesh { name = "AttackAimFill" };
        GameObject go = new GameObject("AttackAimFill");
        // 不挂在本物体下面：顶点是世界坐标，挂了会被父物体的位置（MoveSystem 在 y=260）整体顶飞
        go.transform.SetParent(null);
        go.transform.position = Vector3.zero;
        aimFilter = go.AddComponent<MeshFilter>();
        aimFilter.sharedMesh = aimMesh;
        aimRenderer = go.AddComponent<MeshRenderer>();
        aimRenderer.sharedMaterial = aimMat;
        aimRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        aimRenderer.receiveShadows = false;
    }

    // ------------------------------------------------------------------ 对外

    /// <summary>显示攻击范围；anchor 是圆心（移动终点，或船当前位置）</summary>
    public void Show(ShipStats ship, Vector3 anchor)
    {
        if (ship == null || grid == null) { Hide(); return; }
        BuildRange(ship, anchor);
    }

    public void Hide()
    {
        validHexes.Clear();
        ClearLines(rangeLines);
        HideAim();
    }

    public void ShowAim(Hex hex)
    {
        if (grid == null || aimMesh == null) return;

        Vector3 c = grid.HexToWorld(hex);
        float r = grid.hexSize * inset;
        float y = seaLevel + heightAboveSea + 0.05f;

        Vector3[] verts = new Vector3[7];
        int[] tris = new int[18];

        verts[0] = new Vector3(c.x, y, c.z);
        for (int k = 0; k < 6; k++)
        {
            float a = Mathf.Deg2Rad * (60f * k);
            verts[1 + k] = new Vector3(c.x + Mathf.Cos(a) * r, y, c.z + Mathf.Sin(a) * r);
        }
        for (int k = 0; k < 6; k++)
        {
            tris[k * 3] = 0;
            tris[k * 3 + 1] = 1 + ((k + 1) % 6);
            tris[k * 3 + 2] = 1 + k;
        }

        aimMesh.Clear();
        aimMesh.vertices = verts;
        aimMesh.triangles = tris;
        aimMesh.RecalculateNormals();
        aimMesh.RecalculateBounds();
    }

    /// <summary>改攻击点颜色（能穿=红，穿不了=灰）</summary>
    public void SetAimColor(Color color)
    {
        if (aimMat == null) return;
        if (aimMat.HasProperty("_BaseColor")) aimMat.SetColor("_BaseColor", color);
        if (aimMat.HasProperty("_UnlitColor")) aimMat.SetColor("_UnlitColor", color);
        if (aimMat.HasProperty("_Color")) aimMat.SetColor("_Color", color);
    }

    public void HideAim()
    {
        if (aimMesh != null) aimMesh.Clear();
    }

    public bool IsValidAim(Hex hex)
    {
        return validHexes.Contains(hex);
    }

    public int ValidCount { get { return validHexes.Count; } }

    /// <summary>当前圆心的格子（用于判断要不要重建）</summary>
    public Hex AnchorHex { get; private set; }

    Vector3 lastForward;
    int lastMaxR;

    /// <summary>实时判定某格能不能作为攻击点（不依赖缓存，避免和显示不同步）</summary>
    public bool IsAimAllowed(Hex hex, out string reason)
    {
        reason = "";
        if (grid == null) { reason = "grid 为空"; return false; }

        int d = HexGrid.Distance(AnchorHex, hex);
        if (d < 1) { reason = "不能打自己脚下"; return false; }
        if (d > lastMaxR) { reason = "距离 " + d + " > 射程 " + lastMaxR; return false; }

        Vector3 to = grid.HexToWorld(hex) - grid.HexToWorld(AnchorHex);
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) { reason = "方向为零"; return false; }

        float bearing = Vector3.SignedAngle(lastForward, to, Vector3.up);
        if (Mathf.Abs(bearing) > halfArc)
        {
            reason = "方位 " + bearing.ToString("0") + "° 超出射界 ±" + halfArc + "°";
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ 构建

    void BuildRange(ShipStats ship, Vector3 anchor)
    {
        validHexes.Clear();
        ClearLines(rangeLines);

        Hex from = grid.WorldToHex(anchor);
        AnchorHex = from;

        ShipMover mv = ship.GetComponent<ShipMover>();
        int moveR = mv != null ? mv.moveRangeHexes : 3;
        int maxR = moveR + Mathf.Max(extraRings, 1);

        Vector3 forward = ship.transform.forward;
        lastForward = forward;
        lastMaxR = maxR;
        int used = 0;

        foreach (Hex h in grid.Range(from, maxR))
        {
            if (h == from) continue;

            int d = HexGrid.Distance(from, h);
            if (d > maxR || d < 1) continue;

            Vector3 to = grid.HexToWorld(h) - anchor;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) continue;

            float bearing = Vector3.SignedAngle(forward, to, Vector3.up);
            if (Mathf.Abs(bearing) > halfArc) continue;   // 正前 / 正后 30° 盲区

            validHexes.Add(h);
            DrawHex(rangeLines, h, rangeMat, lineWidth, used++);
        }
    }

    void DrawHex(List<LineRenderer> pool, Hex hex, Material mat, float width, int index = -1)
    {
        LineRenderer lr = GetLine(pool, index, mat, width);

        Vector3 c = grid.HexToWorld(hex);
        float r = grid.hexSize * inset;
        float y = seaLevel + heightAboveSea;

        lr.loop = true;
        lr.positionCount = 6;

        for (int k = 0; k < 6; k++)
        {
            float a = Mathf.Deg2Rad * (60f * k);
            lr.SetPosition(k, new Vector3(c.x + Mathf.Cos(a) * r, y, c.z + Mathf.Sin(a) * r));
        }
    }

    LineRenderer GetLine(List<LineRenderer> pool, int index, Material mat, float width)
    {
        if (index < 0) index = pool.Count;
        while (pool.Count <= index)
        {
            GameObject go = new GameObject("RangeLine");
            go.transform.SetParent(transform, false);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = 0;
            lr.material = mat;
            lr.startColor = rangeColor;
            lr.endColor = rangeColor;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCornerVertices = 2;
            lr.numCapVertices = 2;

            pool.Add(lr);
        }

        LineRenderer line = pool[index];
        line.widthMultiplier = width;
        line.material = mat;
        line.positionCount = 0;
        return line;
    }

    void ClearLines(List<LineRenderer> pool)
    {
        for (int i = 0; i < pool.Count; i++)
            if (pool[i] != null) pool[i].positionCount = 0;
    }
}