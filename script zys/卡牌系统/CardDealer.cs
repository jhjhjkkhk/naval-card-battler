using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

// 发牌器（牌库版）—— 按主流卡牌游戏机制实现：
//
//   卡池配方(deckRecipe)
//        ↓ 按稀有度决定每种牌放几份
//   牌库 drawPile（洗匀）
//        ↓ 每回合抽 drawPerRound 张（从右下角牌库图标飞出）
//   手牌 Hand（无上限，牌多了自动变窄）
//        ↓ 打出
//   弃牌堆 discardPile
//        ↓ 牌库抽空 → 弃牌堆洗回牌库（永远有牌抽）
//
// 稀有度 = 份数：普通放 8 份、传说只放 1 份 → 传说自然难抽到
public class CardDealer : MonoBehaviour
{
    [Header("基础设置")]
    public GameObject cardPrefab;    // 单预制体模式的图纸（用了牌库配方就不用管它）
    public int cardCount = 5;        // 单预制体模式下的发牌数量
    public float cardWidth = 160f;   // 每张牌的宽度
    public float cardHeight = 220f;  // 每张牌的高度
    public float handPaddingX = 60f; // ★ 手牌左右各留多少像素（牌多时不会贴死屏幕边缘）
    public float bottomMargin = 20f; // 牌距离画面最下方的距离
    public bool clearHandOnDeal = true; // 每回合开始先清掉旧手牌（杀戮尖塔式）；不勾 = 手牌累积（无上限）

    [Header("发牌动画（从牌库图标飞出）")]
    public float growDuration = 0.15f;  // 出生：在牌库位置从 0 长到原大
    public float slideDuration = 0.25f; // 滑动：沿弧线飞到手牌位置
    public float dealInterval = 0.08f;  // 发完一张，等多久发下一张
    public float dealTilt = -25f;       // 飞行途中的倾斜角（0 = 不倾斜，负数 = 往左歪着飞入）
    public float openGapWait = 0.12f;   // 新牌起飞前，先等老牌滑开让位多久（杀戮尖塔的节奏感）

    [Header("★ 牌库配方")]
    public List<CardEntry> deckRecipe = new List<CardEntry>();  // 每种牌 + 放几份
    [Tooltip("各稀有度在牌库里的份数（普通/稀有/史诗/传说）。配方里 count 填 0 时按这个自动算")]
    public int[] rarityCounts = new int[] { 8, 4, 2, 1 };
    public bool shuffleOnStart = true;      // 开局洗牌
    public bool tintCardByRarity = false;   // 按稀有度给牌面染色（需要牌根物体上有 Image）

    [Header("牌库运行设置")]
    public RectTransform deckSlot;      // 右下角的牌库图标（放 Canvas 下，不要放进 CardZone）
    public float arcHeight = 60f;       // 飞行弧线高度（0 = 直线飞）
    public int drawPerRound = 4;        // 每回合抽几张（0 = 一次抽光整副牌）
    public bool autoReshuffle = true;   // 牌库抽空 → 弃牌堆洗回来（永远有牌）
    public bool shuffleOnReshuffle = true;

    // ── 运行时三堆牌 ──
    public List<GameObject> deck = new List<GameObject>();        // 完整牌表（配方展开结果，参考用）
    private List<GameObject> drawPile = new List<GameObject>();   // 抽牌堆：抽一张少一张
    private List<GameObject> discardPile = new List<GameObject>();// 弃牌堆

    private RectTransform canvasRect;
    private bool dealing = false;

    void Start()
    {
        canvasRect = GetComponentInParent<Canvas>().transform as RectTransform;
        BuildDeckFromRecipe();   // ① 配方 → 完整牌表
        ResetDrawPile();         // ② 全部塞进抽牌堆并洗匀
    }

    // ════════════ 牌库构建 ════════════

