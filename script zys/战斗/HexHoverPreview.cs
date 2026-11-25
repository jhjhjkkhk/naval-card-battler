using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ============================================================================
// 格子悬停交互（常驻，全地图）
//
// 不论什么模式，鼠标指到哪一格，就描出那一格的边框：
//   移动模式（默认）  白色描边 + 显示距离 / 能否开到
//   攻击模式          黄色（可打）/ 灰色（不可打）+ 穿深与预计伤害
//
// 选中船之后才有"距离 / 可达 / 伤害"这些信息；没选船时只描边。
// ============================================================================
public class HexHoverPreview : MonoBehaviour
{
    [Header("高度")]
    public float seaLevel = 10f;
    public float heightAboveSea = 1.15f;

    [Header("UI")]
    public int fontSize = 26;
    public Color normalColor = new Color(1f, 1f, 1f, 0.95f);
    public Color badColor = new Color(1f, 0.55f, 0.4f, 0.95f);
    public Color infoColor = new Color(0.8f, 0.9f, 1f, 0.95f);

    [Header("描边颜色")]
    public Color moveColor = new Color(1f, 0.85f, 0.25f, 0.75f);   // 移动模式：黄色
    public Color moveReachable = new Color(0.55f, 1f, 0.8f, 0.9f); // 移动模式：可达
    public Color attackOk = new Color(1f, 0.9f, 0.3f, 1f);         // 攻击模式：可打
    public Color attackBad = new Color(0.55f, 0.55f, 0.55f, 1f);   // 攻击模式：不可打

    Text text;
    LineRenderer hoverLine;
    Material lineMat;

    HexGrid grid;
    AttackRangeVisualizer rangeVis;
    ShipStats attacker;
    ShellSpec shell;
    Hex anchor;

    bool attackMode;

    void Awake()
    {
        grid = HexGrid.Instance != null ? HexGrid.Instance : FindObjectOfType<HexGrid>();
    }

