using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

// 牌库查看器：挂在右下角的"牌库"图标上
// 点牌库   → 弹出半透明面板，展示接下来要发的牌（从左到右就是发牌顺序）
// 再点一下，或点面板空白处 → 关闭
// 牌库图标右下角的"数量"角标会实时显示还剩几张
public class DeckViewer : MonoBehaviour, IPointerClickHandler
{
    [Header("必须拖：发牌器（挂 CardDealer 的物体，也就是 CardZone）")]
    public CardDealer dealer;

    [Header("预览面板样式")]
    public float previewCardW = 160f; // 预览牌的宽
    public float previewCardH = 220f; // 预览牌的高
    public float gap = 20f;           // 牌与牌的间距
    public int perRow = 8;            // 一行摆几张
    public int maxShow = 10;          // 面板里最多显示几张（只显示最近打出去的牌）

    private GameObject panel;       // 弹出面板（第一次点击时现场搭建）
    private RectTransform cardArea; // 预览牌都摆在这个区域里
    private Text countText;         // 牌库图标上的数字角标

    void Start()
    {
        // 找到牌库图标下名为"数量"的子物体，拿它的文字组件（找得到才有角标）
        Transform badge = transform.Find("数量");
        if (badge != null) countText = badge.GetComponent<Text>();
    }

    void Update()
    {
        // 每帧刷新角标数字：还剩几张没发
        if (dealer != null && countText != null)
            countText.text = dealer.RemainingCount.ToString();
    }

    // 点牌库图标时 Unity 自动调用（和 CardHover 的 OnPointerEnter 是同一套接口）
    public void OnPointerClick(PointerEventData eventData)
    {
        if (dealer == null)
        {
            Debug.LogWarning("DeckViewer 没拖 dealer，把 CardZone 拖进来");
            return;
        }
        if (panel == null) BuildPanel();   // 第一次点击：先搭好面板

        bool show = !panel.activeSelf;     // 当前开着就关，关着就开
        panel.SetActive(show);
        if (show) RefreshPanel();          // 每次打开都重摆一遍（剩余的牌每次不一样）
    }

    // ================= 下面是"装修"代码：用代码搭 UI，效果和在 Inspector 里手动建一模一样 =================

    // 搭面板骨架：全屏半透明黑底 + 标题 + 摆牌区
    void BuildPanel()
    {
        Canvas canvas = GetComponentInParent<Canvas>();

        // 面板本体：铺满整个画面
        panel = new GameObject("牌库面板");
        panel.transform.SetParent(canvas.transform, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;               // 锚点四角拉开 = 铺满
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.85f);          // 黑色，85% 不透明

        // 点面板空白处 → 关闭（逻辑塞在文件最底下的小脚本 PanelCloser 里）
        panel.AddComponent<PanelCloser>().viewer = this;

        // 面板里不放任何文字：纯粹摆牌，观感干净

        // 摆牌区：画面正中央（牌的坐标都以它为原点）
        GameObject areaObj = new GameObject("牌区");
        areaObj.transform.SetParent(panel.transform, false);
        cardArea = areaObj.AddComponent<RectTransform>();
        cardArea.anchorMin = cardArea.anchorMax = cardArea.pivot = new Vector2(0.5f, 0.5f);
    }

    // 重摆一遍：清掉旧预览 → 问发牌器要剩余的牌 → 一张张摆好
    void RefreshPanel()
    {
        // 清掉上一轮摆的牌（牌区里的所有孩子，从后往前删）
        for (int i = cardArea.childCount - 1; i >= 0; i--)
            Destroy(cardArea.GetChild(i).gameObject);

        // 只展示"已经打出去的牌"（弃牌堆），最新的排最前，最多 maxShow 张
        List<GameObject> remaining = dealer.GetDiscardCards(maxShow);

        for (int i = 0; i < remaining.Count; i++)
        {
            // 克隆一张"预览牌"（和你发牌时克隆的是同一张图纸）
            GameObject card = Instantiate(remaining[i], cardArea);
            RectTransform rt = card.transform as RectTransform;

            // 预览牌只许看、不许动：把悬停和拖拽组件销掉
            CardHover hover = card.GetComponent<CardHover>();
            if (hover != null) Destroy(hover);
            CardDrag drag = card.GetComponent<CardDrag>();
            if (drag != null) Destroy(drag);

            // 站位：每行 perRow 张，从上往下、从左往右；每行居中对齐
            int row = i / perRow;
            int col = i % perRow;
            int inRow = Mathf.Min(remaining.Count - row * perRow, perRow); // 这一行实际几张
            float rowWidth = inRow * (previewCardW + gap) - gap;           // 这一行总宽
            float x = -rowWidth * 0.5f + previewCardW * 0.5f + col * (previewCardW + gap);
            float y = 150f - row * (previewCardH + gap);                   // 第一行偏上，往下排

            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(previewCardW, previewCardH);
            rt.localScale = Vector3.one;
        }
    }
}

// 小配角：挂在面板黑底上，负责"点空白处关闭"
// 不能写进 DeckViewer 里，因为一个文件里每个类要独立挂到不同物体上
public class PanelCloser : MonoBehaviour, IPointerClickHandler
{
    public DeckViewer viewer; // 谁造的我，回头找谁

    public void OnPointerClick(PointerEventData eventData)
    {
        viewer.OnPointerClick(eventData); // 复用牌库的开关逻辑：当前开着 → 关上
    }
}