    // 把配方摊成牌表：count>0 用你填的份数；count=0 按卡牌自己的稀有度自动算份数
    void BuildDeckFromRecipe()
    {
        if (deckRecipe.Count == 0) return;   // 没填配方 → 沿用 Inspector 里手动拖的 deck

        deck = new List<GameObject>();
        foreach (CardEntry entry in deckRecipe)
        {
            if (entry.prefab == null) continue;

            int copies = entry.count > 0 ? entry.count : CopiesByRarity(entry.prefab);
            for (int i = 0; i < copies; i++) deck.Add(entry.prefab);
        }
        Debug.Log($"牌库已按配方生成：{deck.Count} 张（稀有度份数：普通{rarityCounts[0]}/稀有{rarityCounts[1]}/史诗{rarityCounts[2]}/传说{rarityCounts[3]}）");
    }

    // 这张牌该放几份？看它身上 CardDefinition 的稀有度
    int CopiesByRarity(GameObject prefab)
    {
        CardDefinition def = prefab.GetComponent<CardDefinition>();
        if (def == null) return rarityCounts[0];                  // 没定义就当普通卡
        int idx = (int)def.rarity;
        return (idx >= 0 && idx < rarityCounts.Length) ? rarityCounts[idx] : rarityCounts[0];
    }

    // 开局：整副牌进抽牌堆 + 洗匀
    void ResetDrawPile()
    {
        drawPile = new List<GameObject>(deck);
        discardPile = new List<GameObject>();
        if (shuffleOnStart) Shuffle(drawPile);
    }

    // ════════════ 抽牌 / 弃牌 / 重洗 ════════════

    // 从牌库顶抽一张（抽完从堆里移除）。没牌了自动重洗；重洗也没牌 → 返回 null
    GameObject DrawOne()
    {
        if (drawPile.Count == 0)
        {
            if (!autoReshuffle) return null;
            Reshuffle();
            if (drawPile.Count == 0) return null;
        }
        GameObject prefab = drawPile[0];
        drawPile.RemoveAt(0);       // 抽走了，牌库少一张
        return prefab;
    }

    // 弃牌堆洗回牌库
    void Reshuffle()
    {
        drawPile.AddRange(discardPile);
        discardPile.Clear();
        if (shuffleOnReshuffle) Shuffle(drawPile);
        Debug.Log($"牌库抽空 → 弃牌堆洗回，牌库恢复 {drawPile.Count} 张");
    }

    // CardDrag 打出成功后调用：这张牌的图纸进弃牌堆
    public void Discard(GameObject prefab)
    {
        if (prefab != null) discardPile.Add(prefab);
    }

    // 洗牌（Fisher-Yates：从后往前，每张随机和前面某张交换）
    void Shuffle(List<GameObject> pile)
    {
        for (int i = pile.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            GameObject tmp = pile[i];
            pile[i] = pile[j];
            pile[j] = tmp;
        }
    }

    // ════════════ 发牌主流程 ════════════

    public void DealCards()
    {
        if (dealing) return;    // 上一轮还没发完，忽略连点
        bool useDeck = deck.Count > 0;
        int count = useDeck ? (drawPerRound > 0 ? drawPerRound : deck.Count) : cardCount;
        StartCoroutine(DealRoutine(count, clearHandOnDeal));
    }

    // 抽牌卡专用：不清空手牌，从牌库额外抽 n 张（按牌库顺序一张张拿）
    public void DrawCards(int n)
    {
        if (n <= 0) return;
        if (dealing) { Debug.Log("正在发牌，抽牌卡稍后再用"); return; }
        StartCoroutine(DealRoutine(n, false));
    }

