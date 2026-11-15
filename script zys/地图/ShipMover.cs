using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 船舰移动（二战舰船手感：只能前进、转弯有最小半径、加减速有惯性）
//
// 核心规则：
//   1. 不做直线位移，路径是「边转向边前进」模拟出来的弧线 —— 不可能原地掉头
//   2. 每回合最多移动 moveRangeHexes 格，一次移动消耗 1 点决策值
//   3. 转弯半径由 TurnRadiusWorld() 决定：数值越大越笨重
//
// 规划期： mover.GetReachableHexes()  取可到达的格子（给高亮用）
//          mover.TryBuildPath(hex, out path)  生成预览路径
// 演算期： mover.StartMove(path);  每帧 mover.Advance(dt);
// ============================================================================
public class ShipMover : MonoBehaviour
{
    [Header("依赖")]
    [Tooltip("场景里的六边形网格，不拖会自动找")]
    public HexGrid grid;

    [Header("移动能力（每艘船单独调）")]
    [Tooltip("每回合最多移动几格：驱逐舰 4、巡洋舰 3、战列舰 2、航母 1")]
    public int moveRangeHexes = 3;

    [Tooltip("巡航速度（格/秒）。1 = 每秒开过一格，跟地图缩放无关")]
    public float speedHexPerSecond = 1.5f;

    [Tooltip("起步加速度（格/秒²），越小越笨重")]
    public float accelHexPerSecond2 = 2f;

    [Tooltip("停船减速度（格/秒²）")]
    public float decelHexPerSecond2 = 3f;

    [Tooltip("最小转弯半径（世界单位）。越大越笨：驱逐舰 2~3 格，战列舰 5~6 格")]
    public float turnRadiusHex = 2f;

    [Tooltip("转弯时的速度折损：转弯中最高速度 = speedHexPerSecond × 这个系数")]
    [Range(0.3f, 1f)]
    public float turnSpeedFactor = 0.65f;

    [Header("决策值")]
    public int moveCost = 1;

    [Header("调试")]
    [Tooltip("勾上则 Update 里自动推进（不接演算期时单独测试用）")]
    public bool autoTick = true;

    [Tooltip("在 Scene 视图里画出当前路径")]
    public bool drawPathGizmo = true;

    // ------------------------------------------------------------------ 状态

    /// <summary>当前朝向（度）。0 = 东（+X），逆时针为正</summary>
    public float HeadingDegrees { get; private set; }

    public float CurrentSpeed { get; private set; }
    public bool IsMoving { get; private set; }

    /// <summary>本回合是否已安排移动（每回合一次）</summary>
    public bool HasMovedThisTurn { get; private set; }

    /// <summary>最近一次规划的路径（预览箭头、虚影用它）</summary>
    public List<Vector3> PlannedPath { get; private set; } = new List<Vector3>();

    public Hex CurrentHex
    {
        get { return grid != null ? grid.WorldToHex(transform.position) : new Hex(0, 0); }
    }

    List<Vector3> path = new List<Vector3>();
    int pathIndex;

    // ------------------------------------------------------------------ 生命周期

    void Awake()
    {
        if (grid == null) grid = HexGrid.Instance;
        HeadingDegrees = NormalizeAngle(90f - transform.eulerAngles.y);
    }

    void Start()
    {
        if (grid == null) grid = HexGrid.Instance;
    }

    void Update()
    {
        if (autoTick && IsMoving) Advance(Time.deltaTime);
    }

    // ------------------------------------------------------------------ 可达范围

    /// <summary>
    /// 本回合能开到的所有格子（已按移动力、地图边界、转弯条件筛过）。
    /// 高亮渲染直接消费这个列表。
    /// </summary>
    public List<Hex> GetReachableHexes()
    {
        List<Hex> result = new List<Hex>();
        if (grid == null) grid = HexGrid.Instance;
        if (grid == null) return result;

        Hex from = CurrentHex;

        // 只遍历半径内的格子，避免全图扫描
        foreach (Hex h in grid.Range(from, moveRangeHexes))
        {
            if (h == from) continue;                 // 原点不高亮

            // 先用六边形距离粗筛，省掉大量模拟
            if (HexGrid.Distance(from, h) > moveRangeHexes) continue;

            List<Vector3> p;
            if (TryBuildPath(h, out p)) result.Add(h);
        }
        return result;
    }

    // ------------------------------------------------------------------ 规划

