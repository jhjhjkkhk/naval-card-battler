using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// buff 详情框（崩铁式）
//   点信息牌上的小图标 → 在图标正上方弹出一个长方形，显示这个 buff 的详细信息
//
// ★ 不用手动挂：第一次用的时候自动在场景里创建（独立 Canvas + 超高层级 → 永远在最前方）
public class BuffTooltip : MonoBehaviour
{
    [Header("画布指定（★ 推荐手动拖）")]
    public Canvas targetCanvas;   // 详情框挂到哪个画布下。不拖 → 自动找场景里的根画布

    [Header("外观")]
    public Vector2 boxSize = new Vector2(260, 150);                  // 详情框尺寸
    public Color bgColor = new Color(0.08f, 0.09f, 0.12f, 0.96f);    // 背景（近黑）
    public Color borderColor = new Color(0.60f, 0.50f, 0.22f, 1f);   // 描边（暗金）
    public Color titleColor = new Color(1f, 0.85f, 0.35f);           // 标题（金黄）
    public Color textColor = Color.white;
    public int titleFontSize = 22;
    public int bodyFontSize = 19;
    public float gapAboveIcon = 18f;     // 弹在图标上方多少像素
    public float iconSize = 40f;

    [Header("调试")]
    public bool debugLog = true;         // ★ 排查用：Console 打印弹框的实际坐标，正常后可关

    static BuffTooltip instance;
    public static BuffTooltip Instance
    {
        get { if (instance == null) CreateInstance(); return instance; }
    }

    RectTransform box;
    Canvas hostCanvas;      // ★ 详情框所在的画布（复用场景里已有的，不新建根 Canvas）
    Image iconImg;
    Text titleText;
    Text bodyText;
    bool visible;
    float shownAt;
    BuffEntry current;

