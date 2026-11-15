using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 卡牌拖拽系统：
//   拖出卡牌区松手 = 打出去（给选中目标上buff，卡牌消耗）
//   没拖出区 / 没选中目标 / 阵营不合规 = 卡牌自动飞回原位
// 挂在卡牌预制体上（发牌器发现没挂会自动补挂）
// 卡牌身份从自己身上的 CardDefinition 读取——做预制体时就把信息填好了
public class CardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private CardDefinition definition;    // 这张卡的身份证（自己做主，不用别人发配）
    private RectTransform rt;
    private RectTransform zoneRect;       // 卡牌区（判断"有没有拖出去"）
    private Vector2 homePos;              // 队列里的家（飞回用）
    private Coroutine flying;

    [HideInInspector] public GameObject sourcePrefab; // 我克隆自哪张图纸（打出去后它要进弃牌堆）
    [HideInInspector] public CardDealer dealer;        // 发牌器（打出去要向它汇报）
    public bool IsDragging { get; private set; }       // 正被拖着吗（重排手牌时要跳过我）

    void Awake()
    {
        rt = transform as RectTransform;
        if (transform.parent != null) zoneRect = transform.parent as RectTransform;

        // 读自己身上的卡牌定义，并把卡名印到牌面上
        definition = GetComponent<CardDefinition>();
        SetupNameText();
    }

    // 在牌面上显示卡名（没有文字部件就现场造一个）
    void SetupNameText()
    {
        Text label = GetComponentInChildren<Text>();
        if (label == null)
        {
            GameObject go = new GameObject("NameText");
            go.transform.SetParent(transform, false);
            label = go.AddComponent<Text>();
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.black;
            label.fontSize = 26;
            RectTransform tr = label.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;
        }
        label.font = UiFont.Get();
        label.text = definition != null ? definition.cardName : "未配置";
    }

    // ─── 拖拽三连 ───
    public void OnBeginDrag(PointerEventData e)
    {
        IsDragging = true;      // 标记：我正在被拖，手牌重排时别动我
        if (flying != null) { StopCoroutine(flying); flying = null; }

        CardHover hover = GetComponent<CardHover>();
        if (hover != null) homePos = hover.homePosition;   // 记下家在哪（飞回用）

        // 暂停所有牌的悬停效果（包括自己），否则拖拽途中碰到邻牌会乱放大。
        // 注意：光 enabled=false 不够，正在播的动画协程还会继续跑——必须 StopAllCoroutines 掐断，
        // 再把牌恢复原大小。这些操作都是"遥控"CardHover，不改它一行代码。
        if (transform.parent != null)
        {
            foreach (Transform sib in transform.parent)
            {
                CardHover h = sib.GetComponent<CardHover>();
                if (h != null) { h.StopAllCoroutines(); h.enabled = false; }
                sib.localScale = Vector3.one;   // 途中被放大的牌立刻恢复原样
            }
        }

        transform.SetAsLastSibling();       // 拖动的牌显示在最上层
    }

    public void OnDrag(PointerEventData e)
    {
        // 世界空间画布：屏幕坐标 ≠ 世界坐标，必须先"翻译"再挪牌
        // 翻译：把鼠标的屏幕位置，换算成卡牌区平面上的世界坐标
        RectTransform plane = zoneRect != null ? zoneRect : (transform.parent as RectTransform);
        Vector3 worldPos;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(plane, e.position, e.pressEventCamera, out worldPos))
        {
            transform.position = worldPos; // 牌跟手走
        }
    }

    public void OnEndDrag(PointerEventData e)
    {
        IsDragging = false;     // 松手了，可以参与重排了

        // 恢复所有牌的悬停效果
        if (transform.parent != null)
        {
            foreach (Transform sib in transform.parent)
            {
                CardHover h = sib.GetComponent<CardHover>();
                if (h != null) h.enabled = true;
            }
        }

        if (zoneRect == null && transform.parent != null)
            zoneRect = transform.parent as RectTransform;

        // ① 拖出卡牌区了吗？（世界空间画布必须带上事件摄像机，不然判断结果不准）
        bool outsideZone = zoneRect == null ||
            !RectTransformUtility.RectangleContainsScreenPoint(zoneRect, e.position, e.pressEventCamera);

        if (outsideZone)
        {
            // ⓪ 回合 / 行动值检查：不是我方回合，或行动值已用完 → 不准出牌
            if (TurnManager.Instance != null && !TurnManager.Instance.CanPlayerAct())
            {
                Debug.Log("现在不能出牌（不是我方回合，或行动值已用完）");
                flying = StartCoroutine(FlyHome());
                return;
            }

            // ⓪ 抽牌卡：不挑目标，直接从牌库按顺序抽 N 张（N = 卡牌的 value）
            if (definition != null && definition.type == CardDefinition.CardType.Draw)
            {
                if (dealer != null)
                {
                    dealer.DrawCards(Mathf.Max(1, definition.value));
                    dealer.OnCardPlayed(gameObject, sourcePrefab);
                    if (TurnManager.Instance != null) TurnManager.Instance.SpendAP();   // 消耗 1 点行动值
                }
                else
                {
                    Debug.LogWarning("抽牌卡找不到发牌器（dealer 未接线）");
                }
                Destroy(gameObject);
                return;
            }

            // 优先打给"松手位置压着的那艘船"；没压到船才退回用当前选中的
            ShipStats target = PickShipAtScreenPoint(e);
            if (target == null) target = ShipSelector.Selected;

            // 诊断：打出目标到底是谁、阵营是什么、卡牌是什么类型（排查完可删）
            if (target != null)
                Debug.Log($"[出牌诊断] 目标船={target.shipName} 物体={target.gameObject.name} " +
                          $"isEnemy={target.isEnemy} | 卡牌={definition.cardName} " +
                          $"类型={definition.type} 是增益卡={definition.IsBuff}");

            // ② 没目标 → 收回
            if (target == null)
            {
                Debug.Log("没有选中目标，卡牌收回");
            }
            // ③ 有目标 → 结算（阵营不合规会在内部被拦下）
            else if (target.TryApplyCard(definition, out string reason))
            {
                // 打出成功：图纸进弃牌堆 → 实体销毁 → 剩余手牌补位重排
                if (dealer != null) dealer.OnCardPlayed(gameObject, sourcePrefab);
                if (TurnManager.Instance != null) TurnManager.Instance.SpendAP();   // 消耗 1 点行动值
                Destroy(gameObject);
                return;
            }
            else
            {
                Debug.Log("使用不合规：" + reason);
            }
        }

        // 走到这 = 收回：飞回队列原位
        flying = StartCoroutine(FlyHome());
    }

    // 松手位置下面压着哪艘船？朝鼠标发一条射线（和 ShipSelector 选船同一套思路）
    // 压到船 → 返回那艘船；压到水面/空处 → 返回 null
    ShipStats PickShipAtScreenPoint(PointerEventData e)
    {
        Camera cam = e.pressEventCamera != null ? e.pressEventCamera : Camera.main;
        if (cam == null) return null;

        Ray ray = cam.ScreenPointToRay(e.position);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 2000f))
            return hit.collider.GetComponentInParent<ShipStats>();  // 往上找：可能撞到的是船的某个部件
        return null;
    }

    // 收回动画：从当前位置滑回"家"（家可能被重排改过，所以每帧重新问一次）
    IEnumerator FlyHome()
    {
        Vector2 from = rt.anchoredPosition;
        Vector2 fromSize = rt.sizeDelta;
        float t = 0f;
        const float dur = 0.2f;
        CardHover h = GetComponent<CardHover>();

        while (t < dur)
        {
            t += Time.deltaTime;
            Vector2 targetPos = h != null ? h.homePosition : homePos;              // 最新的家
            Vector2 targetSize = (h != null && h.HomeSize.x > 0f) ? h.HomeSize : fromSize;
            float e = Mathf.Clamp01(t / dur);
            rt.anchoredPosition = Vector2.Lerp(from, targetPos, e);
            rt.sizeDelta = Vector2.Lerp(fromSize, targetSize, e);
            yield return null;
        }

        rt.anchoredPosition = h != null ? h.homePosition : homePos;
        if (h != null && h.HomeSize.x > 0f) rt.sizeDelta = h.HomeSize;
    }
}