    void Start()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas != null)
        {
            GameObject go = new GameObject("HexHoverText");
            go.transform.SetParent(canvas.transform, false);

            text = go.AddComponent<Text>();
            text.font = UiFont.Get();
            text.fontSize = fontSize;
            text.alignment = TextAnchor.LowerLeft;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform rt = text.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(30f, 140f);
            rt.sizeDelta = new Vector2(480f, 180f);
            rt.gameObject.SetActive(false);
        }

        GameObject lineGo = new GameObject("HoverHexLine");
        lineGo.transform.SetParent(null);
        hoverLine = lineGo.AddComponent<LineRenderer>();
        hoverLine.useWorldSpace = true;
        hoverLine.loop = true;
        hoverLine.positionCount = 0;
        hoverLine.widthMultiplier = 0.55f;
        lineMat = CombatVisualUtil.CreateUnlit(moveColor);
        hoverLine.material = lineMat;
        hoverLine.startColor = moveColor;
        hoverLine.endColor = moveColor;
        hoverLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        hoverLine.receiveShadows = false;
    }

    // ------------------------------------------------------------------ 外部

    /// <summary>攻击模式：传入开火船与圆心</summary>
    public void SetAttackContext(ShipStats ship, Vector3 anchorPos, ShellSpec shellSpec, AttackRangeVisualizer vis)
    {
        attacker = ship;
        shell = shellSpec;
        rangeVis = vis;
        attackMode = true;
        if (grid != null) anchor = grid.WorldToHex(anchorPos);
    }

    /// <summary>退出攻击模式：回到普通悬停</summary>
    public void ClearAttackContext()
    {
        attackMode = false;
        rangeVis = null;
        shell = null;
    }

    public void Hide()
    {
        if (text != null) text.gameObject.SetActive(false);
        if (hoverLine != null) hoverLine.positionCount = 0;
    }

    void Update()
    {
        if (grid == null) return;

        Hex hex;
        if (!TryHexUnderMouse(out hex)) { Hide(); return; }

        Color lineColor;
        string body;

        if (attackMode && attacker != null && rangeVis != null)
        {
            string reason;
            bool ok = rangeVis.IsAimAllowed(hex, out reason);
            lineColor = ok ? attackOk : attackBad;
            body = AttackInfo(hex, ok, reason);
        }
        else
        {
            ShipStats sel = ShipSelector.Selected;
            bool reachable = false;
            int dist = 0;

            if (sel != null)
            {
                ShipMover mv = sel.GetComponent<ShipMover>();
                if (mv != null)
                {
                    Hex from = grid.WorldToHex(sel.transform.position);
                    dist = HexGrid.Distance(from, hex);
                    reachable = mv.CanReach(hex);
                    anchor = from;
                }
            }

            lineColor = reachable ? moveReachable : moveColor;
            body = MoveInfo(hex, sel, dist, reachable);
        }

        SetLineColor(lineColor);
        DrawHover(hex);
        ShowText(body, lineColor);
    }

    void SetLineColor(Color c)
    {
        if (hoverLine == null) return;
        hoverLine.startColor = c;
        hoverLine.endColor = c;
        if (lineMat != null && lineMat.HasProperty("_BaseColor"))
            lineMat.SetColor("_BaseColor", c);
    }

    bool TryHexUnderMouse(out Hex hex)
    {
        hex = default(Hex);

        Camera cam = Camera.main;
        if (cam == null) return false;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        Vector3 point;

        if (Physics.Raycast(ray, out hit, 10000f)) point = hit.point;
        else
        {
            Plane plane = new Plane(Vector3.up, new Vector3(0f, seaLevel, 0f));
            float d;
            if (!plane.Raycast(ray, out d)) return false;
            point = ray.GetPoint(d);
        }

        hex = grid.WorldToHex(point);
        return true;
    }

    void DrawHover(Hex hex)
    {
        if (hoverLine == null) return;

        Vector3 c = grid.HexToWorld(hex);
        float r = grid.hexSize * 0.94f;
        float y = seaLevel + heightAboveSea;

        hoverLine.positionCount = 6;
        for (int k = 0; k < 6; k++)
        {
            float a = Mathf.Deg2Rad * (60f * k);
            hoverLine.SetPosition(k, new Vector3(c.x + Mathf.Cos(a) * r, y, c.z + Mathf.Sin(a) * r));
        }
    }

    // ------------------------------------------------------------------ 文案

    string MoveInfo(Hex hex, ShipStats sel, int dist, bool reachable)
    {
        if (sel == null) return "格子 " + hex;

        string s = "格子 " + hex + "   距离 " + dist + " 格\n";
        if (reachable) s += "可开到";
        else s += "开不到（超出移动力或拐不过去）";

        // 有敌舰就顺便报一下
        List<ShipStats> foes = EnemiesAt(hex, sel);
        if (foes.Count > 0) s += "\n" + foes[0].shipName + "（" + foes[0].currentHp + "/" + foes[0].maxHp + "）";

        return s;
    }

    string AttackInfo(Hex hex, bool allowed, string reason)
    {
        int dist = HexGrid.Distance(anchor, hex);
        int barrels = Mathf.Max(1, attacker.TotalBarrels);

        float spacing = grid.hexSize * Mathf.Sqrt(3f);
        float radius = SalvoCalculator.ScatterRadiusWorld(dist, spacing);
        ShipDebuffs deb = attacker.GetComponent<ShipDebuffs>();
        if (deb != null) radius *= deb.ScatterMultiplier;

        float sigma = Mathf.Max(radius * 0.5f, 0.01f);
        float dHex = spacing * 0.5f;
        float pTarget = 1f - Mathf.Exp(-(dHex * dHex) / (2f * sigma * sigma));
        float pNear = 1f - Mathf.Exp(-(9f * dHex * dHex) / (2f * sigma * sigma));

        float expectedHits = barrels * pTarget;
        float expectedNear = Mathf.Max(0f, barrels * (pNear - pTarget));

        string head = "格子 " + hex + "   距离 " + dist + " 格";

        if (!allowed) return head + "\n" + reason;

        List<ShipStats> foes = EnemiesAt(hex, attacker);
        if (foes.Count == 0)
        {
            // 格子上没船，但隔壁有船 → 这格打下去会变成近失弹
            ShipStats near = NearestEnemy(hex, attacker, 1);
            if (near != null)
            {
                float nearDmg = expectedNear * near.maxHp * 0.01f;
                return head + "\n"
                     + "邻近 " + near.shipName + "（" + near.currentHp + "/" + near.maxHp + "）\n"
                     + "无直接命中，预计近失 " + expectedNear.ToString("0.0") + " 发\n"
                     + "预计伤害 " + nearDmg.ToString("0") + "（1% 溅射，可能漏水）";
            }

            return head + "\n空海面   预计 " + expectedHits.ToString("0.0") + " 发落点";
        }

        ShipStats t = foes[0];
        Vector3 dir = grid.HexToWorld(hex) - attacker.transform.position;
        float angle = CombatMath.ImpactAngle(dir, t.transform.forward);

        float pen = CombatMath.Penetration(attacker.basePenetration * shell.penetrationScale,
                                           dist, attacker.maxRangeHex);
        float effBelt = CombatMath.EffectiveArmor(t.armorBelt, angle, shell.normalization);
        float effDeck = CombatMath.EffectiveArmor(t.armorDeck, angle, shell.normalization);

        bool penBelt = pen >= effBelt || CombatMath.IsOvermatch(pen, t.armorBelt);
        bool penDeck = pen >= effDeck || CombatMath.IsOvermatch(pen, t.armorDeck);

        float avgDmg = attacker.baseDamage * shell.damageScale;
        float expDmg = expectedHits * (penBelt ? avgDmg : 0f) + expectedNear * t.maxHp * 0.01f;

        return head + "\n"
             + t.shipName + "（" + t.currentHp + "/" + t.maxHp + "）  迎弹角 " + angle.ToString("0") + "°\n"
             + "穿深 " + pen.ToString("0") + " / 装甲带 " + effBelt.ToString("0") + "\n"
             + "预计命中 " + expectedHits.ToString("0.0") + " 发，近失 " + expectedNear.ToString("0.0") + " 发\n"
             + (penBelt ? "可穿水线" : "穿不了水线")
             + (penDeck ? " / 可穿甲板" : " / 穿不了甲板")
             + "   预计伤害 " + expDmg.ToString("0");
    }

    void ShowText(string body, Color color)
    {
        if (text == null) return;
        text.text = body;
        text.color = color;
        text.gameObject.SetActive(true);
    }

    ShipStats NearestEnemy(Hex hex, ShipStats self, int maxDist)
    {
        ShipStats best = null;
        int bestD = int.MaxValue;

        foreach (ShipStats s in FindObjectsOfType<ShipStats>())
        {
            if (s == null || s == self) continue;
            if (s.isEnemy == self.isEnemy) continue;
            if (s.currentHp <= 0) continue;

            int d = HexGrid.Distance(hex, grid.WorldToHex(s.transform.position));
            if (d <= maxDist && d < bestD) { bestD = d; best = s; }
        }
        return best;
    }

    List<ShipStats> EnemiesAt(Hex hex, ShipStats self)
    {
        List<ShipStats> list = new List<ShipStats>();
        foreach (ShipStats s in FindObjectsOfType<ShipStats>())
        {
            if (s == null || s == self) continue;
            if (s.isEnemy == self.isEnemy) continue;
            if (s.currentHp <= 0) continue;
            if (grid.WorldToHex(s.transform.position) == hex) list.Add(s);
        }
        return list;
    }
}