using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 出牌飞出动画（纯附加脚本，★ 不修改任何现有代码）
//
//   牌打出后：在松手位置放大 → 向右平移飞出屏幕 → 消失
//
// 原理：CardDrag 出牌成功后会立刻 Destroy 那张牌，本脚本拦不住。
//   所以改为——拖拽期间先记住这张牌的"图纸 prefab"和位置，
//   松手后发现牌没了（= 出牌成功），就在原地生成一个"替身"来播动画。
//
// 用法：建个空物体挂上，把 CardZone 拖进 Dealer（留空自动找）
public class CardPlayFx : MonoBehaviour
{
    [Header("接线")]
    public CardDealer dealer;              // 手牌容器（留空自动找）

    [Header("动画设置")]
    public bool enableFx = true;
    public bool debugLog = true;           // ★ 排查用：Console 会打印每一环的状态，正常后可关
    public float growScale = 1.5f;         // 先放大到多少倍
    public float growTime = 0.18f;         // 放大时长（秒）
    public float flyDistance = 900f;       // 向右飞多远（像素）
    public float flyTime = 0.42f;          // 飞行时长（秒）
    public bool fadeOut = true;            // 飞行时淡出
    public bool spinWhileFlying = false;   // 飞行时轻微旋转（想要更华丽可开）
    public float spinAngle = 12f;

    private CardDrag tracking;             // 正在跟踪的牌
    private GameObject pendingPrefab;      // 它的图纸（用来生成替身）
    private Vector3 lastPos;               // 松手那一刻的位置
    private bool wasDragging;              // 上一帧是不是在拖
    private Transform spawnRoot;           // 替身挂在哪（Canvas 下）

    void Start()
    {
        if (dealer == null) dealer = FindObjectOfType<CardDealer>();

        if (dealer == null)
        {
            Debug.LogWarning("[出牌动画] 找不到 CardDealer，功能停用");
            return;
        }

        Canvas c = dealer.GetComponentInParent<Canvas>();
        spawnRoot = c != null ? c.transform : dealer.transform.parent;

        if (debugLog)
            Debug.Log("[出牌动画] 初始化 → Dealer: " + dealer.name
                      + " | 挂载点: " + (spawnRoot != null ? spawnRoot.name : "★空★Canvas没找到"));
    }

    void Update()
    {
        if (!enableFx || dealer == null) return;

        // ① 找正在被拖的那张牌
        CardDrag cur = null;
        foreach (Transform t in dealer.transform)
        {
            CardDrag d = t.GetComponent<CardDrag>();
            if (d != null && d.IsDragging) { cur = d; break; }
        }

        if (cur != null)
        {
            if (!wasDragging && debugLog)
                Debug.Log("[出牌动画] 检测到拖拽：" + cur.gameObject.name);
            // 拖拽中：记住它、记住它的图纸、记住位置
            tracking = cur;
            pendingPrefab = cur.sourcePrefab;
            lastPos = cur.transform.position;
            wasDragging = true;
            return;
        }

        if (!wasDragging) return;

        // ② 拖着的牌找不到了 = 松手。★ 等一帧再确认，避免和 CardDrag 的执行顺序打架
        wasDragging = false;
        StartCoroutine(ConfirmRelease());
    }

    IEnumerator ConfirmRelease()
    {
        yield return null;   // ★ 等一帧：让 CardDrag.OnEndDrag 跑完、Destroy 生效

        bool destroyed = (tracking == null);   // 牌被销毁 = 出牌成功；牌还在 = 只是飞回原位

        GameObject prefab = pendingPrefab != null ? pendingPrefab
                           : (dealer != null ? dealer.cardPrefab : null);

        if (debugLog)
            Debug.Log("[出牌动画] 松手确认 → 牌已销毁(出牌): " + destroyed
                      + " | 图纸: " + (prefab != null ? prefab.name : "★空★")
                      + " | 挂载点: " + (spawnRoot != null ? spawnRoot.name : "★空★"));

        if (destroyed && prefab != null && spawnRoot != null)
        {
            if (debugLog) Debug.Log("[出牌动画] ★★ 播放飞出动画 ★★");
            yield return StartCoroutine(PlayFlyOut(prefab, lastPos));
        }
        else if (debugLog)
        {
            Debug.Log("[出牌动画] 没播动画 → destroyed=" + destroyed
                      + " prefab=" + (prefab != null) + " spawnRoot=" + (spawnRoot != null));
        }

        tracking = null;
        pendingPrefab = null;
    }

    IEnumerator PlayFlyOut(GameObject prefab, Vector3 worldPos)
    {
        // 生成替身（挂在 Canvas 下，不进手牌区，所以不会被重排、不会被清掉）
        GameObject ghost = Instantiate(prefab, spawnRoot);
        ghost.transform.position = worldPos;
        ghost.transform.SetAsLastSibling();     // 显示在最上层

        // 关掉替身的交互，免得它还能被拖
        CardDrag d = ghost.GetComponent<CardDrag>();
        if (d != null) d.enabled = false;
        CardHover h = ghost.GetComponent<CardHover>();
        if (h != null) h.enabled = false;

        RectTransform rt = ghost.transform as RectTransform;
        CanvasGroup cg = ghost.GetComponent<CanvasGroup>();
        if (cg == null) cg = ghost.gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;              // ★ 不挡点击

        Vector3 startScale = ghost.transform.localScale;
        Vector3 bigScale = startScale * growScale;

        // ① 放大（在松手的位置原地放大）
        float t = 0f;
        while (t < growTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / growTime);
            ghost.transform.localScale = Vector3.Lerp(startScale, bigScale, k);
            yield return null;
        }
        ghost.transform.localScale = bigScale;

        // ② 向右平移飞出屏幕（缓出：开始快、后面慢）
        float startX = rt != null ? rt.anchoredPosition.x : 0f;
        float startRot = ghost.transform.localEulerAngles.z;
        t = 0f;
        while (t < flyTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / flyTime);
            float eased = 1f - (1f - k) * (1f - k);

            if (rt != null)
            {
                Vector2 p = rt.anchoredPosition;
                p.x = startX + flyDistance * eased;
                rt.anchoredPosition = p;
            }

            if (fadeOut) cg.alpha = 1f - k;
            if (spinWhileFlying)
                ghost.transform.localEulerAngles =
                    new Vector3(0f, 0f, startRot + spinAngle * eased);

            yield return null;
        }

        Destroy(ghost);
    }
}
