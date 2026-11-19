using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 演算期动作录制与回放（移动部分）
//
// 时间规则（按设计确认）：
//   · 规划期最多 60 秒，倒计时归零或玩家点「回合结束」进入演算
//   · 演算时间轴的 0 点 = 玩家【第一次消耗决策值】的时刻
//     → 看牌、思考的时间不算进演算，避免开头一大段空白
//   · 演算结束条件 = 所有指令都已执行 + 所有船都停止移动
//     （以后攻击也接进同一个判断：所有炮弹都落地）
// ============================================================================
public class MoveReplayDirector : MonoBehaviour
{
    public static MoveReplayDirector Instance { get; private set; }

    [Header("时间轴")]
    [Tooltip("规划期时长（秒）；到点自动演算")]
    public float planningSeconds = 60f;

    [Tooltip("勾上则规划期到点自动开演算；关掉则由回合结束按钮 / 倒计时调用")]
    public bool autoEndByTime = false;

    [Tooltip("勾上：时间轴从第一次消耗决策值开始计时；关掉：从回合开始计时")]
    public bool startClockOnFirstOrder = true;

    [Tooltip("演算最长时长，防止卡死")]
    public float maxReplaySeconds = 120f;

    [Tooltip("演算结束回调")]
    public System.Action onReplayFinished;

    // ---------------------------------------------------------------- 状态

    public bool IsPlanning { get; private set; } = true;
    public bool IsReplaying { get; private set; }

    /// <summary>规划期计时（第一次下单后才开始走）</summary>
    public float PlanningClock { get; private set; }

    /// <summary>演算期计时</summary>
    public float ReplayClock { get; private set; }

    bool clockStarted;

    readonly List<MoveOrder> orders = new List<MoveOrder>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[演算] 场景里有多个 MoveReplayDirector，保留 " + Instance.name);
            enabled = false;
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static MoveReplayDirector Ensure()
    {
        if (Instance != null) return Instance;

        MoveReplayDirector found = FindObjectOfType<MoveReplayDirector>();
        if (found != null) return found;

        GameObject go = new GameObject("MoveReplayDirector (auto)");
        MoveReplayDirector d = go.AddComponent<MoveReplayDirector>();
        Debug.Log("[演算] 自动创建 MoveReplayDirector");
        return d;
    }

    // ---------------------------------------------------------------- 录制

    /// <summary>登记一条移动指令（规划期调用）。同一艘船重复下达时只保留最后一次。</summary>
    public void Register(ShipMover mover, List<Vector3> path)
    {
        if (mover == null || path == null || path.Count < 2) return;

        // 第一次下单 → 时间轴从这里开始
        if (startClockOnFirstOrder && !clockStarted)
        {
            clockStarted = true;
            PlanningClock = 0f;
        }

        MoveOrder order = new MoveOrder
        {
            time = PlanningClock,
            mover = mover,
            path = new List<Vector3>(path),
            started = false
        };
        orders.Add(order);

        Debug.Log("[演算] 登记移动 t=" + PlanningClock.ToString("0.0") + "s  船=" + mover.name
                  + "  路径点=" + path.Count);
    }

    public void ReplaceOrder(ShipMover mover, List<Vector3> path)
    {
        if (mover == null || path == null || path.Count < 2) return;

        for (int i = orders.Count - 1; i >= 0; i--)
            if (orders[i].mover == mover) orders.RemoveAt(i);

        Register(mover, path);
    }

    // ---------------------------------------------------------------- 回放

    public void BeginReplay()
    {
        if (!IsPlanning) return;

        IsPlanning = false;
        IsReplaying = true;
        ReplayClock = 0f;

        for (int i = 0; i < orders.Count; i++) orders[i].started = false;

        Debug.Log("[演算] 开始回放，共 " + orders.Count + " 条移动指令");
    }

    public void EndReplay()
    {
        if (!IsReplaying) return;

        IsReplaying = false;
        Debug.Log("[演算] 回放结束");

        if (onReplayFinished != null) onReplayFinished();
    }

    void Update()
    {
        // ---------- 规划期 ----------
        if (IsPlanning)
        {
            if (clockStarted) PlanningClock += Time.deltaTime;
            if (autoEndByTime && PlanningClock >= planningSeconds) BeginReplay();
            return;
        }

        if (!IsReplaying) return;

        // ---------- 演算期 ----------
        ReplayClock += Time.deltaTime;

        // 1) 到点就执行还没执行的指令
        for (int i = 0; i < orders.Count; i++)
        {
            MoveOrder o = orders[i];
            if (o.started || o.time > ReplayClock) continue;

            o.started = true;
            if (o.mover != null) o.mover.StartMove(o.path);
            Debug.Log("[演算] t=" + ReplayClock.ToString("0.0") + "s 执行 " + o.mover.name);
        }

        // 2) 结束条件：指令全执行完 + 所有船都停下
        bool allStarted = true;
        bool anyMoving = false;

        for (int i = 0; i < orders.Count; i++)
        {
            MoveOrder o = orders[i];
            if (!o.started) { allStarted = false; continue; }
            if (o.mover != null && o.mover.IsMoving) anyMoving = true;
        }

        if (allStarted && !anyMoving) { EndReplay(); return; }

        if (ReplayClock > maxReplaySeconds)
        {
            Debug.LogWarning("[演算] 超过 " + maxReplaySeconds + " 秒，强制结束");
            EndReplay();
        }
    }

    /// <summary>新回合：清空指令，回到规划期，时间轴归零</summary>
    public void ClearOrders()
    {
        orders.Clear();
        IsPlanning = true;
        IsReplaying = false;
        clockStarted = false;
        PlanningClock = 0f;
        ReplayClock = 0f;
    }
}

// ----------------------------------------------------------------------------
// 一条移动指令
// ----------------------------------------------------------------------------
[System.Serializable]
public class MoveOrder
{
    public float time;              // 相对「第一次消耗决策值」的秒数
    public ShipMover mover;
    public List<Vector3> path;
    public bool started;
}