    // ★ 场景里手动挂了本脚本 → 用它（这样 Inspector 里改的参数能保存下来）
    //   没挂 → Instance 第一次被访问时自动创建一个
    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            Build();
        }
        else
        {
            Destroy(gameObject);   // 已经有一个了，多余的自毁
        }
    }

    static void CreateInstance()
    {
        GameObject go = new GameObject("BuffTooltip");
        DontDestroyOnLoad(go);
        // ★ 不在 Hierarchy 里出现 → 选不中 → Scene 视图不会画它的 Canvas 边框
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<BuffTooltip>();   // ★ Awake 里负责注册 + Build，这里不要再 Build
    }

    void Build()
    {
        // ★ 包一层：构建出错时打印完整堆栈（Console 里红色的报错行号可能有偏移，
        //   完整堆栈才能精确指出是哪一步炸的）
        try
        {
            BuildInternal();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[详情框] 构建失败 → " + ex.GetType().Name + ": " + ex.Message
                           + "\n" + ex.StackTrace);
        }
    }

    void BuildInternal()
    {
        // 优先用场景里的全局设置（能保存），没挂就用 Inspector 上的默认值
        BuffPanelSettings s = BuffPanelSettings.Instance;
        Vector2 size = s != null ? s.tooltipSize : boxSize;
        float icSize = s != null ? s.tooltipIconSize : iconSize;
        int tFont = s != null ? s.tooltipTitleFontSize : titleFontSize;
        int bFont = s != null ? s.tooltipBodyFontSize : bodyFontSize;

        // ★★ 关键：不新建根 Canvas！
        //    新建一个 Screen Space Overlay 的根 Canvas，Unity 会给它画一个"屏幕范围"的白色边框
        //    （Scene 和 Game 都看得到）→ 就是你看到的"点击图标后画面被白框包围"
        //    所以挂到你手动拖的画布下；没拖就自动找场景里已有的根画布复用
        hostCanvas = targetCanvas != null ? targetCanvas : FindRootCanvas();
        Transform uiParent;

        if (hostCanvas != null)
        {
            uiParent = hostCanvas.transform;
        }
        else
        {
            // 兜底：场景里一个 Canvas 都没有，才新建一个
            GameObject cvGo = new GameObject("TooltipCanvas");
            cvGo.transform.SetParent(transform, false);
            hostCanvas = cvGo.AddComponent<Canvas>();
            hostCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hostCanvas.sortingOrder = 9999;
            uiParent = cvGo.transform;
        }

        // ── 盒子本体 ──
        GameObject boxGo = new GameObject("详情框");
        boxGo.transform.SetParent(uiParent, false);

        // ★★ 顺序不能反：先加 RectTransform，再加 Canvas
        //   Canvas 组件会自动附带一个 RectTransform；如果先加 Canvas 再 AddComponent<RectTransform>，
        //   同一个物体上加两次会直接抛 NullReferenceException（就是之前那个构建失败）
        box = boxGo.AddComponent<RectTransform>();
        box.sizeDelta = size;

        // ★ 嵌套 Canvas + overrideSorting → 盖住同层所有 UI（不用再建根 Canvas）
        Canvas own = boxGo.AddComponent<Canvas>();
        own.overrideSorting = true;
        own.sortingOrder = 9999;
        box.pivot = new Vector2(0.5f, 0f);          // 底边中心对齐锚点
        box.anchorMin = box.anchorMax = Vector2.zero;

        Image bg = boxGo.AddComponent<Image>();
        bg.color = bgColor;
        bg.raycastTarget = false;                    // ★ 不挡点击

        Outline ol = boxGo.AddComponent<Outline>();
        ol.effectColor = borderColor;
        ol.effectDistance = new Vector2(2f, 2f);

        // ── 左上小图标 ──
        GameObject iconGo = new GameObject("图标");
        iconGo.transform.SetParent(boxGo.transform, false);
        RectTransform iconRt = iconGo.AddComponent<RectTransform>();
        iconImg = iconGo.AddComponent<Image>();
        iconImg.raycastTarget = false;
        iconRt.anchorMin = iconRt.anchorMax = iconRt.pivot = new Vector2(0f, 1f);
        iconRt.anchoredPosition = new Vector2(12f, -12f);
        iconRt.sizeDelta = new Vector2(icSize, icSize);

        // ── 标题（buff 名）──
        GameObject titleGo = new GameObject("标题");
        titleGo.transform.SetParent(boxGo.transform, false);
        RectTransform titleRt = titleGo.AddComponent<RectTransform>();
        titleText = titleGo.AddComponent<Text>();
        titleText.font = UiFont.Get();
        titleText.fontSize = tFont;
        titleText.color = titleColor;
        titleText.alignment = TextAnchor.MiddleLeft;
        titleText.raycastTarget = false;
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0f, 1f);
        titleRt.anchoredPosition = new Vector2(icSize + 20f, -14f);
        titleRt.sizeDelta = new Vector2(-(icSize + 32f), 30f);

        // ── 正文（效果 / 剩余回合 / 描述）──
        GameObject bodyGo = new GameObject("正文");
        bodyGo.transform.SetParent(boxGo.transform, false);
        RectTransform bodyRt = bodyGo.AddComponent<RectTransform>();
        bodyText = bodyGo.AddComponent<Text>();
        bodyText.font = UiFont.Get();
        bodyText.fontSize = bFont;
        bodyText.color = textColor;
        bodyText.alignment = TextAnchor.UpperLeft;
        bodyText.raycastTarget = false;
        bodyRt.anchorMin = Vector2.zero;
        bodyRt.anchorMax = Vector2.one;
        bodyRt.offsetMin = new Vector2(14f, 10f);
        bodyRt.offsetMax = new Vector2(-14f, -(icSize + 22f));

        boxGo.SetActive(false);

        if (debugLog)
            Debug.Log("[详情框] 构建完成 → 挂在画布「" + hostCanvas.name
                      + "」下（模式 " + hostCanvas.renderMode + "，scaleFactor " + hostCanvas.scaleFactor + "）");
    }

    // 找场景里已有的根画布（信息牌 / 手牌所在的那个），复用它
    // ★ 不自己新建根 Canvas —— 新建的 Overlay 画布会被 Unity 画上"屏幕范围"的白色边框
    static Canvas FindRootCanvas()
    {
        Canvas[] all = FindObjectsOfType<Canvas>();
        for (int i = 0; i < all.Length; i++)
        {
            Canvas c = all[i];
            if (c != null && c.isRootCanvas && c.gameObject.activeInHierarchy)
                return c;
        }
        return null;
    }

    // 点图标：同一个再点一次 = 收起；点别的 = 换内容
    public void Toggle(BuffEntry b, Vector3 anchorWorldPos, Camera cam)
    {
        if (visible && current == b) { Hide(); return; }
        Show(b, anchorWorldPos, cam);
    }

    public void Show(BuffEntry b, Vector3 anchorWorldPos, Camera cam)
    {
        if (b == null) return;

        // ★ 防御：Build 没跑完（比如构建途中被报错打断）→ 直接显示会 NRE，这里拦住
        if (box == null || iconImg == null || titleText == null || bodyText == null)
        {
            Debug.LogWarning("[详情框] 组件尚未构建完成，忽略本次显示（若反复出现请把 Console 完整报错发我）");
            return;
        }

        current = b;

        // 图标的世界坐标 → 屏幕坐标（像素）
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, anchorWorldPos);

        // ★ 换算成画布坐标：复用的画布带 CanvasScaler 时要除掉缩放系数
        float sf = hostCanvas != null ? hostCanvas.scaleFactor : 1f;
        if (sf <= 0f) sf = 1f;

        Vector2 local = screen / sf;
        float gap = gapAboveIcon / sf;
        float screenH = Screen.height / sf;

        // 默认弹在图标上方；信息牌在屏幕顶部、上方放不下时 → 自动翻到图标下方
        Vector2 pos = local + new Vector2(0f, gap);
        float boxH = box.sizeDelta.y;
        if (pos.y + boxH > screenH)
            pos = local - new Vector2(0f, gap + boxH);

        box.anchoredPosition = pos;

        // 有图用图，没图用类型色块
        iconImg.sprite = b.icon;
        iconImg.color = b.icon != null ? Color.white : TypeColor(b.type);

        titleText.text = b.buffName;

        string desc = string.IsNullOrEmpty(b.description) ? "" : "\n" + b.description;
        bodyText.text = b.EffectText + "\n剩余 " + b.TurnsText + desc;

        box.gameObject.SetActive(true);
        visible = true;
        shownAt = Time.time;

        if (debugLog)
            Debug.Log("[详情框] 显示「" + b.buffName + "」→ 图标屏幕坐标 " + screen
                      + " | 框落点 " + pos + " | 框尺寸 " + box.sizeDelta
                      + " | 当前屏幕 " + Screen.width + "x" + Screen.height);
    }

    public void Hide()
    {
        if (box != null) box.gameObject.SetActive(false);
        visible = false;
        current = null;
    }

    void Update()
    {
        if (!visible) return;
        // 弹出后 0.2 秒内忽略点击，否则"点图标那一下"会立刻把它关掉
        if (Time.time - shownAt < 0.2f) return;

        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
        {
            // ★ 点在 UI 上（包括图标本身）→ 不在这里收起。
            //   图标的收/开由它自己的 onClick（Toggle）负责 ——
            //   否则这一帧先 Hide、按钮又 Toggle 弹开，第二次点击永远关不掉
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Hide();
        }
    }

    // 没配图标素材时，按 buff 类型给个颜色块（攻击红 / 防御蓝 / 回血绿 / 减益紫橙）
    public static Color TypeColor(CardDefinition.CardType t)
    {
        switch (t)
        {
            case CardDefinition.CardType.AttackUp: return new Color(0.95f, 0.35f, 0.25f);
            case CardDefinition.CardType.DefenseUp: return new Color(0.30f, 0.60f, 0.95f);
            case CardDefinition.CardType.Heal: return new Color(0.35f, 0.85f, 0.40f);
            case CardDefinition.CardType.AttackDown: return new Color(0.72f, 0.30f, 0.88f);
            case CardDefinition.CardType.DefenseDown: return new Color(0.88f, 0.55f, 0.20f);
            default: return Color.gray;
        }
    }
}
