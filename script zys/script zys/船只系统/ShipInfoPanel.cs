using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 船舰信息牌（屏幕边栏版）：
//   友方船 → 固定在屏幕最左侧，从上往下排队，每块紧紧相连
//   敌方船 → 固定在屏幕最右侧，同上
//   点击牌子 → 放大成三行详细信息（显示在同栏其他牌子上面，不会被挡）；再点一下缩回
// 由 ShipStats 开局时自动创建，挂在主画布下的"友方信息栏/敌方信息栏"里
public class ShipInfoPanel : MonoBehaviour, IPointerClickHandler
{
    private ShipStats ship;    // 数据来源（哪艘船）
    private Text text;         // 显示文字的部件
    private bool expanded;     // 展开模式？（点牌子或选中船时 = true）

    private Image hpFill;              // 血条前景（宽度按血量比例伸缩）
    private RectTransform hpSlotRt;    // 血条底槽（负责定位，长度 = 牌子宽）
    private Coroutine sizeAnim;        // 正在跑的尺寸动画（防止两个动画打架）

    [Header("状态栏设置")]
    public float buffIconSize = 44f;   // ★ buff 图标大小（像素），想更大就调大

    private GameObject buffBar;        // ★ 状态栏：buff 图标（只在展开时显示，收起完全隐藏）
    private RectTransform buffBarRt;
    private int lastBuffCount = -1;    // 上次图标个数（数量变了才重建，不每帧重建）

    // 全场只允许一块牌展开：记住"当前那块"，再展开别的会先把这块收回去
    private static ShipInfoPanel currentExpanded = null;

    // 两种状态的尺寸（造一次全类共享，谁也不许改）
    private static readonly Vector2 collapsedSize = new Vector2(420, 200);  // ★ 比原来高：顶部给大横幅船图留位置
    private static readonly Vector2 expandedSize = new Vector2(420, 350);
    private const float shipImgHeight = 110f;   // ★ 顶部船舰图片的高度（横幅）
    private const float topMargin = 40f;   // 第一块牌离画布顶部的距离（像素），想再远就改大这个数
    private const float expandDuration = 0.25f;  // 展开/收起动画时长（秒）
    private const float hpBarHeight = 14f;       // 血条厚度
    private const float hpBarPadding = 10f;      // 血条左右留白

    // 工厂方法：造一块新的信息牌，自动挂进左栏或右栏（ShipStats 开局调用）
    public static ShipInfoPanel Create(ShipStats owner)
    {
        Canvas main = FindObjectOfType<Canvas>();      // 主画布（场景里唯一的那块）
        if (main == null) { Debug.LogError("场景里没有画布，信息牌无处安放"); return null; }

        bool left = !owner.isEnemy;                    // 友方 → 左栏；敌方 → 右栏

        // ── ① 找到（或现场造）这一侧的信息栏容器 ──
        string barName = left ? "友方信息栏" : "敌方信息栏";
        RectTransform bar = main.transform.Find(barName) as RectTransform;
        if (bar == null)
        {
            GameObject barGo = new GameObject(barName);
            barGo.transform.SetParent(main.transform, false);
            bar = barGo.AddComponent<RectTransform>();
            // 贴屏幕边缘、上下拉满的一条"挂衣杆"（左栏贴左边，右栏贴右边）
            float edge = left ? 0f : 1f;
            bar.anchorMin = new Vector2(edge, 0f);
            bar.anchorMax = new Vector2(edge, 1f);
            bar.pivot = new Vector2(edge, 0.5f);
            bar.offsetMin = bar.offsetMax = Vector2.zero;
        }

        // ── ② 造牌子本体，挂到栏下 ──
        GameObject go = new GameObject(owner.shipName + "_信息牌");
        go.transform.SetParent(bar, false);
        RectTransform rt = go.AddComponent<RectTransform>();

        // 锚在栏的顶角：友方牌左上角贴杆、向右伸；敌方牌右上角贴杆、向左伸
        float hEdge = left ? 0f : 1f;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(hEdge, 1f);
        rt.sizeDelta = collapsedSize;

        // 排队：自己是栏里第几个孩子，就往下挪几个牌子高（紧紧相连）；先留出顶部空隙
        int index = bar.childCount - 1;                // 减 1：childCount 把自己也算进去了
        rt.anchoredPosition = new Vector2(0, -topMargin - index * collapsedSize.y);

        // ── ③ 背景板（半透明黑，白字的衬底）──
        GameObject bg = new GameObject("背景");
        bg.transform.SetParent(go.transform, false);
        Image img = bg.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.6f);
        StretchToFill(img.rectTransform);

