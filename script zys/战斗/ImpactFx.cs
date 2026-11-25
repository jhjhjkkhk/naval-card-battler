using System.Collections;
using UnityEngine;

// ============================================================================
// 落点 / 命中特效（纯代码，不需要素材）
//
// 五种表现，靠"颜色 + 形状 + 方向"区分：
//   Penetration 击穿：橙色火球膨胀 + 黑烟上升
//   Ricochet    跳弹：白色火星朝斜上方弹开
//   NoPen       未击穿：灰色闷烟
//   NearMiss    近失：白色细高水柱 + 水面圆环
//   Miss        打空：小水花
// ============================================================================
public enum FxKind { Penetration, Ricochet, NoPen, NearMiss, Miss }

public class ImpactFx : MonoBehaviour
{
    Material mat;
    Color baseColor;
    float radius;
    float duration;
    float t;

    // ------------------------------------------------------------------ 预算

    // 一轮齐射里同类特效最多几个（避免 9 门炮刷 9 个爆炸糊成一片）
    static int MaxPerSalvo { get { return CombatSettings.Instance.maxFxPerSalvo; } }
    static readonly System.Collections.Generic.Dictionary<FxKind, int> counts
        = new System.Collections.Generic.Dictionary<FxKind, int>();

    /// <summary>每次开火前调用，重置这一轮的预算</summary>
    public static void ResetSalvoBudget()
    {
        counts.Clear();
    }

    static bool TryConsume(FxKind kind)
    {
        int n;
        counts.TryGetValue(kind, out n);
        if (n >= MaxPerSalvo) return false;
        counts[kind] = n + 1;
        return true;
    }

    // ------------------------------------------------------------------ 对外

    public static void Play(Vector3 pos, FxKind kind)
    {
        if (!TryConsume(kind)) return;

        switch (kind)
        {
            case FxKind.Penetration:
                Sphere(pos + Vector3.up * 2f, new Color(1f, 0.45f, 0.1f, 0.95f), 5f, 0.55f);
                Smoke(pos + Vector3.up * 4f, new Color(0.15f, 0.15f, 0.15f, 0.8f), 6f, 1.4f);
                Ring(pos, new Color(1f, 0.6f, 0.2f, 0.8f), 2f, 8f, 0.5f);
                break;

            case FxKind.Ricochet:
                Spark(pos, new Color(1f, 0.95f, 0.6f, 1f));
                break;

            case FxKind.NoPen:
                Sphere(pos + Vector3.up * 1.5f, new Color(0.55f, 0.55f, 0.55f, 0.85f), 3f, 0.7f);
                Spark(pos, new Color(1f, 0.85f, 0.5f, 0.9f));
                break;

            case FxKind.NearMiss:
                WaterColumn(pos, new Color(0.85f, 0.95f, 1f, 0.95f));
                Ring(pos, new Color(0.9f, 0.97f, 1f, 0.7f), 2f, 9f, 0.6f);
                break;

            default: // Miss
                WaterColumn(pos, new Color(0.8f, 0.9f, 1f, 0.6f), 0.45f);
                break;
        }
    }

    // ------------------------------------------------------------------ 具体形状

    static void Sphere(Vector3 pos, Color color, float radius, float duration)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.transform.position = pos;

        Material m = CombatVisualUtil.CreateUnlit(color);
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        ImpactFx fx = go.AddComponent<ImpactFx>();
        fx.mat = m; fx.baseColor = color; fx.radius = radius; fx.duration = duration;
    }

    /// <summary>慢速膨胀的黑烟（升起来再消失）</summary>
    static void Smoke(Vector3 pos, Color color, float radius, float duration)
    {
        Sphere(pos, color, radius, duration);
    }

    /// <summary>水柱：细长的圆柱，快速升起再消失</summary>
    static void WaterColumn(Vector3 pos, Color color, float scale = 1f)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(go.GetComponent<Collider>());
        go.transform.position = pos + Vector3.up * 0.5f;
        go.transform.localScale = new Vector3(1.2f * scale, 1f * scale, 1.2f * scale);

        Material m = CombatVisualUtil.CreateUnlit(color);
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        ImpactFx fx = go.AddComponent<ImpactFx>();
        fx.mat = m; fx.baseColor = color; fx.radius = 6f * scale; fx.duration = 0.55f;
        fx.waterColumn = true;
    }

    bool waterColumn;

    /// <summary>跳弹火星：一条朝斜上方的亮线</summary>
    static void Spark(Vector3 pos, Color color)
    {
        GameObject go = new GameObject("Spark");
        go.transform.position = pos;

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.widthMultiplier = 0.6f;
        Material m = CombatVisualUtil.CreateUnlit(color);
        lr.material = m;
        lr.startColor = color;
        lr.endColor = new Color(color.r, color.g, color.b, 0f);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Vector3 a = pos;
        Vector3 b = pos + new Vector3(Random.Range(-3f, 3f), Random.Range(8f, 14f), Random.Range(-3f, 3f));
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);

        ImpactFx fx = go.AddComponent<ImpactFx>();
        fx.mat = m; fx.baseColor = color; fx.radius = 0f; fx.duration = 0.35f;
        fx.spark = lr;
    }

    LineRenderer spark;

    /// <summary>水面扩散圆环</summary>
    static void Ring(Vector3 pos, Color color, float fromR, float toR, float duration)
    {
        GameObject go = new GameObject("Ring");
        go.transform.position = pos;

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.positionCount = 32;
        lr.widthMultiplier = 0.5f;
        Material m = CombatVisualUtil.CreateUnlit(color);
        lr.material = m;
        lr.startColor = color;
        lr.endColor = color;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        ImpactFx fx = go.AddComponent<ImpactFx>();
        fx.mat = m; fx.baseColor = color; fx.duration = duration;
        fx.ring = lr;
        fx.ringFrom = fromR;
        fx.ringTo = toR;
        fx.ringCenter = new Vector3(pos.x, 10.4f, pos.z);
    }

    LineRenderer ring;
    float ringFrom, ringTo;
    Vector3 ringCenter;

    // ------------------------------------------------------------------ 动画

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / duration);

        if (waterColumn)
        {
            // 细长水柱：先快速升起，再淡出
            float h = Mathf.Lerp(1f, radius, Mathf.Sin(k * Mathf.PI * 0.8f));
            transform.localScale = new Vector3(1.2f * (1f + k), h, 1.2f * (1f + k));
            transform.position += Vector3.up * (Time.deltaTime * 3f);
        }
        else if (spark != null)
        {
            // 火星线缩短
            Vector3 a = transform.position;
            Vector3 b = Vector3.Lerp(spark.GetPosition(1), a, Time.deltaTime * 6f);
            spark.SetPosition(1, b);
        }
        else if (ring != null)
        {
            float r = Mathf.Lerp(ringFrom, ringTo, k);
            for (int i = 0; i < 32; i++)
            {
                float ang = Mathf.PI * 2f * i / 32f;
                ring.SetPosition(i, ringCenter + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r));
            }
        }
        else if (radius > 0f)
        {
            transform.localScale = Vector3.one * Mathf.Lerp(radius * 0.25f, radius, k);
        }

        Color c = baseColor;
        c.a = baseColor.a * (1f - k * k);
        if (mat != null)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }

        if (k >= 1f) Destroy(gameObject);
    }
}