using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ============================================================================
// 规划期输入：选中 / 取消选中 / 下达移动指令
//
// 交互（按设计确认）：
//   左键点船        → 选中，显示绿色可达范围
//   左键点可达格    → 立刻确定目的地：出现「虚影 + 虚线」，并登记到演算时间轴
//   右键            → 取消选中，绿色范围消失（虚影和虚线保留）
//   演算期          → 完全锁输入，虚影/虚线隐藏；UI 信息牌仍可点击查看
//
// 注意：移动不在规划期执行，只在回合结束后的演算期按时间戳复刻。
// ============================================================================
public class ShipMoveTester : MonoBehaviour
{
    [Header("引用（留空全自动）")]
    public ShipMover mover;
    public HexGrid grid;
    public MoveReplayDirector director;
    public MovePreview preview;
    public MovementRangeVisualizer visualizer;

    [Header("自动建网格（场景里没有 HexGrid 时）")]
    public bool autoCreateGrid = true;
    public int mapWidth = 20;
    public int mapHeight = 20;
    public float hexSize = 3f;

    [Header("海面高度（不可达提示用）")]
    public float seaLevel = 10f;

    [Header("规划期")]
    [Tooltip("规划期时长；没有 TurnManager 时由这里控制")]
    public float planningSeconds = 60f;
    [Tooltip("按 T 键可提前结束规划期（测试用）")]
    public bool debugEndPlanningKey = true;

    readonly List<ShipMover> allMovers = new List<ShipMover>();

    // ------------------------------------------------------------------ 启动

