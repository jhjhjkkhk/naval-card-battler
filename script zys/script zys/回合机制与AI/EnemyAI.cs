using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 敌方 AI：和玩家一样拥有【自己的牌库 + 手牌 + 行动值】，回合里自动抽牌、自动出牌
//
//   敌方回合开始 → 从自己的牌库抽 N 张
//   → 每张牌消耗 1 点行动值，打给合法目标
//   → 行动值耗尽 / 没牌可打 → 自动结束回合
//
// 和玩家的区别：敌方的"手牌"只是逻辑上的列表（存预制体引用），不显示在屏幕上
// 挂在场景里任意物体上，全场只需要 1 个
public class EnemyAI : MonoBehaviour
{
    public static EnemyAI Instance { get; private set; }

    [Header("敌方牌库配方（填法和 CardDealer 里一样）")]
    public List<CardDealer.CardEntry> deckRecipe = new List<CardDealer.CardEntry>();
    [Tooltip("各稀有度的份数（普通/稀有/史诗/传说）。配方里 count 填 0 时按这个算")]
    public int[] rarityCounts = new int[] { 8, 4, 2, 1 };

    [Header("抽牌设置")]
    public int drawPerTurn = 4;      // 敌方每回合开始抽几张
    public int maxHandSize = 8;      // 敌方手牌上限（满了就不再抽）

    [Header("AI 节奏")]
    public float actionDelay = 0.8f; // 每张牌之间停多久（让玩家看清发生了什么）

    // 三堆牌 + 手牌（我方 CardDealer 的同款结构，但完全独立）
    private List<GameObject> deck = new List<GameObject>();
    private List<GameObject> drawPile = new List<GameObject>();
    private List<GameObject> discardPile = new List<GameObject>();
    private List<GameObject> hand = new List<GameObject>();   // 逻辑手牌：存的是预制体，不生成实体