    IEnumerator DealRoutine(int count, bool clearFirst)
    {
        dealing = true;
        if (clearFirst) DestroyAllCards();

        bool useDeck = deck.Count > 0;

        // 出生点：右下角牌库图标（没拖图标就用画面右下角）
        float canvasH = canvasRect.rect.height;
        float canvasW = canvasRect.rect.width;
        Vector2 deckPos;
        if (deckSlot != null)
            deckPos = transform.InverseTransformPoint(deckSlot.position);
        else
            deckPos = new Vector2(canvasW * 0.5f - 90f, -canvasH * 0.5f + 90f);

        for (int i = 0; i < count; i++)
        {
            // ① 抽一张（牌库模式才抽；单预制体模式永远用 cardPrefab）
            GameObject prefabToUse = useDeck ? DrawOne() : cardPrefab;
            if (prefabToUse == null) break;    // 真没牌了，收工

            GameObject card = Instantiate(prefabToUse, transform);
            RectTransform rt = card.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            card.name = "Card_" + i;

            PrepCard(card, prefabToUse);   // 补组件、记来源、按稀有度染色

            // ② 先让全场重排（含这张新牌）：老牌平滑滑开让出空位，新牌自己瞬间定好座位
            RelayoutHand(true, card);
            Vector2 home = rt.anchoredPosition;

            // ③ 拉回牌库口，从 0 长大，再沿弧线飞到座位
            rt.anchoredPosition = deckPos;
            card.transform.localScale = Vector3.zero;
            card.transform.localRotation = Quaternion.Euler(0, 0, dealTilt);

            // 先给老牌一点时间把位置让出来，新牌再起飞（不让位就飞进去会显得很挤）
            if (openGapWait > 0f) yield return new WaitForSeconds(openGapWait);

            yield return StartCoroutine(AnimateScale(card.transform, Vector3.one, growDuration));
            yield return StartCoroutine(AnimateFlyIn(rt, home, slideDuration, arcHeight, dealTilt));

            yield return new WaitForSeconds(dealInterval);
        }

        RelayoutHand(true);   // 收尾：全体再对齐一次（有动画保护，不会跳）
        dealing = false;
    }

    // 给新牌补零件：缺啥装啥 + 记住它来自哪张图纸（打出后要进弃牌堆）
    void PrepCard(GameObject card, GameObject prefab)
    {
        if (card.GetComponent<CardDrag>() == null) card.AddComponent<CardDrag>();
        if (card.GetComponent<CardHover>() == null) card.AddComponent<CardHover>();

        CardDrag drag = card.GetComponent<CardDrag>();
        drag.sourcePrefab = prefab;
        drag.dealer = this;

        if (tintCardByRarity)
        {
            CardDefinition def = card.GetComponent<CardDefinition>();
            Image img = card.GetComponent<Image>();
            if (img == null) img = card.GetComponentInChildren<Image>();
            if (def != null && img != null) img.color = def.RarityColor;
        }
    }

    // ════════════ 手牌排布 ════════════

    // 手牌全部重新排队：等距排开、牌多了自动变窄（手牌无上限也不会挤出画面）
    // animate    = true  → 老牌平滑滑到新座位（杀戮尖塔那种"让位"效果），不瞬移
    // instantFor = 某张牌 → 只有这张瞬间到位（通常是刚抽出来、马上要从牌库飞进来的那张）
    public void RelayoutHand(bool animate = true, GameObject instantFor = null)
    {
        int n = transform.childCount;
        if (n == 0) return;

        float canvasW = canvasRect.rect.width;
        float canvasH = canvasRect.rect.height;

        // ★ 可用宽度 = 画布宽 - 左右内边距（否则牌一多就铺满整屏、最边上的牌贴死屏幕边缘）
        float usableW = canvasW - handPaddingX * 2f;
        if (usableW < 50f) usableW = 50f;

        float width = cardWidth;
        if (n * width > usableW) width = usableW / n;   // 牌太多 → 变瘦，保证全塞得下

        float totalWidth = n * width;
        float y = -canvasH * 0.5f + bottomMargin + cardHeight * 0.5f;

        for (int i = 0; i < n; i++)
        {
            RectTransform rt = transform.GetChild(i) as RectTransform;
            if (rt == null) continue;

            float x = -totalWidth * 0.5f + width * 0.5f + i * width;
            Vector2 home = new Vector2(x, y);
            Vector2 size = new Vector2(width, cardHeight);

            // 告诉悬停动画"家"在哪（拖拽飞回也用这个）
            CardHover hover = rt.GetComponent<CardHover>();
            if (hover != null) hover.SetHome(home, size);

            // 正在被拖的牌不要抢它位置
            CardDrag drag = rt.GetComponent<CardDrag>();
            if (drag != null && drag.IsDragging) continue;

            // 刚抽出来的那张：位置先算准（马上要被拉回牌库口起飞），不参与滑行
            if (instantFor != null && rt.gameObject == instantFor)
            {
                if (hover != null) hover.MoveHome(false);
                else { rt.anchoredPosition = home; rt.sizeDelta = size; }
                continue;
            }

            if (hover != null) hover.MoveHome(animate);   // 平滑滑过去
            else { rt.anchoredPosition = home; rt.sizeDelta = size; }
        }
    }

