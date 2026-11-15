using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 回合制 + 行动值系统
//
//   我方回合开始 → 行动值 = 3，自动发牌
//   每打出一张卡 → 消耗 1 点行动值
//   行动值归零   → 不能再出牌，只能点"回合结束"按钮
//   点击回合结束 → 敌方 AI 回合（自动行动）
//   敌方行动值耗尽 → 自动结束，交还我方（我方行动值重置为 3 + 自动发牌）
//
// 挂在场景里任意物体上，全场只需要 1 个
public class TurnManager : MonoBehaviour
{
    public enum Side { Player, Enemy }

    public static TurnManager Instance { get; private set; }   // 全局入口：别的脚本问我"现在能不能出牌"

    [Header("行动值设置")]
    public int playerMaxAP = 3;    // 我方每回合几点行动值
    public int enemyMaxAP = 3;     // 敌方每回合几点行动值（填 0 = 敌方回合直接跳过，不做任何行动）

    [Header("接线")]
    public CardDealer dealer;      // 发牌器（我方回合开始自动发牌）
    public Text apText;            // 左下角行动值文字（不拖就自动造一个）
    public Button endTurnButton;   // "回合结束"按钮（不拖就自动造一个）

    [Header("敌方 AI 设置")]
    public float enemyActionDelay = 0.8f;  // 敌方每次行动之间停多久（让玩家看清发生了什么）

    public Side CurrentSide { get; private set; }   // 现在是谁的回合
    public int CurrentAP { get; private set; }      // 当前这方还剩几点行动值

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (dealer == null) dealer = FindObjectOfType<CardDealer>();
        SetupAPText();
        SetupEndTurnButton();

