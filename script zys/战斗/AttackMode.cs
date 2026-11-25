using UnityEngine;

// ============================================================================
// 攻击模式（按 F 进入 / 退出）
//
// 规则（已确认）：
//   · 先选中一艘我方船，再按 F
//   · 显示绿色可瞄准格（线框）；范围圆心 = 移动指令的终点（先移动再攻击）
//   · 点绿格 → 红色攻击点，登记到演算
//   · 再点别的绿格 → 换攻击点（不重复扣决策值）
//   · F 退出：红线隐藏，指令保留
//   · 演算期自动退出
// ============================================================================
public class AttackMode : MonoBehaviour
{
    [Header("按键")]
    public KeyCode toggleKey = KeyCode.F;

    [Header("弹种")]
    public ShellSpec.ShellType shellType = ShellSpec.ShellType.AP;

    [Header("引用（留空自动找）")]
    public AttackRangeVisualizer visualizer;
    public MoveReplayDirector director;
    public HexGrid grid;
    public MovementRangeVisualizer moveVisualizer;
    public HexHoverPreview preview;

    public static bool IsActive { get; private set; }

    ShipStats attacker;

    // 每艘船各自的攻击点（换船不串）
    readonly System.Collections.Generic.Dictionary<ShipStats, Hex> aims
        = new System.Collections.Generic.Dictionary<ShipStats, Hex>();

    Hex aimHex;
    bool hasAim;

    Hex lastAnchorHex;

    void Start()
    {
        if (grid == null) grid = HexGrid.Instance != null ? HexGrid.Instance : FindObjectOfType<HexGrid>();
        if (director == null) director = MoveReplayDirector.Ensure();
        if (moveVisualizer == null) moveVisualizer = FindObjectOfType<MovementRangeVisualizer>();
        if (preview == null) preview = FindObjectOfType<HexHoverPreview>();
        if (preview == null) preview = gameObject.AddComponent<HexHoverPreview>();
        if (visualizer == null) visualizer = FindObjectOfType<AttackRangeVisualizer>();
        if (visualizer == null)
        {
            visualizer = gameObject.AddComponent<AttackRangeVisualizer>();
            Debug.Log("[攻击] 自动添加 AttackRangeVisualizer");
        }
    }

    bool lastPlanning = true;

    void Update()
    {
        bool planning = director == null || director.IsPlanning;
        if (planning && !lastPlanning) ResetTurn();     // 新回合：清掉上一轮的红格
        lastPlanning = planning;

        if (director != null && !director.IsPlanning)
        {
            if (IsActive) Exit();
            return;
        }

        if (Input.GetKeyDown(toggleKey)) Toggle();
        if (!IsActive) return;

        RefreshAnchor();
        UpdatePreview();

        if (Input.GetMouseButtonDown(0))
        {
            bool overUI = CombatVisualUtil.PointerOverInteractiveUI();
            Debug.Log("[攻击] 收到左键，overUI=" + overUI + "，有效格数=" + (visualizer != null ? visualizer.ValidCount : -1));
            if (overUI) return;
            TrySetAim();
        }
    }

    void Toggle()
    {
        if (IsActive) { Exit(); return; }

        ShipStats selected = ShipSelector.Selected;
        if (selected == null) { Debug.Log("[攻击] 先点一艘我方船再按 F"); return; }
        if (selected.isEnemy) { Debug.Log("[攻击] 不能指挥敌方船"); return; }

        attacker = selected;
        IsActive = true;

        hasAim = aims.ContainsKey(attacker);
        if (hasAim) aims.TryGetValue(attacker, out aimHex);
        lastAnchorHex = new Hex(-9999, -9999);

        // 同一时间只显示一种范围：进入攻击模式就关掉移动范围
        if (moveVisualizer != null) moveVisualizer.HideAll();

        RefreshAnchor(true);

        if (hasAim) visualizer.ShowAim(aimHex);
        else visualizer.HideAim();

        Debug.Log("[攻击] 进入攻击模式：" + attacker.shipName + "，弹种 " + shellType);
    }

    void Exit()
    {
        IsActive = false;
        if (visualizer != null) visualizer.Hide();
        if (preview != null) preview.ClearAttackContext();

        // 恢复移动范围（船还是选中的，只是换了种提示）
        if (moveVisualizer != null)
        {
            ShipMover mv = ShipSelector.Selected != null ? ShipSelector.Selected.GetComponent<ShipMover>() : null;
            if (mv != null) moveVisualizer.SetTarget(mv);
        }

        Debug.Log("[攻击] 退出攻击模式");
    }