    public int HandCount { get { return hand.Count; } }        // 敌方手牌几张（调试用）
    public int DrawPileCount { get { return drawPile.Count; } }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        BuildDeck();
    }

    // ══════════ 牌库构建 ══════════

    void BuildDeck()
    {
        deck = new List<GameObject>();
        foreach (CardDealer.CardEntry entry in deckRecipe)
        {
            if (entry == null || entry.prefab == null) continue;
            int copies = entry.count > 0 ? entry.count : CopiesByRarity(entry.prefab);
            for (int i = 0; i < copies; i++) deck.Add(entry.prefab);
        }

        drawPile = new List<GameObject>(deck);
        Shuffle(drawPile);
        Debug.Log($"[敌方] 牌库已生成：{deck.Count} 张" +
                  (deck.Count == 0 ? "（★ 没填配方，敌方将无牌可打）" : ""));
    }

    int CopiesByRarity(GameObject prefab)
    {
        CardDefinition def = prefab.GetComponent<CardDefinition>();
        if (def == null) return rarityCounts[0];
        int idx = (int)def.rarity;
        return (idx >= 0 && idx < rarityCounts.Length) ? rarityCounts[idx] : rarityCounts[0];
    }

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

    // ══════════ 敌方回合主流程（由 TurnManager 调用）══════════

    public IEnumerator RunTurn(int actionPoints)
    {
        int ap = actionPoints;

        DrawCards(drawPerTurn);   // 回合开始：抽牌
        Debug.Log($"[敌方] 抽牌后手牌 {hand.Count} 张，行动值 {ap}");

        yield return new WaitForSeconds(actionDelay);

        while (ap > 0)
        {
            if (!TryPlayOneCard())
            {
                Debug.Log($"[敌方] 没有能打出的牌，提前结束回合（剩余行动值 {ap} 浪费）");
                break;
            }

            ap--;                                              // 出一张扣一点
            if (TurnManager.Instance != null) TurnManager.Instance.SetAP(ap);   // 左下角数字跟着减
            yield return new WaitForSeconds(actionDelay);
        }
    }

    // ══════════ 抽牌 ══════════

    void DrawCards(int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (hand.Count >= maxHandSize)
            {
                Debug.Log($"[敌方] 手牌已满（{maxHandSize} 张），停止抽牌");
                break;
            }

            // 牌库空了 → 弃牌堆洗回来（和玩家一样，永远有牌）
            if (drawPile.Count == 0)
            {
                if (discardPile.Count == 0) break;   // 真没牌了
                drawPile.AddRange(discardPile);
                discardPile.Clear();
                Shuffle(drawPile);
                Debug.Log($"[敌方] 牌库抽空 → 弃牌堆洗回 {drawPile.Count} 张");
            }

            hand.Add(drawPile[0]);
            drawPile.RemoveAt(0);
        }
    }

    // ══════════ 出牌决策 ══════════

    // 尝试打出一张牌：从所有"能合法打出"的牌里随机挑一张
    // 返回 true = 成功打出（调用方扣行动值）
    bool TryPlayOneCard()
    {
        // ① 先收集所有可行的（手牌下标 + 目标）组合
        List<int> playableIdx = new List<int>();
        List<ShipStats> playableTarget = new List<ShipStats>();

        for (int i = 0; i < hand.Count; i++)
        {
            GameObject prefab = hand[i];
            if (prefab == null) continue;
            CardDefinition def = prefab.GetComponent<CardDefinition>();
            if (def == null) continue;

            // 抽牌卡：不需要目标，永远能打
            if (def.type == CardDefinition.CardType.Draw)
            {
                playableIdx.Add(i);
                playableTarget.Add(null);
                continue;
            }

            ShipStats t = FindTarget(def);
            if (t != null)
            {
                playableIdx.Add(i);
                playableTarget.Add(t);
            }
        }

        if (playableIdx.Count == 0) return false;   // 一张都打不出

        // ② 依次尝试：随机挑一个，★ 先结算再动牌——失败就换下一个候选
        //    （以前是先把手牌移除再结算，万一失败就变成"牌丢了 + 行动值白扣 + 效果没生效"）
        while (playableIdx.Count > 0)
        {
            int pick = Random.Range(0, playableIdx.Count);
            int idx = playableIdx[pick];
            ShipStats target = playableTarget[pick];

            GameObject chosen = hand[idx];
            CardDefinition chosenDef = chosen != null ? chosen.GetComponent<CardDefinition>() : null;

            if (chosenDef == null)   // 数据坏了，跳过这张
            { playableIdx.RemoveAt(pick); playableTarget.RemoveAt(pick); continue; }

            // ③-A 抽牌卡：不需要目标，一定成功
            if (target == null)
            {
                hand.RemoveAt(idx);
                discardPile.Add(chosen);                    // 打出去的牌进敌方弃牌堆
                int n = Mathf.Max(1, chosenDef.value);
                DrawCards(n);
                Debug.Log($"[敌方] 打出【{chosenDef.cardName}】→ 抽 {n} 张（手牌 {hand.Count}）");
                return true;
            }

            // ③-B 普通卡：先试着结算，成功了才把手牌移除进弃牌堆
            // ★ 第二个参数 true = "出牌的是敌方"，这样阵营规则才对（增益打自己人，减益打玩家）
            string reason;
            if (target.TryApplyCard(chosenDef, true, out reason))
            {
                hand.RemoveAt(idx);
                discardPile.Add(chosen);
                Debug.Log($"[敌方] 打出【{chosenDef.cardName}】→ {target.shipName}（攻击{target.CurrentAttack} 防御{target.CurrentDefense} 血{target.currentHp}）");
                return true;
            }

            // 打不出去（罕见）：牌留在手上，也不扣行动值，换下一张试试
            Debug.Log($"[敌方] {chosenDef.cardName} 打不出去：{reason}，换一张");
            playableIdx.RemoveAt(pick);
            playableTarget.RemoveAt(pick);
        }

        return false;   // 所有候选都试过，一张都打不出
    }

    // 这张牌该打给谁？（挑最优目标，不是随机）
    //   阵营：增益卡（AttackUp/DefenseUp/Heal）→ 打敌方自己人（isEnemy = true）
    //         减益卡（AttackDown/DefenseDown） → 打玩家的船（isEnemy = false）
    //   排序：回血 → 血最少的；攻击类 → 攻击最高的；防御类 → 防御最高的
    ShipStats FindTarget(CardDefinition def)
    {
        bool wantOwnSide = def.IsBuff;    // 敌方的"自己人"就是 isEnemy == true 的船

        List<ShipStats> pool = new List<ShipStats>();
        foreach (ShipStats s in FindObjectsOfType<ShipStats>())
        {
            if (s == null || s.currentHp <= 0) continue;              // 死船不打
            if (s.isEnemy != wantOwnSide) continue;                   // 阵营不对
            // 治疗卡：满血的跳过（浪费）
            if (def.type == CardDefinition.CardType.Heal && s.currentHp >= s.maxHp) continue;
            pool.Add(s);
        }

        if (pool.Count == 0) return null;

        // ★ 挑"最优目标"：按卡牌类型排序，排完取第一个（不再是随机）
        //   规则：回血给血最少的；攻击类给攻击最高的；防御类给防御最高的
        switch (def.type)
        {
            case CardDefinition.CardType.Heal:
                // 血最少的最先奶（升序：血少的排前面）
                pool.Sort((a, b) => a.currentHp.CompareTo(b.currentHp));
                break;

            case CardDefinition.CardType.AttackUp:      // 给自己人加攻 → 加给攻击最高的
            case CardDefinition.CardType.AttackDown:    // 削玩家攻击 → 削攻击最高的那个威胁
                // 降序：攻击高的排前面
                pool.Sort((a, b) => b.CurrentAttack.CompareTo(a.CurrentAttack));
                break;

            case CardDefinition.CardType.DefenseUp:     // 给自己人加防 → 加给防御最高的
            case CardDefinition.CardType.DefenseDown:   // 削玩家防御 → 削防御最高的
                // 降序：防御高的排前面
                pool.Sort((a, b) => b.CurrentDefense.CompareTo(a.CurrentDefense));
                break;
        }

        return pool[0];   // 排完序，第一个就是最优目标
    }
}