    /// <summary>规划到目标格的路径（会写入 PlannedPath）</summary>
    public List<Vector3> PlanPath(Hex targetHex, out bool success)
    {
        List<Vector3> p;
        success = TryBuildPath(targetHex, out p);
        if (success) PlannedPath = p;
        return success ? p : null;
    }

    /// <summary>
    /// 纯计算：从当前位置/朝向出发，能否在移动力内开到目标格。
    /// 不改动 PlannedPath，可以被高亮逻辑反复调用。
    /// </summary>
    public bool TryBuildPath(Hex targetHex, out List<Vector3> result)
    {
        result = null;
        if (grid == null) grid = HexGrid.Instance;
        if (grid == null) return false;
        if (!grid.InBounds(targetHex)) return false;

        Hex from = CurrentHex;
        int hexSteps = HexGrid.Distance(from, targetHex);
        if (hexSteps > moveRangeHexes) return false;

        Vector3 targetPos = grid.HexToWorld(targetHex);
        targetPos.y = transform.position.y;

        bool ok;
        List<Vector3> candidate = SimulateApproach(targetPos, out ok);
        if (!ok || candidate == null || candidate.Count < 2) return false;

        // 绕行上限：直线距离 × 3 + 一个完整掉头弧长（放宽，否则近处背后目标会被误判为不可达）
        float spacing = HexSpacing();
        float allow = hexSteps * spacing * 4f + 2f * Mathf.PI * Mathf.Max(TurnRadiusWorld(), 0.1f);
        if (PathLength(candidate) > allow) return false;

        result = candidate;
        return true;
    }

    /// <summary>边转向边前进的模拟：每步最大转角 = 速度 / 转弯半径 × dt</summary>
    List<Vector3> SimulateApproach(Vector3 targetPos, out bool success)
    {
        success = false;
        List<Vector3> pts = new List<Vector3>();

        Vector3 pos = transform.position; pos.y = 0f;
        Vector3 target = targetPos; target.y = 0f;

        float heading = HeadingDegrees * Mathf.Deg2Rad;
        const float speed = 1f;
        const float step = 0.05f;

        // 自适应转弯半径：远处用大弧（笨重），近处收紧（否则开不到隔壁格子）
        float straight = Vector3.Distance(transform.position, targetPos);
        float radius = Mathf.Min(TurnRadiusWorld(),
                                 Mathf.Max(straight * 0.8f, HexSpacing() * 0.35f));

        float maxTravel = moveRangeHexes * HexSpacing() * 4f
                        + 2f * Mathf.PI * Mathf.Max(radius, 0.1f)
                        + HexSpacing() * 2f;
        float travelled = 0f;

        pts.Add(new Vector3(pos.x, transform.position.y, pos.z));

        for (int i = 0; i < 20000; i++)
        {
            Vector3 delta = target - pos; delta.y = 0f;
            if (delta.magnitude <= 0.05f) { success = true; break; }

            float desired = Mathf.Atan2(delta.z, delta.x) * Mathf.Rad2Deg;
            float diff = Mathf.DeltaAngle(heading * Mathf.Rad2Deg, desired);
            float maxTurnRate = speed / Mathf.Max(radius, 0.1f);
            float turn = Mathf.Clamp(diff * Mathf.Deg2Rad, -maxTurnRate * step, maxTurnRate * step);

            heading += turn;
            pos += new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading)) * speed * step;
            travelled += speed * step;

