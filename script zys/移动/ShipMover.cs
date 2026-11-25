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
    /// 结果按「所在格 + 朝向」缓存，避免每次点船都重算导致卡顿。
    /// </summary>
    public List<Hex> GetReachableHexes()
    {
        if (grid == null) grid = HexGrid.Instance;

        Hex from = CurrentHex;
        int headingKey = Mathf.RoundToInt(HeadingDegrees);

        if (cacheList != null && cacheOrigin == from && cacheHeading == headingKey)
            return cacheList;

        List<Hex> result = new List<Hex>(24);
        if (grid != null)
        {
            // 只遍历半径内的格子，避免全图扫描
            foreach (Hex h in grid.Range(from, moveRangeHexes))
            {
                if (h == from) continue;                 // 原点不高亮
                if (HexGrid.Distance(from, h) > moveRangeHexes) continue;

                if (CanReach(h)) result.Add(h);          // 轻量判定，不生成路径
            }
        }

        cacheList = result;
        cacheOrigin = from;
        cacheHeading = headingKey;
        return result;
    }

    /// <summary>位置或朝向变了，缓存作废</summary>
    public void InvalidateReachableCache()
    {
        cacheList = null;
    }

    List<Hex> cacheList;
    Hex cacheOrigin;
    int cacheHeading = int.MinValue;

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
    /// 轻量版可达性判定：只判断"能不能到"，不生成路径点、不分配 List。
    /// 高亮范围时逐格调用它（比 TryBuildPath 快很多，不产生 GC）。
    /// </summary>
    public bool CanReach(Hex targetHex)
    {
        if (grid == null) grid = HexGrid.Instance;
        if (grid == null || !grid.InBounds(targetHex)) return false;

        Hex from = CurrentHex;
        if (HexGrid.Distance(from, targetHex) > moveRangeHexes) return false;

        Vector3 targetPos = grid.HexToWorld(targetHex);
        targetPos.y = transform.position.y;

        return CanReachCore(targetPos);
    }

    bool CanReachCore(Vector3 targetPos)
    {
        Vector3 pos = transform.position; pos.y = 0f;
        Vector3 target = targetPos; target.y = 0f;

        float spacing = HexSpacing();
        float direct = Vector3.Distance(pos, target);
        float arrive = spacing * 0.45f;

        if (direct <= arrive) return true;

        // 转弯半径：目标越近收得越紧，避免"目标落在转弯圆里"导致永远绕圈
        float radius = Mathf.Min(TurnRadiusWorld(),
                                 Mathf.Max(direct * 0.45f, spacing * 0.3f));
        radius = Mathf.Max(radius, 0.1f);

        const float step = 0.15f;
        float heading = HeadingDegrees * Mathf.Deg2Rad;
        float travelled = 0f;
        float travelCap = direct * 3f + 2f * Mathf.PI * radius;

        for (int i = 0; i < 4000; i++)
        {
            Vector3 d = target - pos; d.y = 0f;
            if (d.sqrMagnitude <= arrive * arrive) return true;

            float desired = Mathf.Atan2(d.z, d.x);
            float diffDeg = Mathf.DeltaAngle(heading * Mathf.Rad2Deg, desired * Mathf.Rad2Deg);

            // 方向偏得越多越慢：慢下来才拐得过来，不会绕圈
            float align = Mathf.Clamp01(Mathf.Cos(diffDeg * Mathf.Deg2Rad));
            float speed = Mathf.Lerp(0.2f, 1f, align);

            float maxTurn = speed / radius * step;             // 每步最大转角
            heading += Mathf.Clamp(diffDeg * Mathf.Deg2Rad, -maxTurn, maxTurn);

            pos += new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading)) * speed * step;
            travelled += speed * step;
            if (travelled > travelCap) return false;           // 绕太远，判不可达
        }
        return false;
    }

    /// <summary>
    /// 纯计算：从当前位置/朝向出发，能否在移动力内开到目标格。
    /// 不改动 PlannedPath，可以被高亮逻辑反复调用。
    /// </summary>
    public bool TryBuildPath(Hex targetHex, out List<Vector3> result)
    {
        result = null;

        // 关键：先过和高亮完全相同的判定 —— 高亮说能到，这里就必须能走
        if (!CanReach(targetHex)) return false;

        if (grid == null) grid = HexGrid.Instance;
        Vector3 targetPos = grid.HexToWorld(targetHex);
        targetPos.y = transform.position.y;

        bool ok;
        List<Vector3> candidate = SimulateApproach(targetPos, out ok);
        if (candidate == null || candidate.Count < 2) return false;

        // 终点强制吸附到格心：虚影必须和六边形中心点重合
        Vector3 endPos = candidate[candidate.Count - 1];
        if (Vector3.Distance(endPos, targetPos) > 0.01f)
        {
            // 最后一段用一个短补间过渡，避免视觉上"跳"过去
            candidate.Add(targetPos);
        }
        else
        {
            candidate[candidate.Count - 1] = targetPos;
        }

        result = candidate;
        return true;
    }

    /// <summary>边转向边前进的模拟：方向偏得多就减速，慢下来才拐得过来</summary>
    List<Vector3> SimulateApproach(Vector3 targetPos, out bool success)
    {
        success = false;
        List<Vector3> pts = new List<Vector3>();

        Vector3 pos = transform.position; pos.y = 0f;
        Vector3 target = targetPos; target.y = 0f;

        float heading = HeadingDegrees * Mathf.Deg2Rad;
        const float step = 0.05f;

        float spacing = HexSpacing();
        float direct = Vector3.Distance(pos, target);
        float arrive = spacing * 0.45f;

        // 与 CanReachCore 完全一致的半径规则
        float radius = Mathf.Max(Mathf.Min(TurnRadiusWorld(),
                                 Mathf.Max(direct * 0.45f, spacing * 0.3f)), 0.1f);

        float travelCap = direct * 3f + 2f * Mathf.PI * radius;
        float travelled = 0f;

        pts.Add(new Vector3(pos.x, transform.position.y, pos.z));

        // 最坏情况：绕大圈也要走完，步数按 travelCap 估
        int maxSteps = Mathf.Min(20000, Mathf.CeilToInt(travelCap / (step * 0.2f)) + 100);

        for (int i = 0; i < maxSteps; i++)
        {
            Vector3 delta = target - pos; delta.y = 0f;
            if (delta.sqrMagnitude <= arrive * arrive) { success = true; break; }

            float desiredDeg = Mathf.Atan2(delta.z, delta.x) * Mathf.Rad2Deg;
            float diffDeg = Mathf.DeltaAngle(heading * Mathf.Rad2Deg, desiredDeg);

            float align = Mathf.Clamp01(Mathf.Cos(diffDeg * Mathf.Deg2Rad));
            float speed = Mathf.Lerp(0.2f, 1f, align);

            float maxTurn = speed / radius * step;
            heading += Mathf.Clamp(diffDeg * Mathf.Deg2Rad, -maxTurn, maxTurn);

            pos += new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading)) * speed * step;
            travelled += speed * step;

            pts.Add(new Vector3(pos.x, transform.position.y, pos.z));
            if (travelled > travelCap) break;
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
        InvalidateReachableCache();
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
        // 轮机破损 → 航速下降
        ShipDebuffs debuff = GetComponent<ShipDebuffs>();
        float speedMul = debuff != null ? debuff.SpeedMultiplier : 1f;

        float vMax = speedHexPerSecond * spacing * speedMul;
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
            InvalidateReachableCache();
        }
    }

    public void ResetTurnFlag()
    {
        HasMovedThisTurn = false;
    }

    /// <summary>规划期下达移动指令时标记：本回合这艘船已经用掉了移动</summary>
    public void MarkMovedThisTurn()
    {
        HasMovedThisTurn = true;
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
