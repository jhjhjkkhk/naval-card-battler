using UnityEngine;
using UnityEngine.UI;

// ============================================================================
// 规划期倒计时（屏幕顶部居中）
//
// 规则：
//   · 每个我方回合从 60 秒开始倒数
//   · 倒计时归零 → 自动进入演算
//   · 玩家点「回合结束」→ 立刻进入演算（演算时长只到最后一个操作时刻 + 尾巴）
//   · 演算期间显示「演算中」，不显示数字
// ============================================================================
public class PlanningCountdown : MonoBehaviour
{
    [Header("接线（留空自动找）")]
    public TurnManager turnManager;
    public MoveReplayDirector director;

    [Header("时间")]
    public float maxSeconds = 60f;

    [Header("外观")]
    public int fontSize = 64;
    public Color normalColor = Color.white;
    public Color urgentColor = new Color(1f, 0.3f, 0.25f);
    public float urgentThreshold = 10f;
    [Tooltip("距离屏幕上边缘多少像素")]
    public float topOffset = 40f;

    Text text;
    float remaining;
    bool lastPlanning = true;

    void Start()
    {
        if (turnManager == null) turnManager = FindObjectOfType<TurnManager>();
        if (director == null) director = MoveReplayDirector.Ensure();

        SetupText();
        remaining = maxSeconds;
    }

    void SetupText()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        GameObject go = new GameObject("规划倒计时");
        go.transform.SetParent(canvas.transform, false);

        text = go.AddComponent<Text>();
        text.font = UiFont.Get();
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = normalColor;
        text.text = Mathf.CeilToInt(maxSeconds).ToString();

        RectTransform rt = text.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 1f);     // 顶部居中
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -topOffset);
        rt.sizeDelta = new Vector2(320f, 90f);
    }

    void Update()
    {
        if (director == null || text == null) return;

        bool planning = director.IsPlanning;

        // 从演算切回规划 = 新回合开始，重置计时
        if (planning && !lastPlanning) remaining = maxSeconds;
        lastPlanning = planning;

        // 演算期：不显示倒计时
        if (!planning)
        {
            text.text = "演算中";
            text.color = normalColor;
            return;
        }

        // 敌方回合不显示
        if (turnManager != null && !turnManager.IsPlayerTurn)
        {
            text.text = "";
            return;
        }

        remaining -= Time.deltaTime;
        if (remaining < 0f) remaining = 0f;

        text.text = Mathf.CeilToInt(remaining).ToString();
        text.color = remaining <= urgentThreshold ? urgentColor : normalColor;

        if (remaining <= 0f && director.IsPlanning)
        {
            Debug.Log("[回合] 倒计时结束，自动进入演算");
            director.BeginReplay();
        }
    }
}