        // ── ④ 血条：底槽（贴牌子底部，左右留白）──
        GameObject slot = new GameObject("血条底");
        slot.transform.SetParent(go.transform, false);
        RectTransform slotRt = slot.AddComponent<RectTransform>();
        Image slotImg = slot.AddComponent<Image>();
        slotImg.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);   // 深灰底槽
        slotRt.anchorMin = new Vector2(0f, 0f);              // 锚：左右拉满、贴底
        slotRt.anchorMax = new Vector2(1f, 0f);
        slotRt.pivot = new Vector2(0.5f, 0f);

        // ── ⑤ 血条前景（从左往右伸缩，宽度在 LateUpdate 里按血量算）──
        GameObject fill = new GameObject("血条");
        fill.transform.SetParent(slot.transform, false);
        RectTransform fillRt = fill.AddComponent<RectTransform>();
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = Color.green;
        fillRt.anchorMin = new Vector2(0f, 0f);              // 锚在底槽左边缘，向右长
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;

        // ── ⑤·五 船舰图片（贴牌子最顶上、横跨全宽的长方形）──
        GameObject shipImgGo = new GameObject("船舰图片");
        shipImgGo.transform.SetParent(go.transform, false);
        Image shipImg = shipImgGo.AddComponent<Image>();
        shipImg.type = Image.Type.Simple;                    // 明确用 Simple：图片拉伸铺满整个框
        shipImg.preserveAspect = false;                      // ★ 不保持比例 → 强制铺满 420 宽横幅
        RectTransform shipRt = shipImg.rectTransform;
        shipRt.anchorMin = new Vector2(0f, 1f);              // 锚在牌子顶部
        shipRt.anchorMax = new Vector2(1f, 1f);              // 左右拉满 = 横跨全宽
        shipRt.pivot = new Vector2(0.5f, 1f);
        shipRt.offsetMin = new Vector2(0f, -shipImgHeight);  // 高度 = shipImgHeight
        shipRt.offsetMax = Vector2.zero;

        // 图片来源：① 船的 ShipStats 上手动拖的 shipIcon 优先；② 没拖 → 自动取船模型材质贴图
        Sprite shipSprite = owner.shipIcon;
        if (shipSprite == null)
        {
            Texture2D tex = null;
            foreach (Renderer r in owner.GetComponentsInChildren<Renderer>())
            {
                if (r.sharedMaterial != null && r.sharedMaterial.mainTexture is Texture2D)
                {
                    tex = (Texture2D)r.sharedMaterial.mainTexture;
                    break;
                }
            }
            if (tex != null)
                shipSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
        if (shipSprite != null)
            shipImg.sprite = shipSprite;
        else
            shipImg.enabled = false;   // 两边都没有 → 隐藏这块，不留空白

        // ★ 布局算完后再打印真实尺寸（创建瞬间 rect 还没算好，打印出来是 0）
        Canvas.ForceUpdateCanvases();
        Debug.Log("[信息牌 v2] " + owner.shipName + " 顶部船图 → " +
                  (shipSprite != null ? "已设置：" + shipSprite.name : "没有图片（shipIcon 未拖 & 模型无贴图）") +
                  " | 实际显示区域 " + shipRt.rect.width + " x " + shipRt.rect.height +
                  " | 保持比例=" + shipImg.preserveAspect + " | 拉伸模式=" + shipImg.type);

        // ── ⑥ 文字（底部让出血条的位置，免得字压在条上）──
        GameObject txt = new GameObject("文字");
        txt.transform.SetParent(go.transform, false);
        Text t = txt.AddComponent<Text>();
        t.font = UiFont.Get();
        t.color = Color.white;
        t.fontSize = 26;
        t.alignment = TextAnchor.MiddleCenter;
        StretchToFill(t.rectTransform);
        t.rectTransform.offsetMin = new Vector2(0f, hpBarHeight + 12f);   // 文字区域底边抬高
        t.rectTransform.offsetMax = new Vector2(0f, -shipImgHeight);      // ★ 顶边让出船图的位置

        // ── ⑥·五 状态栏：buff 小图标横排（★ 收起时完全隐藏，展开才显示）──
        GameObject buffs = new GameObject("状态栏");
        buffs.transform.SetParent(go.transform, false);
        RectTransform buffsRt = buffs.AddComponent<RectTransform>();
        buffsRt.anchorMin = buffsRt.anchorMax = buffsRt.pivot = new Vector2(0.5f, 0f);
        buffsRt.anchoredPosition = new Vector2(0f, hpBarHeight + 26f);
        buffsRt.sizeDelta = new Vector2(360f, 52f);   // ★ 高度要放得下 44 的图标

        HorizontalLayoutGroup hlg = buffs.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 4f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = false;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
        buffs.SetActive(false);     // 收起时不显示

        // ── ⑦ 挂上本脚本并接线 ──
        ShipInfoPanel panel = go.AddComponent<ShipInfoPanel>();
        panel.ship = owner;
        panel.text = t;
        panel.hpFill = fillImg;
        panel.hpSlotRt = slotRt;
        panel.buffBar = buffs;
        panel.buffBarRt = buffsRt;
        RefreshBarLayout(bar);      // 新生一张牌后，整栏重新排队（防上张牌正展开导致重叠）
        return panel;
    }

    // 小工具：锚点四角拉开 + 偏移归零 = 铺满父物体（背景板和文字都用）
    static void StretchToFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // 重新计算这一栏所有信息牌的上下排列：谁展开了，下面的牌就让出位置
    static void RefreshBarLayout(RectTransform bar)
    {
        float y = topMargin;
        for (int i = 0; i < bar.childCount; i++)
        {
            RectTransform child = bar.GetChild(i) as RectTransform;
            if (child == null) continue;
            child.anchoredPosition = new Vector2(child.anchoredPosition.x, -y);
            y += child.sizeDelta.y;
        }
    }

    // 展开/收起（ShipSelector 点船时调用；玩家点牌子走下面的 OnPointerClick）
    // 全场互斥：展开这块的同时，上一块展开的会自动收回 → 永远只有一个是放大的
    public void SetExpanded(bool on)
    {
        if (expanded == on && sizeAnim == null) return;   // 状态没变、也没在动画中 → 不用重复做

        if (on && currentExpanded != null && currentExpanded != this)
            currentExpanded.SetExpanded(false);            // 先把上一块收回去

        expanded = on;
        currentExpanded = on ? this : (currentExpanded == this ? null : currentExpanded);

        // ★ 收起这块牌子时，顺带关掉它弹出的 buff 详情框
        //   （buff 图标只在展开时显示，牌子收起后详情框悬在半空很奇怪）
        if (!on && BuffTooltip.Instance != null)
            BuffTooltip.Instance.Hide();

        if (sizeAnim != null) StopCoroutine(sizeAnim);
        sizeAnim = StartCoroutine(AnimateSize(on ? expandedSize : collapsedSize));
    }

    // 缓慢缩放动画：每帧改一点尺寸，并让同栏其他牌子同步让位（手风琴效果）
    IEnumerator AnimateSize(Vector2 target)
    {
        RectTransform rt = transform as RectTransform;
        Vector2 start = rt.sizeDelta;
        float t = 0f;
        while (t < expandDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / expandDuration);
            float e = p * p * (3f - 2f * p);                    // 缓入缓出（两头慢、中间快）
            rt.sizeDelta = Vector2.Lerp(start, target, e);
            RefreshBarLayout(transform.parent as RectTransform); // 下面的牌实时让位
            yield return null;
        }
        rt.sizeDelta = target;
        RefreshBarLayout(transform.parent as RectTransform);
        sizeAnim = null;
    }

    // 点牌子：展开 ⇄ 收起；展开时顺带让它主人那艘船进入选中状态（海面白圈）
    public void OnPointerClick(PointerEventData e)
    {
        bool next = !expanded;
        SetExpanded(next);

        if (next) ShipSelector.SelectShip(ship);                       // 展开 → 船也被选中
        else if (ShipSelector.Selected == ship) ShipSelector.DeselectShip(); // 收起 → 取消选中
    }

    // 每帧刷新：重新印字 + 刷新血条 + 刷新状态栏图标
    void LateUpdate()
    {
        if (ship == null) return;
        text.text = expanded ? BuildExpandedText() : BuildDefaultText();

        // 展开时给状态栏让出位置；收起时文字用整块空间
        text.rectTransform.offsetMin = new Vector2(0f, expanded ? hpBarHeight + 80f : hpBarHeight + 12f);

        RefreshHpBar();
        RefreshBuffBar();
    }

    // 状态栏：★ 只在展开时显示，而且没 buff 时完全不显示（不占任何空间）
    void RefreshBuffBar()
    {
        if (buffBar == null) return;

        bool show = expanded && ship.buffs.Count > 0;   // ★ 收起 / 没buff → 什么都不显示
        buffBar.SetActive(show);
        if (!show) return;

        if (lastBuffCount != ship.buffs.Count)
        {
            RebuildBuffIcons();
            lastBuffCount = ship.buffs.Count;
        }
    }

    void RebuildBuffIcons()
    {
        for (int i = buffBarRt.childCount - 1; i >= 0; i--)
            Destroy(buffBarRt.GetChild(i).gameObject);

        for (int i = 0; i < ship.buffs.Count; i++)
        {
            BuffEntry b = ship.buffs[i];
            if (b == null) continue;

            GameObject iconGo = new GameObject("buff_" + b.buffName);
            iconGo.transform.SetParent(buffBarRt, false);
            RectTransform rt = iconGo.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(buffIconSize, buffIconSize);   // ★ 图标大小（Inspector 可调）

            Image img = iconGo.AddComponent<Image>();
            img.sprite = b.icon;                            // 卡牌上拖的图片
            img.color = b.icon != null ? Color.white
                                       : BuffTooltip.TypeColor(b.type);   // 没图 → 类型色块

            Button btn = iconGo.AddComponent<Button>();
            BuffEntry captured = b;
            RectTransform capturedRt = rt;
            btn.onClick.AddListener(() => OnBuffIconClicked(captured, capturedRt));
        }
    }

    // 点 buff 图标 → 屏幕最前方弹详情框（再点一次收起）
    void OnBuffIconClicked(BuffEntry b, RectTransform iconRt)
    {
        Camera cam = null;   // 覆盖层画布不用相机；主画布是 Camera/World 模式才需要
        Canvas c = GetComponentInParent<Canvas>();
        if (c != null && c.renderMode != RenderMode.ScreenSpaceOverlay) cam = c.worldCamera;

        BuffTooltip.Instance.Toggle(b, iconRt.position, cam);
    }

    // 血条：底槽铺满牌子宽度（左右留白），前景宽度 = 血量百分比
    void RefreshHpBar()
    {
        if (hpSlotRt == null || hpFill == null) return;

        float panelW = (transform as RectTransform).rect.width;
        hpSlotRt.offsetMin = new Vector2(hpBarPadding, 6f);
        hpSlotRt.offsetMax = new Vector2(-hpBarPadding, 6f + hpBarHeight);

        float full = Mathf.Max(0f, panelW - hpBarPadding * 2f);
        float ratio = ship.maxHp > 0 ? Mathf.Clamp01((float)ship.currentHp / ship.maxHp) : 0f;
        hpFill.rectTransform.sizeDelta = new Vector2(full * ratio, 0f);

        // 颜色：血多绿、中等黄、见底红
        hpFill.color = ratio > 0.5f ? new Color(0.25f, 0.85f, 0.35f)
                     : ratio > 0.25f ? new Color(1f, 0.78f, 0.2f)
                     : new Color(1f, 0.3f, 0.25f);
    }

    // withStats = true  → "战舰  85/100   攻 15  防 7"（收起时用：直接看到加成后的实际值）
    // withStats = false → "战舰  85/100"（展开时用：攻防在第 2 行有更详细的分解，这里不重复）
    string BuildDefaultText(bool withStats = true)
    {
        string s = ship.shipName + "  " + ship.currentHp + "/" + ship.maxHp;
        if (withStats)
            s += "   攻 " + ship.CurrentAttack + "  防 " + ship.CurrentDefense;
        return s;
    }

    string BuildExpandedText()
    {
        string line1 = BuildDefaultText(false);   // ★ 不带攻防，避免和第 2 行重复
        string line2 = "攻击 " + ShipStats.FormatStat(ship.baseAttack, ship.bonusAttack)
                     + "  防御 " + ShipStats.FormatStat(ship.baseDefense, ship.bonusDefense);
        // ★ 没有第 3 行了：buff 完全交给下方图标栏表达
        //   没 buff → 图标栏隐藏，这里也不显示任何文字
        return line1 + "\n" + line2;
    }
}