    /// <summary>范围圆心：有移动指令就用移动终点，否则用船当前位置</summary>
    void RefreshAnchor(bool force = false)
    {
        if (attacker == null || grid == null) return;

        Vector3 anchor = attacker.transform.position;
        if (director != null)
        {
            ShipMover mv = attacker.GetComponent<ShipMover>();
            if (mv != null)
            {
                Vector3 end;
                if (director.TryGetPlannedEnd(mv, out end)) anchor = end;
            }
        }

        Hex anchorHex = grid.WorldToHex(anchor);
        if (!force && anchorHex == lastAnchorHex && visualizer.IsValidAim(aimHex)) return;

        lastAnchorHex = anchorHex;
        visualizer.Show(attacker, anchor);

        // 圆心变了，原来的攻击点可能已经不在范围内
        if (hasAim)
        {
            if (visualizer.IsValidAim(aimHex)) visualizer.ShowAim(aimHex);
            else
            {
                hasAim = false;
                visualizer.HideAim();
                Debug.Log("[攻击] 移动后原攻击点不在范围内，已清除");
            }
        }
    }

    bool CanPenetrate(Hex hex, ShellSpec shell)
    {
        if (grid == null || attacker == null) return true;

        foreach (ShipStats s in FindObjectsOfType<ShipStats>())
        {
            if (s == null || s == attacker || s.isEnemy == attacker.isEnemy) continue;
            if (s.currentHp <= 0) continue;
            if (grid.WorldToHex(s.transform.position) != hex) continue;

            int dist = HexGrid.Distance(grid.WorldToHex(attacker.transform.position), hex);
            float pen = CombatMath.Penetration(attacker.basePenetration * shell.penetrationScale,
                                               dist, attacker.maxRangeHex);

            Vector3 dir = s.transform.position - attacker.transform.position;
            float angle = CombatMath.ImpactAngle(dir, s.transform.forward);
            float eff = CombatMath.EffectiveArmor(s.armorBelt, angle, shell.normalization);

            return pen >= eff || CombatMath.IsOvermatch(pen, s.armorBelt);
        }
        return true;   // 空海面按红色处理
    }

    void UpdatePreview()
    {
        if (preview == null || attacker == null) return;

        Vector3 anchorPos = attacker.transform.position;
        ShipMover mv = attacker.GetComponent<ShipMover>();
        if (director != null && mv != null)
        {
            Vector3 end;
            if (director.TryGetPlannedEnd(mv, out end)) anchorPos = end;
        }

        ShellSpec shell = shellType == ShellSpec.ShellType.AP ? ShellSpec.AP() : ShellSpec.HE();
        preview.SetAttackContext(attacker, anchorPos, shell, visualizer);
    }

    void TrySetAim()
    {
        if (grid == null || attacker == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        // 优先用物理射线（和移动点击一致）：直接打到海面/船体上
        Vector3 hitPoint;
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 10000f))
        {
            hitPoint = hit.point;
        }
        else
        {
            // 兜底：用数学平面，高度取海面
            float planeY = visualizer != null ? visualizer.seaLevel : 10f;
            Plane plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            float dist;
            if (!plane.Raycast(ray, out dist)) return;
            hitPoint = ray.GetPoint(dist);
        }

        Hex hex = grid.WorldToHex(hitPoint);

        string reason;
        bool allowed = visualizer.IsAimAllowed(hex, out reason);
        if (!allowed)
        {
            Debug.Log("[攻击] 点中 " + hex + "（世界 " + hitPoint.ToString("0.0") + "）被拒：" + reason
                      + " | 圆心 " + visualizer.AnchorHex);
            return;
        }

        Debug.Log("[攻击] 点中 " + hex + " → 有效，标记为攻击点");

        // 本回合第一次给这艘船标点才扣决策值，换点不重复扣
        bool firstForShip = !aims.ContainsKey(attacker);
        if (firstForShip)
        {
            TurnManager tm = TurnManager.Instance;
            if (tm != null)
            {
                if (!tm.IsPlayerTurn) { Debug.Log("[攻击] 现在不是我方回合"); return; }
                if (!tm.TrySpendDecision(tm.attackDecisionCost))
                {
                    Debug.Log("[攻击] 决策值不足（剩 " + tm.DecisionPoints + "），无法攻击");
                    return;
                }
            }
        }

        aimHex = hex;
        hasAim = true;
        aims[attacker] = hex;

        visualizer.ShowAim(hex);

        ShellSpec shell = shellType == ShellSpec.ShellType.AP ? ShellSpec.AP() : ShellSpec.HE();

        // 能击穿 = 红色，穿不了 = 灰色，一眼看出这炮有没有用
        visualizer.SetAimColor(CanPenetrate(hex, shell) ? new Color(1f, 0.12f, 0.08f, 1f)
                                                        : new Color(0.55f, 0.55f, 0.55f, 1f));
        director.ReplaceAttack(attacker, hex, shell);

        Debug.Log("[攻击] 标记攻击点 " + hex + "（" + shell.Describe() + "）");
    }

    public void ResetTurn()
    {
        if (visualizer != null) visualizer.HideAim();
        aims.Clear();
        hasAim = false;
        aimHex = default(Hex);
        lastAnchorHex = new Hex(-9999, -9999);
    }
}