    void Start()
    {
        SetupGrid();
        SetupShips();
        SetupHelpers();
        Debug.Log("[移动] 初始化完成，船数 " + allMovers.Count);
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
            Debug.Log("[移动] 自动创建 HexGrid");
        }
    }

    void SetupShips()
    {
        allMovers.Clear();

        foreach (ShipStats stats in FindObjectsOfType<ShipStats>())
        {
            // UI 里的船卡也挂了 ShipStats，绝不能给它加 ShipMover / Collider，
            // 否则克隆虚影时会把 UI 卡牌复制成一个巨大的色块
            if (stats.GetComponentInParent<Canvas>() != null) continue;

            ShipMover m = stats.GetComponent<ShipMover>();
            if (m == null) m = stats.gameObject.AddComponent<ShipMover>();
            if (m.grid == null) m.grid = grid;
            EnsureCollider(stats.gameObject);
            allMovers.Add(m);
        }

        foreach (ShipMover m in FindObjectsOfType<ShipMover>())
        {
            if (m.GetComponentInParent<Canvas>() != null) continue;
            if (!allMovers.Contains(m))
            {
                if (m.grid == null) m.grid = grid;
                allMovers.Add(m);
            }
        }
    }

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

    void SetupHelpers()
    {
        // 没有演算调度器就自动建一个：保证「移动只在演算期执行」，不会点哪走哪
        director = MoveReplayDirector.Ensure();
        director.planningSeconds = planningSeconds;

        if (visualizer == null) visualizer = FindObjectOfType<MovementRangeVisualizer>();
        if (visualizer == null)
        {
            visualizer = gameObject.AddComponent<MovementRangeVisualizer>();
            Debug.Log("[移动] 自动添加 MovementRangeVisualizer");
        }
        visualizer.seaLevel = seaLevel;

        if (preview == null) preview = FindObjectOfType<MovePreview>();
        if (preview == null)
        {
            preview = gameObject.AddComponent<MovePreview>();
            Debug.Log("[移动] 自动添加 MovePreview");
        }
        preview.seaLevel = seaLevel;
    }

    // ------------------------------------------------------------------ 输入

    void Update()
    {
        if (grid == null) return;

        // 测试用：按 T 提前结束规划期
        if (debugEndPlanningKey && director != null && director.IsPlanning && Input.GetKeyDown(KeyCode.T))
        {
            Debug.Log("[移动] 手动结束规划期");
            director.BeginReplay();
        }

        bool replaying = director != null && !director.IsPlanning;

        // 演算期：不显示预览，也不允许选物体
        if (replaying)
        {
            if (preview != null) preview.Hide();
            if (visualizer != null) visualizer.HideAll();
            mover = null;
            return;
        }

        // 只拦「真正能交互」的 UI（按钮 / 卡牌 / 信息牌），
        // 背景图、装饰图不拦——否则信息牌一展开就把海面全挡住，选不了第二艘船
        if (PointerOverInteractiveUI())
        {
            if (Input.GetMouseButtonDown(0)) Debug.Log("[移动] 点击落在 UI 上，忽略");
            return;
        }

        if (Input.GetMouseButtonDown(0)) OnLeftClick();
        if (Input.GetMouseButtonDown(1)) Deselect();
    }

    static readonly List<RaycastResult> uiHits = new List<RaycastResult>();

    static bool PointerOverInteractiveUI()
    {
        if (EventSystem.current == null) return false;

        PointerEventData ped = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        uiHits.Clear();
        EventSystem.current.RaycastAll(ped, uiHits);

        for (int i = 0; i < uiHits.Count; i++)
        {
            GameObject go = uiHits[i].gameObject;
            if (go == null) continue;

            if (go.GetComponentInParent<Selectable>() != null) return true;

            MonoBehaviour[] mbs = go.GetComponentsInParent<MonoBehaviour>();
            for (int j = 0; j < mbs.Length; j++)
                if (mbs[j] is IPointerClickHandler) return true;
        }
        return false;
    }

    void OnLeftClick()
    {
        Camera cam = Camera.main;
        if (cam == null) { Debug.LogWarning("[移动] 没有 MainCamera"); return; }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        // 1) 先看有没有点到船（穿透水面等遮挡物）
        RaycastHit[] hits = Physics.RaycastAll(ray, 5000f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit h in hits)
        {
            ShipMover hitMover = h.collider.GetComponentInParent<ShipMover>();
            if (hitMover == null) continue;
            Select(hitMover);
            return;
        }

        // 2) 点水面 → 给当前选中的船下达移动指令
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        float dist;
        if (!plane.Raycast(ray, out dist)) return;

        if (mover == null) return;                       // 没选船，点水面不做事
        if (mover.IsMoving) return;                      // 正在航行

        Hex target = grid.WorldToHex(ray.GetPoint(dist));

        bool ok;
        List<Vector3> path = mover.PlanPath(target, out ok);
        if (!ok || path == null)
        {
            Debug.Log("[移动] " + target + " 到不了（超出移动力或绕不过去）");
            return;
        }

        // 本回合已经给这艘船下过指令 → 只改目标，不再扣决策值
        bool alreadyOrdered = mover.HasMovedThisTurn;

        if (!alreadyOrdered)
        {
            TurnManager tm = TurnManager.Instance;
            if (tm != null)
            {
                if (!tm.IsPlayerTurn)
                {
                    Debug.Log("[移动] 现在不是我方回合");
                    return;
                }
                if (!tm.TrySpendDecision(tm.moveDecisionCost))
                {
                    Debug.Log("[移动] 决策值不足（剩 " + tm.DecisionPoints + "），无法移动");
                    return;
                }
            }
            mover.MarkMovedThisTurn();
        }

        // 登记到演算时间轴（同一艘船只保留最后一次指令）
        if (director != null) director.ReplaceOrder(mover, path);
        else mover.StartMove(path);   // 没有导演器时退化成立即移动（单测用）

        if (preview != null) preview.Show(mover, path);

        Debug.Log(alreadyOrdered
            ? "[移动] 修改目 " + target + "（不额外消耗决策值）"
            : "[移动] 下达指令 " + target + "，消耗 " + (TurnManager.Instance != null ? TurnManager.Instance.moveDecisionCost : 0) + " 点决策值");
    }

    void Select(ShipMover m)
    {
        mover = m;
        if (visualizer != null) visualizer.SetTarget(m);

        // 点船体时，让原有的船只选择系统同步选中（信息牌会展开）
        ShipStats stats = m.GetComponent<ShipStats>();
        if (stats != null) ShipSelector.SelectShip(stats);

        Debug.Log("[移动] 选中 " + m.name);
    }

    void Deselect()
    {
        if (mover == null) return;
        Debug.Log("[移动] 取消选中 " + mover.name);
        mover = null;
        if (visualizer != null) visualizer.HideAll();
        // 注意：虚影和虚线保留 —— 指令已经下过了
    }

    /// <summary>点到了去不了的格子：红色六边形闪一下</summary>
    void ShowInvalid(Hex target)
    {
        if (grid == null) return;

        Vector3 c = grid.HexToWorld(target);
        GameObject go = new GameObject("InvalidHex");
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.widthMultiplier = 0.4f;
        lr.material = CreateUnlitDecalMaterial(new Color(1f, 0.3f, 0.25f, 0.9f));
        lr.startColor = new Color(1f, 0.3f, 0.25f, 0.9f);
        lr.endColor = lr.startColor;
        lr.positionCount = 6;

        float y = seaLevel + 0.6f;
        float size = grid.hexSize * 0.85f;
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Deg2Rad * (60f * i);
            lr.SetPosition(i, new Vector3(c.x + Mathf.Cos(a) * size, y, c.z + Mathf.Sin(a) * size));
        }

        Destroy(go, 1.2f);
    }

    static Material CreateUnlitDecalMaterial(Color color)
    {
        Shader sh = Shader.Find("HDRP/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        Material mat = new Material(sh) { name = "InvalidHexMat" };
        if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 1f);
        if (mat.HasProperty("_BlendMode")) mat.SetFloat("_BlendMode", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        mat.renderQueue = 3000;
        return mat;
    }
}