            pts.Add(new Vector3(pos.x, transform.position.y, pos.z));
            if (travelled > maxTravel) break;
        }

        // 没精确到达时给个容差：船头离目标格中心半格以内就算到位
        if (!success)
        {
            Vector3 d = target - pos; d.y = 0f;
            if (d.magnitude <= HexSpacing() * 0.45f) success = true;
        }

        return Decimate(pts, 0.4f);
    }

    // ------------------------------------------------------------------ 执行

    public void StartMove(List<Vector3> newPath)
    {
        if (newPath == null || newPath.Count < 2) return;

        path = new List<Vector3>(newPath);
        pathIndex = 1;
        IsMoving = true;
        HasMovedThisTurn = true;
        CurrentSpeed = Mathf.Min(CurrentSpeed, speedHexPerSecond * HexSpacing() * 0.3f);
    }

    public void Advance(float dt)
    {
        if (!IsMoving || pathIndex >= path.Count)
        {
            IsMoving = false;
            return;
        }

        float remaining = RemainingDistance();

        // speedHexPerSecond / accelHexPerSecond2 / decelHexPerSecond2 的单位都是「格/秒」，这里换算成世界单位
        float spacing = HexSpacing();
        float vMax = speedHexPerSecond * spacing;
        float aAcc = accelHexPerSecond2 * spacing;
        float aDec = decelHexPerSecond2 * spacing;

        float targetSpeed = vMax;

        float turnDelta = Mathf.Abs(Mathf.DeltaAngle(HeadingDegrees, HeadingToNextPoint()));
        if (turnDelta > 5f) targetSpeed *= turnSpeedFactor;

        if (aDec > 0.01f)
        {
            float brakeSpeed = Mathf.Sqrt(2f * aDec * Mathf.Max(remaining, 0.001f));
            targetSpeed = Mathf.Min(targetSpeed, brakeSpeed);
        }

        float rate = (targetSpeed > CurrentSpeed) ? aAcc : aDec;
        CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetSpeed, rate * dt);

        Vector3 next = path[pathIndex];
        Vector3 toNext = next - transform.position; toNext.y = 0f;
        float stepLen = CurrentSpeed * dt;

        if (toNext.magnitude <= stepLen || toNext.magnitude < 0.05f)
        {
            transform.position = new Vector3(next.x, transform.position.y, next.z);
            pathIndex++;
        }
        else
        {
            transform.position += toNext.normalized * stepLen;
        }

        float desiredHeading = HeadingToNextPoint();
        float maxYaw = Mathf.Max(CurrentSpeed, 0.5f) / Mathf.Max(TurnRadiusWorld(), 0.1f) * Mathf.Rad2Deg;
        HeadingDegrees = Mathf.MoveTowardsAngle(HeadingDegrees, desiredHeading, maxYaw * dt);
        transform.rotation = Quaternion.Euler(0f, 90f - HeadingDegrees, 0f);

        if (pathIndex >= path.Count)
        {
            IsMoving = false;
            CurrentSpeed = 0f;
        }
    }

    public void ResetTurnFlag()
    {
        HasMovedThisTurn = false;
    }

    // ------------------------------------------------------------------ 工具

    /// <summary>转弯半径换算成世界单位 = 格数 × 格间距</summary>
    float TurnRadiusWorld()
    {
        return Mathf.Max(turnRadiusHex, 0.05f) * HexSpacing();
    }
    float HexSpacing()
    {
        return grid != null ? grid.hexSize * Mathf.Sqrt(3f) : 1f;
    }

    float HeadingToNextPoint()
    {
        if (pathIndex >= path.Count) return HeadingDegrees;
        Vector3 d = path[pathIndex] - transform.position; d.y = 0f;
        if (d.sqrMagnitude < 0.0001f) return HeadingDegrees;
        return Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
    }

    float RemainingDistance()
    {
        if (pathIndex >= path.Count) return 0f;
        float sum = Vector3.Distance(transform.position, path[pathIndex]);
        for (int i = pathIndex; i < path.Count - 1; i++)
            sum += Vector3.Distance(path[i], path[i + 1]);
        return sum;
    }

    static float PathLength(List<Vector3> pts)
    {
        float sum = 0f;
        for (int i = 0; i < pts.Count - 1; i++) sum += Vector3.Distance(pts[i], pts[i + 1]);
        return sum;
    }

    static List<Vector3> Decimate(List<Vector3> pts, float minGap)
    {
        List<Vector3> outPts = new List<Vector3>();
        if (pts.Count == 0) return outPts;

        outPts.Add(pts[0]);
        Vector3 last = pts[0];
        for (int i = 1; i < pts.Count; i++)
        {
            if (Vector3.Distance(pts[i], last) >= minGap)
            {
                outPts.Add(pts[i]);
                last = pts[i];
            }
        }
        if (outPts[outPts.Count - 1] != pts[pts.Count - 1]) outPts.Add(pts[pts.Count - 1]);
        return outPts;
    }

    static float NormalizeAngle(float a)
    {
        a %= 360f;
        if (a < 0f) a += 360f;
        return a;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawPathGizmo || PlannedPath == null || PlannedPath.Count < 2) return;
        Gizmos.color = new Color(1f, 0.85f, 0.25f, 0.9f);
        for (int i = 0; i < PlannedPath.Count - 1; i++)
            Gizmos.DrawLine(PlannedPath[i], PlannedPath[i + 1]);
    }
}