    // CardDrag 打出成功后调用：图纸进弃牌堆 + 立刻脱离手牌区 + 剩下的牌补位
    public void OnCardPlayed(GameObject cardInstance, GameObject prefab)
    {
        Discard(prefab);
        cardInstance.transform.SetParent(null);  // 立刻不是本区孩子了（Destroy 是延迟的）
        RelayoutHand(true);                      // 剩下的牌平滑补上空缺（不瞬移）
    }

    // ════════════ 查询接口（牌库图标角标 / 查看面板用）════════════

    public int RemainingCount { get { return drawPile.Count; } }   // 牌库还剩几张
    public int DiscardCount { get { return discardPile.Count; } }  // 弃牌堆几张

    public List<GameObject> GetRemainingCards()
    {
        return new List<GameObject>(drawPile);   // 当前牌库里剩下的（顺序就是接下来的抽牌顺序）
    }

    // 弃牌堆里最近打出去的牌（最新的排在最前），最多给 maxCount 张（给"牌库面板"展示用）
    public List<GameObject> GetDiscardCards(int maxCount)
    {
        List<GameObject> result = new List<GameObject>();
        for (int i = discardPile.Count - 1; i >= 0 && result.Count < maxCount; i--)
            result.Add(discardPile[i]);
        return result;
    }

    // ════════════ 清场 ════════════

    public void ClearCards()
    {
        StopAllCoroutines();
        dealing = false;
        DestroyAllCards();
    }

    void DestroyAllCards()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform t = transform.GetChild(i);

            // ★ 没打出的手牌：销毁前先把它的"图纸"送进弃牌堆
            //   以前是直接 Destroy —— 这些牌凭空消失，导致牌库越打越少、最后抽不出牌
            CardDrag d = t.GetComponent<CardDrag>();
            if (d != null && d.sourcePrefab != null) Discard(d.sourcePrefab);

            Destroy(t.gameObject);
        }
    }

    // ════════════ 动画 ════════════

    IEnumerator AnimateScale(Transform t, Vector3 target, float duration)
    {
        Vector3 start = t.localScale;
        float time = 0f;
        while (time < duration)
        {
            time += Time.deltaTime;
            float p = Mathf.Clamp01(time / duration);
            t.localScale = Vector3.Lerp(start, target, p);
            yield return null;
        }
        t.localScale = target;
    }

    // 沿弧线飞到目标位置，同时把倾斜角扳正（像被"甩"进手牌里）
    IEnumerator AnimateFlyIn(RectTransform rt, Vector2 target, float duration, float arc, float tilt)
    {
        Vector2 start = rt.anchoredPosition;
        float time = 0f;
        while (time < duration)
        {
            time += Time.deltaTime;
            float p = Mathf.Clamp01(time / duration);
            Vector2 pos = Vector2.Lerp(start, target, p);
            pos.y += Mathf.Sin(p * Mathf.PI) * arc;                       // 弧线：先升高再落下
            rt.anchoredPosition = pos;
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(tilt, 0f, p)); // 倾斜 → 扳正
            yield return null;
        }
        rt.anchoredPosition = target;
        rt.localRotation = Quaternion.identity;
    }

    // ════════════ 配方小格子 ════════════

    [System.Serializable]
    public class CardEntry
    {
        public GameObject prefab;   // 这种牌的预制体
        [Tooltip("在牌库里放几份；填 0 = 按这张牌自己的稀有度自动算")]
        public int count = 0;
    }
}