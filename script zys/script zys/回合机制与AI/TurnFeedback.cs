using UnityEngine;
using UnityEngine.UI;

// 手牌锁定（纯附加脚本，★ 不修改任何现有代码）
//
//   行动值用完 / 不是我方回合 → 手牌整体下沉 + 变暗 + 点不动拖不动
//
// 用法：场景里建个空物体，把这个脚本拖上去，把 CardZone 拖进 Dealer（留空自动找）
//
// 实现原理（不碰原代码的关键）：
//   · 移动的是「CardZone 容器本身」，不是每张牌 → 里面的牌自然跟着整体下沉
//   · 给容器加一个 CanvasGroup → 同时实现「变暗」和「禁止交互」（blocksRaycasts = false）
public class TurnFeedback : MonoBehaviour
{
    [Header("接线")]
    public CardDealer dealer;      // 手牌容器（留空自动找）

    [Header("手牌锁定（行动值用完时）")]
    public bool enableHandLock = true;
    public float lockOffsetY = -110f;                  // 下沉多少像素（负数 = 往下缩回去）
    [Range(0.2f, 1f)]
    public float lockAlpha = 0.55f;                    // 变暗到多少（1 = 不变暗）
    public float lockSlideSpeed = 9f;                  // 下沉 / 回升的速度

    private RectTransform zone;        // 手牌容器
    private Vector2 zoneBasePos;       // 容器原始位置（解锁时回到这里）
    private CanvasGroup zoneGroup;     // 挂在容器上，控制变暗 + 禁止交互
    private bool locked;               // 当前是不是锁着

    void Start()
    {
        if (dealer == null) dealer = FindObjectOfType<CardDealer>();

        if (dealer == null)
        {
            Debug.LogWarning("[手牌锁定] 场景里找不到 CardDealer，功能停用");
            return;
        }

        zone = dealer.transform as RectTransform;
        if (zone == null)
        {
            Debug.LogWarning("[手牌锁定] CardDealer 不在 UI 上，功能停用");
            return;
        }

        zoneBasePos = zone.anchoredPosition;    // 记住原始位置

        // 给容器加 CanvasGroup（不改原脚本，现场加一个）
        zoneGroup = zone.GetComponent<CanvasGroup>();
        if (zoneGroup == null) zoneGroup = zone.gameObject.AddComponent<CanvasGroup>();
        zoneGroup.alpha = 1f;
        zoneGroup.blocksRaycasts = true;
    }

    void Update()
    {
        if (!enableHandLock) return;

        TurnManager tm = TurnManager.Instance;

        // 没挂回合管理器 → 什么都不做（保持原样，不干扰）
        if (tm == null) return;

        if (zone == null || zoneGroup == null) return;

        // 该不该锁？不能行动（不是我方回合 或 行动值 0）→ 锁
        bool shouldLock = !tm.CanPlayerAct();
        if (shouldLock != locked) ApplyLock(shouldLock);

        // 平滑下沉 / 回升（直接移动容器，牌自然跟着走）
        float wantY = zoneBasePos.y + (locked ? lockOffsetY : 0f);
        if (Mathf.Abs(zone.anchoredPosition.y - wantY) > 0.5f)
        {
            Vector2 p = zone.anchoredPosition;
            p.y = Mathf.Lerp(p.y, wantY, Time.deltaTime * lockSlideSpeed);
            zone.anchoredPosition = p;
        }
    }

    void ApplyLock(bool v)
    {
        locked = v;

        zoneGroup.alpha = v ? lockAlpha : 1f;      // 变暗
        zoneGroup.blocksRaycasts = !v;             // ★ 锁定时点不中、拖不动
        zoneGroup.interactable = !v;

        Debug.Log(v ? "[手牌锁定] 锁定 → 下沉 + 变暗 + 不可交互"
                    : "[手牌锁定] 解锁 → 回升 + 恢复交互");
    }
}