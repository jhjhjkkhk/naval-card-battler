using UnityEngine;

// ============================================================================
// 炮弹飞行（弹道版）
//
// 视觉三件事：
//   1. 抛物线：炮弹从炮口升起再落下，不再贴海面直线飞
//   2. 白色拖尾：能看到弹道走向
//   3. 落点圆圈：飞行途中在落点画一个收缩的白圈，预告"要砸这里"
//
// 落地时回调演算导演结算伤害。
// ============================================================================
public class ShellFlight : MonoBehaviour
{
    Vector3 from;
    Vector3 to;
    float duration;
    float elapsed;
    float arcHeight;
    System.Action onLand;

    TrailRenderer trail;
    LineRenderer marker;
    float markerRadius;

    public static ShellFlight Spawn(Vector3 from, Vector3 to, float speedWorld, float markerRadius, System.Action onLand)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Shell";
        go.transform.position = from;
        go.transform.localScale = Vector3.one * 0.5f;

        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            mr.sharedMaterial = CombatVisualUtil.CreateUnlit(new Color(1f, 0.95f, 0.75f, 1f));
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        ShellFlight shell = go.AddComponent<ShellFlight>();
        shell.from = from;
        shell.to = to;
        shell.speedWorld = Mathf.Max(speedWorld, 1f);
        shell.duration = Mathf.Max(0.15f, Vector3.Distance(from, to) / shell.speedWorld);
        shell.onLand = onLand;
        shell.markerRadius = Mathf.Max(markerRadius, 0.5f);

        // 抛物线高度：距离越远，弧顶越高
        float dist = Vector3.Distance(from, to);
        shell.arcHeight = Mathf.Clamp(dist * 0.35f, 8f, 90f);

        // 白色拖尾
        TrailRenderer tr = go.AddComponent<TrailRenderer>();
        tr.time = 0.35f;
        tr.widthMultiplier = 0.14f;
        tr.material = CombatVisualUtil.CreateUnlit(new Color(1f, 1f, 1f, 0.85f));
        tr.startColor = new Color(1f, 1f, 1f, 0.9f);
        tr.endColor = new Color(1f, 1f, 1f, 0f);
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        shell.trail = tr;

        // 落点预警圆圈
        shell.CreateMarker();

        return shell;
    }

    float speedWorld;

    void CreateMarker()
    {
        GameObject go = new GameObject("LandingMarker");
        go.transform.SetParent(null);
        go.transform.position = to;

        marker = go.AddComponent<LineRenderer>();
        marker.useWorldSpace = true;
        marker.loop = true;
        marker.positionCount = 32;
        marker.widthMultiplier = 0.35f;
        marker.material = CombatVisualUtil.CreateUnlit(new Color(1f, 1f, 1f, 0.9f));
        marker.startColor = new Color(1f, 1f, 1f, 0.9f);
        marker.endColor = new Color(1f, 1f, 1f, 0.9f);
        marker.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        marker.receiveShadows = false;
    }

    void Update()
    {
        elapsed += Time.deltaTime;

        float t = duration <= 0.001f ? 1f : Mathf.Clamp01(elapsed / duration);

        // 抛物线：水平插值 + 正弦弧顶
        Vector3 flat = Vector3.Lerp(from, to, t);
        flat.y += arcHeight * Mathf.Sin(Mathf.PI * t);
        transform.position = flat;

        // 落点圈从大收缩到小
        if (marker != null)
        {
            float r = Mathf.Lerp(markerRadius, markerRadius * 0.25f, t);
            float y = 10.6f;
            for (int i = 0; i < 32; i++)
            {
                float a = Mathf.PI * 2f * i / 32f;
                marker.SetPosition(i, new Vector3(to.x + Mathf.Cos(a) * r, y, to.z + Mathf.Sin(a) * r));
            }
        }

        if (t >= 1f)
        {
            if (marker != null) Destroy(marker.gameObject);
            if (onLand != null) onLand();
            Destroy(gameObject, 0.05f);
            enabled = false;
        }
    }
}