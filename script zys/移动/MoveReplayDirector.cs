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
    readonly List<AttackOrder> attacks = new List<AttackOrder>();

    [Header("射击")]
    [Tooltip("炮弹飞行速度（格/秒）")]
    public float shellSpeedHex = 8f;

    [Header("齐射节奏（分组开火）")]
    [Tooltip("每组最少几门")]
    public int volleyGroupMin = 1;
    [Tooltip("每组最多几门")]
    public int volleyGroupMax = 3;
    [Tooltip("组与组之间隔多少秒")]
    public float volleyInterval = 0.22f;

    [Header("散布（越大越散）")]
    [Tooltip("基础散布半径（格）：贴脸时也有这么多")]
    public float scatterBaseHex = 0.45f;

    [Tooltip("每 1 格距离额外增加多少散布（格）")]
    public float scatterPerHex = 0.08f;

    [Tooltip("散布上限（格）")]
    public float scatterMaxHex = 2f;

    [Tooltip("每回合演算的随机种子（同种子可复现）")]
    public int rngSeed = 12345;

    int activeShells;
    System.Random rng;

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

    /// <summary>登记攻击指令（同一艘船只保留最后一次瞄准点）</summary>
    public void ReplaceAttack(ShipStats attacker, Hex aim, ShellSpec shell)
    {
        if (attacker == null || shell == null) return;

        if (startClockOnFirstOrder && !clockStarted)
        {
            clockStarted = true;
            PlanningClock = 0f;
        }

        for (int i = attacks.Count - 1; i >= 0; i--)
            if (attacks[i].attacker == attacker) attacks.RemoveAt(i);

        attacks.Add(new AttackOrder
        {
            time = PlanningClock,
            attacker = attacker,
            aim = aim,
            shell = shell,
            started = false
        });

        Debug.Log("[演算] 登记攻击 t=" + PlanningClock.ToString("0.0") + "s  "
                  + attacker.shipName + " → " + aim);
    }

    /// <summary>取某艘船已经登记的移动终点（攻击范围要用它当圆心）</summary>
    public bool TryGetPlannedEnd(ShipMover mover, out Vector3 end)
    {
        end = Vector3.zero;
        if (mover == null) return false;

        for (int i = orders.Count - 1; i >= 0; i--)
        {
            MoveOrder o = orders[i];
            if (o.mover != mover || o.path == null || o.path.Count == 0) continue;
            end = o.path[o.path.Count - 1];
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- 开火

    void FireAttack(AttackOrder a)
    {
        HexGrid grid = HexGrid.Instance != null ? HexGrid.Instance : FindObjectOfType<HexGrid>();
        if (grid == null || a.attacker == null) return;

        Hex aHex = grid.WorldToHex(a.attacker.transform.position);
        float dist = HexGrid.Distance(aHex, a.aim);

        if (dist > a.attacker.maxRangeHex)
        {
            Debug.Log("[演算] " + a.attacker.shipName + " 目标 " + dist + " 格，超出射程，放弃开火");
            return;
        }

        // 弹药库受损 → 本回合不能开火
        ShipDebuffs attackerDebuff = a.attacker.GetComponent<ShipDebuffs>();
        if (attackerDebuff != null && attackerDebuff.CannotFire)
        {
            Debug.Log("[演算] " + a.attacker.shipName + " 弹药库受损，本回合无法开火");
            return;
        }

        if (rng == null) rng = new System.Random(rngSeed);

        Vector3 aimPos = grid.HexToWorld(a.aim); aimPos.y = 0f;
        float spacing = grid.hexSize * Mathf.Sqrt(3f);
        Vector3 muzzle = a.attacker.transform.position + Vector3.up * 2f;

        float radius = SalvoCalculator.ScatterRadiusWorld(dist, spacing, scatterBaseHex, scatterPerHex, scatterMaxHex);
        if (attackerDebuff != null) radius *= attackerDebuff.ScatterMultiplier;   // 炮机受损打得更散
        int barrels = Mathf.Max(1, a.attacker.TotalBarrels);
        float speed = Mathf.Max(shellSpeedHex, 1f) * spacing;

        Debug.Log("[演算] t=" + ReplayClock.ToString("0.0") + "s 开火：" + a.attacker.shipName
                  + " → " + a.aim + "（" + barrels + " 门，散布 ±" + (radius / spacing).ToString("0.00") + " 格）");

        StartCoroutine(FireInGroups(a, barrels, aimPos, radius, spacing, muzzle, speed));
    }

    /// <summary>分组开火：1~3 门一组，组间停一下，直到打完</summary>
    System.Collections.IEnumerator FireInGroups(AttackOrder a, int barrels, Vector3 aimPos,
                                                float radius, float spacing, Vector3 muzzle, float speed)
    {
        int remaining = barrels;
        int groupIndex = 0;

        while (remaining > 0)
        {
            int group = Mathf.Clamp(Random.Range(volleyGroupMin, volleyGroupMax + 1), 1, remaining);
            remaining -= group;
            groupIndex++;

            ImpactFx.ResetSalvoBudget();   // 每组各自的特效预算

            for (int i = 0; i < group; i++)
            {
                Vector3 landing = SalvoCalculator.LandingPoint(aimPos, radius, rng);
                landing.y = 0f;

                AttackOrder order = a;
                Vector3 land = landing;

                activeShells++;
                ShellFlight.Spawn(muzzle, land, speed, spacing * 0.85f, delegate
                {
                    activeShells--;
                    OnShellLand(order, land);
                });
            }

            Debug.Log("[演算]   " + a.attacker.shipName + " 第 " + groupIndex + " 组开火：" + group
                      + " 门（剩 " + remaining + "）");

            if (remaining > 0) yield return new WaitForSeconds(volleyInterval);
        }
    }

    void OnShellLand(AttackOrder a, Vector3 landing)
    {
        HexGrid grid = HexGrid.Instance != null ? HexGrid.Instance : FindObjectOfType<HexGrid>();
        if (grid == null || a.attacker == null) return;

        Hex h = grid.WorldToHex(landing);

        List<ShipStats> targets = FindShipsAt(h, a.attacker);
        bool nearMiss = false;

        if (targets.Count == 0)
        {
            ShipStats near = FindShipNear(h, a.attacker, 1);
            if (near != null) { targets.Add(near); nearMiss = true; }
        }

        if (targets.Count == 0)
        {
            ImpactFx.Play(new Vector3(landing.x, 10.2f, landing.z), FxKind.Miss);
            Debug.Log("[演算] 落空 " + h + "（打水）");
            return;
        }

        Hex aHex = grid.WorldToHex(a.attacker.transform.position);
        float dist = HexGrid.Distance(aHex, h);

        Vector3 dir = landing - a.attacker.transform.position;
        dir.y = 0f;

        if (nearMiss) ImpactFx.Play(new Vector3(landing.x, 10.2f, landing.z), FxKind.NearMiss);

        // 同格的每艘船都结算一次
        for (int i = 0; i < targets.Count; i++)
        {
            ShipStats target = targets[i];
            if (target == null || target.currentHp <= 0) continue;

            ShotResult shot;
            if (nearMiss)
            {
                ShotResult? nm = CombatResolver.ResolveSingleShell(
                    a.attacker, target, dir, target.transform.forward, dist, a.shell, rng);
                if (nm == null) continue;
                shot = nm.Value;
            }
            else
            {
                shot = CombatResolver.ResolveHit(
                    a.attacker, target, dir, target.transform.forward, dist, a.shell, rng);
            }

            // 按结果播不同特效
            if (!nearMiss)
            {
                FxKind kind = FxKind.Penetration;
                if (shot.outcome == ShotOutcome.Ricochet) kind = FxKind.Ricochet;
                else if (shot.outcome == ShotOutcome.NoPenetration) kind = FxKind.NoPen;
                ImpactFx.Play(new Vector3(landing.x, landing.y + 1.5f, landing.z), kind);
            }

            ApplyShot(target, shot);
        }
    }

    void ApplyShot(ShipStats target, ShotResult shot)
    {
        int dmg = Mathf.RoundToInt(shot.damage);
        target.currentHp = Mathf.Max(0, target.currentHp - dmg);

        Debug.Log("[演算]   " + shot.Describe()
                  + " → " + target.shipName + " HP " + target.currentHp + "/" + target.maxHp);

        if (dmg > 0)
            DamagePopup.Show(target.transform.position, dmg, shot.critical);

        // debuff 落地
        if (shot.debuff != DebuffType.None)
            ShipDebuffs.Get(target).Apply(shot.debuff);

        // 弹药库殉爆
        if (shot.debuff == DebuffType.Magazine && rng != null && rng.NextDouble() < CombatSettings.Instance.magazineDetonateChance)
        {
            Debug.Log("[演算]   ★ 弹药库殉爆！" + target.shipName);
            ShipSink.Sink(target, "弹药库殉爆");
            return;
        }

        if (target.currentHp <= 0) ShipSink.Sink(target, shot.outcome.ToString());
    }

    List<ShipStats> FindShipsAt(Hex hex, ShipStats attacker)
    {
        List<ShipStats> list = new List<ShipStats>();
        HexGrid grid = HexGrid.Instance != null ? HexGrid.Instance : FindObjectOfType<HexGrid>();
        if (grid == null) return list;

        foreach (ShipStats s in FindObjectsOfType<ShipStats>())
        {
            if (s == null || s == attacker) continue;
            if (s.isEnemy == attacker.isEnemy) continue;
            if (s.currentHp <= 0) continue;
            if (grid.WorldToHex(s.transform.position) == hex) list.Add(s);
        }
        return list;
    }

    ShipStats FindShipNear(Hex hex, ShipStats attacker, int radius)
    {
        HexGrid grid = HexGrid.Instance != null ? HexGrid.Instance : FindObjectOfType<HexGrid>();
        if (grid == null) return null;

        ShipStats best = null;
        int bestD = int.MaxValue;

        foreach (ShipStats s in FindObjectsOfType<ShipStats>())
        {
            if (s == null || s == attacker) continue;
            if (s.isEnemy == attacker.isEnemy) continue;
            if (s.currentHp <= 0) continue;

            int d = HexGrid.Distance(hex, grid.WorldToHex(s.transform.position));
            if (d <= radius && d < bestD) { bestD = d; best = s; }
        }
        return best;
    }
    // ---------------------------------------------------------------- 回放

    public void BeginReplay()
    {
        if (!IsPlanning) return;

        IsPlanning = false;
        IsReplaying = true;
        ReplayClock = 0f;

        for (int i = 0; i < orders.Count; i++) orders[i].started = false;
        for (int i = 0; i < attacks.Count; i++) attacks[i].started = false;
        activeShells = 0;
        rng = new System.Random(rngSeed);

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

        // 1.5) 到点开火
        for (int i = 0; i < attacks.Count; i++)
        {
            AttackOrder a = attacks[i];
            if (a.started || a.time > ReplayClock) continue;
            a.started = true;
            FireAttack(a);
        }

        // 2) 结束条件：指令全执行完 + 所有船都停下 + 没有炮弹在飞
        bool allStarted = true;
        bool anyMoving = false;

        for (int i = 0; i < orders.Count; i++)
        {
            MoveOrder o = orders[i];
            if (!o.started) { allStarted = false; continue; }
            if (o.mover != null && o.mover.IsMoving) anyMoving = true;
        }

        bool allAttacksStarted = true;
        for (int i = 0; i < attacks.Count; i++) if (!attacks[i].started) allAttacksStarted = false;

        if (allStarted && allAttacksStarted && !anyMoving && activeShells <= 0) { EndReplay(); return; }

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
        attacks.Clear();
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
public class AttackOrder
{
    public float time;
    public ShipStats attacker;
    public Hex aim;
    public ShellSpec shell;
    public bool started;
}

[System.Serializable]
public class MoveOrder
{
    public float time;              // 相对「第一次消耗决策值」的秒数
    public ShipMover mover;
    public List<Vector3> path;
    public bool started;
}
