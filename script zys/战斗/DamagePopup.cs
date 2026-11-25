using UnityEngine;
using UnityEngine.UI;

// ============================================================================
// 伤害飘字（uGUI 版）
//   用项目里已验证的 UiFont + Text，挂到屏幕 Canvas 上，
//   每帧把世界坐标转屏幕坐标，保证一定看得见。
// ============================================================================
public class DamagePopup : MonoBehaviour
{
    Text text;
    Vector3 worldPos;
    float t;
    float life = 1.0f;
    float rise = 45f;          // 屏幕像素
    Color baseColor;

    public static void Show(Vector3 worldPosition, int damage, bool crit = false)
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        GameObject go = new GameObject("DamagePopup");
        go.transform.SetParent(canvas.transform, false);

        Text txt = go.AddComponent<Text>();
        txt.font = UiFont.Get();
        txt.fontSize = crit ? 46 : 34;
        txt.fontStyle = crit ? FontStyle.Bold : FontStyle.Normal;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.raycastTarget = false;
        txt.text = (crit ? "暴击 " : "") + damage;

        Color c = crit ? new Color(1f, 0.55f, 0.1f) : new Color(1f, 1f, 1f);
        txt.color = c;

        RectTransform rt = txt.rectTransform;
        rt.sizeDelta = new Vector2(240f, 70f);

        DamagePopup p = go.AddComponent<DamagePopup>();
        p.text = txt;
        p.worldPos = worldPosition + Vector3.up * 3f;
        p.baseColor = c;
        p.rise = crit ? 60f : 45f;

        p.UpdatePosition(0f);
    }

    void UpdatePosition(float k)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 sp = cam.WorldToScreenPoint(worldPos);
        if (sp.z < 0f) { text.enabled = false; return; }

        text.enabled = true;

        RectTransform canvasRt = text.canvas.transform as RectTransform;
        Vector2 local;
        Camera uiCam = text.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : text.canvas.worldCamera;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, uiCam, out local);

        text.rectTransform.anchoredPosition = local + new Vector2(0f, rise * k);
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / life);

        UpdatePosition(k);

        Color c = baseColor;
        c.a = 1f - k * k;
        text.color = c;

        if (k >= 1f) Destroy(gameObject);
    }
}