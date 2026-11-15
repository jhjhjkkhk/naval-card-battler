using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 移动测试驱动（也可以当作临时版的"规划期操作入口"）
//
// 零配置用法：
//   场景里建一个空物体 → 挂上本脚本 → 运行 → 点地面，船就按弧线开过去。
//
// 它会自动：
//   1. 找 HexGrid（场景里没有就自动建一个）
//   2. 给场景里所有带 ShipStats 的船自动补上 ShipMover
//   3. 点击船 = 选中；点击水面 = 让当前选中的船开过去
//   4. 打印失败原因（超出移动力 / 被地图挡住 / 需要绕太远）
// ============================================================================
public class ShipMoveTester : MonoBehaviour
{
    [Header("引用（留空全自动）")]
    public ShipMover mover;
    public HexGrid grid;
    public LineRenderer line;

    [Header("自动建网格（场景里没有 HexGrid 时）")]
    public bool autoCreateGrid = true;
    public int mapWidth = 20;
    public int mapHeight = 20;
    public float hexSize = 3f;

    [Header("外观")]
    public float lineWidth = 0.15f;
    public Color lineColor = new Color(1f, 0.85f, 0.25f, 0.9f);

    readonly List<ShipMover> allMovers = new List<ShipMover>();

    void Start()
    {
        SetupGrid();
        SetupShips();
        SetupLine();
        EnsureVisualizer();
    }

    /// <summary>场景里没有高亮组件就自动补一个，省得手动挂</summary>
    void EnsureVisualizer()
    {
        if (FindObjectOfType<MovementRangeVisualizer>() != null) return;
        MovementRangeVisualizer v = gameObject.AddComponent<MovementRangeVisualizer>();
        Debug.Log("[移动] 自动添加 MovementRangeVisualizer");
        if (mover != null) v.target = mover;
    }

    void SetupGrid()
    {
        if (grid == null) grid = HexGrid.Instance;
        if (grid == null) grid = FindObjectOfType<HexGrid>();

        if (grid == null && autoCreateGrid)
        {
            GameObject go = new GameObject("HexGrid (auto)");
            grid = go.AddComponent<HexGrid>();
            grid.mapWidth = mapWidth;
            grid.mapHeight = mapHeight;
            grid.hexSize = hexSize;
            Debug.Log("[移动] 场景里没有 HexGrid，已自动创建，map=" + mapWidth + "x" + mapHeight + " hexSize=" + hexSize);
        }
    }

    /// <summary>船没有碰撞体时补一个包围盒，保证鼠标能点中</summary>
    static void EnsureCollider(GameObject go)
    {
        if (go.GetComponent<Collider>() != null) return;

        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;

        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);

        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.center = go.transform.InverseTransformPoint(b.center);
        bc.size = b.size;
    }

    void SetupShips()
    {
        allMovers.Clear();

        // 给所有 ShipStats 自动补 ShipMover
        foreach (ShipStats stats in FindObjectsOfType<ShipStats>())
        {
            ShipMover m = stats.GetComponent<ShipMover>();
            if (m == null) m = stats.gameObject.AddComponent<ShipMover>();
            if (m.grid == null) m.grid = grid;

            EnsureCollider(stats.gameObject);   // 没有碰撞体就补一个，否则点不中船
            allMovers.Add(m);
        }

        // 场景里直接挂了 ShipMover 的也收进来
        foreach (ShipMover m in FindObjectsOfType<ShipMover>())
        {
            if (!allMovers.Contains(m))
            {
                if (m.grid == null) m.grid = grid;
                allMovers.Add(m);
            }
        }

        if (mover == null && allMovers.Count > 0) mover = allMovers[0];

        Debug.Log("[移动] 找到 " + allMovers.Count + " 艘船" + (mover != null ? "，当前控制：" + mover.name : "（没有船，请确认 ShipStats 挂在船模型上）"));
    }

    void SetupLine()
    {
        if (line != null) return;

        GameObject go = new GameObject("MovePathLine");
        go.transform.SetParent(transform, false);
        line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = lineWidth;
        line.positionCount = 0;

        Shader sh = Shader.Find("HDRP/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        Material mat = new Material(sh);

        if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 1f);
        if (mat.HasProperty("_BlendMode")) mat.SetFloat("_BlendMode", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", lineColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", lineColor);
        mat.renderQueue = 3000;

        line.material = mat;
        line.startColor = lineColor;
        line.endColor = lineColor;
    }

    void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        if (grid == null) return;

        Camera cam = Camera.main;
        if (cam == null) { Debug.LogWarning("[移动] 场景里没有 MainCamera 标签的相机"); return; }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        // 先看有没有点到船（点船 = 选中）
        // 用 RaycastAll：海水/地形可能挡在前面，要穿过它们只认带 ShipMover 的物体
        RaycastHit[] hits = Physics.RaycastAll(ray, 5000f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        bool hitAnyCollider = hits.Length > 0;
        foreach (RaycastHit h in hits)
        {
            ShipMover hitMover = h.collider.GetComponentInParent<ShipMover>();
            if (hitMover == null) continue;

            mover = hitMover;
            MovementRangeVisualizer v = FindObjectOfType<MovementRangeVisualizer>();
            if (v != null) v.SetTarget(mover);
            Debug.Log("[移动] 选中 " + mover.name + "（命中 " + h.collider.name + "）");
            return;
        }

        if (hitAnyCollider)
            Debug.Log("[移动] 点到 " + hits[0].collider.name + "，它身上没有 ShipMover；本次按「点水面」处理");

        // 点水面 = 移动
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        float dist;
        if (!plane.Raycast(ray, out dist)) return;

        Vector3 point = ray.GetPoint(dist);
        Hex target = grid.WorldToHex(point);

        if (mover == null) { Debug.LogWarning("[移动] 没有可控制的船"); return; }

        bool ok;
        List<Vector3> path = mover.PlanPath(target, out ok);

        if (!ok || path == null)
        {
            Vector3 center = grid.HexToWorld(target);
            int steps = HexGrid.Distance(mover.CurrentHex, target);
            Debug.Log("[移动] 到不了 " + target + "（相距 " + steps + " 格，本船最多 " + mover.moveRangeHexes + " 格，或转弯绕不过去）");
            line.positionCount = 0;
            return;
        }

        DrawPath(path);
        mover.StartMove(path);
        Debug.Log("[移动] 前往 " + target + "，路径 " + path.Count + " 个点");
    }

    void DrawPath(List<Vector3> pts)
    {
        line.positionCount = pts.Count;
        for (int i = 0; i < pts.Count; i++)
            line.SetPosition(i, pts[i] + Vector3.up * 0.15f);
    }
}