        // 等一帧再开第一回合：确保 CardDealer.Start() 已经把牌库建好了
        StartCoroutine(DelayedFirstTurn());
    }

    IEnumerator DelayedFirstTurn()
    {
        yield return null;
        StartPlayerTurn();
    }

    // ══════════ 查询：现在能不能出牌？══════════

    // 只有"我方回合 + 还有行动值"才能出牌
    public bool CanPlayerAct()
    {
        return CurrentSide == Side.Player && CurrentAP > 0;
    }

    public bool IsPlayerTurn { get { return CurrentSide == Side.Player; } }

    // 出牌成功后调用：扣 1 点行动值
    public void SpendAP()
    {
        if (CurrentAP > 0) CurrentAP--;
        RefreshAPUI();
    }

    // 直接把行动值设成某个数（敌方 AI 出牌时用它更新左下角显示）
    public void SetAP(int value)
    {
        CurrentAP = value;
        RefreshAPUI();
    }

    // ══════════ 我方回合 ══════════

    // ★ 我方回合一开始调用：所有船（我方 + 敌方）的 buff 统一 -1，归零的消除
    //   只在【我方回合开始】结算这一次 —— 敌方回合不结算，
    //   这样我方给敌方上的减益能完整覆盖一个敌方回合，不会一进敌方回合就蒸发
    void TickAllBuffs()
    {
        ShipStats[] all = FindObjectsOfType<ShipStats>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null)
                all[i].TickBuffs();
        }
    }

    public void StartPlayerTurn()
    {
        CurrentSide = Side.Player;
        CurrentAP = playerMaxAP;
        Debug.Log("=== 我方回合开始，行动值 " + CurrentAP + " ===");

        TickAllBuffs();   // ★ 我方回合开始 → 所有船的 buff 剩余回合 -1，到 0 的消除

        if (dealer != null) dealer.DealCards();   // ★ 自动发牌，不用点按钮
        RefreshAPUI();
    }

    // 点"回合结束"按钮 → 调这个
    public void EndPlayerTurn()
    {
        if (CurrentSide != Side.Player)
        {
            Debug.Log("现在不是我方回合，点结束无效");
            return;
        }
        Debug.Log("=== 我方回合结束 ===");
        StartEnemyTurn();
    }

    // ══════════ 敌方回合（AI 自动打完）══════════

    void StartEnemyTurn()
    {
        CurrentSide = Side.Enemy;
        CurrentAP = enemyMaxAP;

        // ★ 敌方回合不再结算 buff：统一在我方回合开始时扣，避免减益刚上就被清掉
        // 敌方行动值 = 0 → 敌方什么都不做，立刻把回合交还给我方
        // （AI 代码还在下面，以后把 Enemy Max AP 调成 3 就能启用）
        if (CurrentAP <= 0)
        {
            Debug.Log("=== 敌方回合：行动值 0，直接跳过 ===");
            StartPlayerTurn();
            return;
        }

        Debug.Log("=== 敌方回合开始，行动值 " + CurrentAP + " ===");
        RefreshAPUI();

        // 场景里有 EnemyAI → 让它抽牌、打牌；没挂 EnemyAI → 退回原来的"随机攻击"
        if (EnemyAI.Instance != null)
            StartCoroutine(EnemyCardTurn());
        else
            StartCoroutine(EnemyTurnRoutine());
    }

    // 敌方 AI 的回合：整回合交给 EnemyAI 跑，它打完（或没牌可打）后自动还给我方
    IEnumerator EnemyCardTurn()
    {
        yield return StartCoroutine(EnemyAI.Instance.RunTurn(enemyMaxAP));
        Debug.Log("=== 敌方回合结束，交还我方 ===");
        StartPlayerTurn();
    }

    IEnumerator EnemyTurnRoutine()
    {
        yield return new WaitForSeconds(enemyActionDelay);   // 先顿一下，让玩家反应过来

        while (CurrentAP > 0)
        {
            EnemyActOnce();          // 敌方行动一次
            CurrentAP--;
            RefreshAPUI();
            yield return new WaitForSeconds(enemyActionDelay);
        }

        Debug.Log("=== 敌方行动值耗尽，自动结束回合 ===");
        StartPlayerTurn();           // ★ 自动交还给我方（我方行动值重置 + 自动发牌）
    }

    // 敌方行动一次：随机挑一艘活着的敌船，攻击一艘活着的友船
    void EnemyActOnce()
    {
        List<ShipStats> enemies = new List<ShipStats>();
        List<ShipStats> allies = new List<ShipStats>();

        foreach (ShipStats s in FindObjectsOfType<ShipStats>())
        {
            if (s == null || s.currentHp <= 0) continue;   // 死船不参与
            if (s.isEnemy) enemies.Add(s);
            else allies.Add(s);
        }

        if (enemies.Count == 0 || allies.Count == 0) return;   // 有一边死光了，跳过

        ShipStats attacker = enemies[Random.Range(0, enemies.Count)];
        ShipStats target = allies[Random.Range(0, allies.Count)];

        // 伤害 = 攻击力 - 防御力，至少 1 点
        int dmg = Mathf.Max(1, attacker.CurrentAttack - target.CurrentDefense);
        target.currentHp = Mathf.Max(0, target.currentHp - dmg);

        Debug.Log($"[敌方行动] {attacker.shipName} 攻击 {target.shipName}，造成 {dmg} 伤害 → " +
                  $"{target.shipName} 剩余 {target.currentHp}");
    }

    // ══════════ UI：左下角行动值 ══════════

    void SetupAPText()
    {
        if (apText != null) return;              // 已经拖了就用你拖的
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        GameObject go = new GameObject("行动值");
        go.transform.SetParent(canvas.transform, false);

        apText = go.AddComponent<Text>();
        apText.font = UiFont.Get();
        apText.fontSize = 34;
        apText.alignment = TextAnchor.LowerLeft;

        RectTransform rt = apText.rectTransform;
        rt.anchorMin = new Vector2(0f, 0f);      // 左下角
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(30f, 30f);
        rt.sizeDelta = new Vector2(420f, 90f);
    }

    void RefreshAPUI()
    {
        if (apText != null)
        {
            string who = CurrentSide == Side.Player ? "我方" : "敌方";
            apText.text = $"{who}回合\n行动值：{CurrentAP}";
            apText.color = CurrentSide == Side.Player ? Color.yellow : Color.red;
        }

        // 敌方回合时把"回合结束"按钮变灰（点了也没用）
        if (endTurnButton != null)
            endTurnButton.interactable = IsPlayerTurn;
    }

    // ══════════ UI：右下角"回合结束"按钮 ══════════

    void SetupEndTurnButton()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        // 已经拖了按钮 → 只负责接线，不新造
        if (endTurnButton != null)
        {
            endTurnButton.onClick.RemoveAllListeners();
            endTurnButton.onClick.AddListener(EndPlayerTurn);
            return;
        }

        GameObject go = new GameObject("回合结束按钮");
        go.transform.SetParent(canvas.transform, false);

        Image img = go.AddComponent<Image>();
        img.color = new Color(0.85f, 0.35f, 0.20f, 0.95f);   // 橙红色，醒目

        endTurnButton = go.AddComponent<Button>();
        endTurnButton.targetGraphic = img;
        endTurnButton.onClick.AddListener(EndPlayerTurn);      // ★ 点它 = 结束回合

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);    // 右下角
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-30f, 30f);
        rt.sizeDelta = new Vector2(190f, 70f);

        // 按钮上的文字
        GameObject txtGo = new GameObject("文字");
        txtGo.transform.SetParent(go.transform, false);
        Text txt = txtGo.AddComponent<Text>();
        txt.font = UiFont.Get();
        txt.text = "回合结束";
        txt.fontSize = 28;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleCenter;

        RectTransform trt = txt.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
    }
}
