using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 移动预览：每艘船各自一个虚影 + 一条虚线
//
// 设计：
//   · 规划期给哪艘船选了目标格，那艘船就保留「半透明剪影 + 段状虚线」
//   · 多艘船可以同时有预览，互不覆盖
//   · 同一条船改目标 → 只更新它自己那一份
//   · 演算期 → 全部隐藏
//
// 虚影不克隆整个 GameObject（会把船身上的 UI/Canvas 一起复制成巨大色块），
// 只按 MeshFilter 复制网格做纯剪影。
// ============================================================================
public class MovePreview : MonoBehaviour
{
    [Header("高度")]
    [Tooltip("海面高度（你的 Ocean 在 y=10）")]
    public float seaLevel = 10f;

    [Tooltip("虚线高出海面多少")]
    public float lineHeight = 1.0f;

    [Header("虚线外观")]
    public Color dashColor = new Color(1f, 0.85f, 0.25f, 1f);
    public float dashWidth = 0.3f;
    [Tooltip("每一小段实线的长度")]
    public float dashLength = 1.2f;
    [Tooltip("段与段之间的空隙")]
    public float gapLength = 0.8f;

    [Header("虚影外观")]
    public Color ghostColor = new Color(0.45f, 1f, 0.65f, 0.22f);

    // 每艘船一份预览
    class PreviewItem
    {
        public ShipMover mover;
        public GameObject ghost;
        public readonly List<LineRenderer> dashes = new List<LineRenderer>();
    }

    readonly Dictionary<ShipMover, PreviewItem> items = new Dictionary<ShipMover, PreviewItem>();
    Material dashMaterial;
    Material ghostMaterial;

    // ------------------------------------------------------------------ 对外

    /// <summary>为某艘船显示（或更新）虚影与虚线，不影响其它船</summary>
    public void Show(ShipMover mover, List<Vector3> path)
    {
        if (mover == null || path == null || path.Count < 2) return;

        EnsureMaterials();

        PreviewItem item;
        if (!items.TryGetValue(mover, out item))
        {
            item = new PreviewItem { mover = mover };
            items[mover] = item;
        }

        if (item.ghost == null) BuildGhost(item);
        PlaceGhost(item, path);
        BuildDashes(item, path);
    }

    /// <summary>隐藏全部预览（演算期调用）</summary>
    public void Hide()
    {
        foreach (PreviewItem item in items.Values)
        {
            if (item.ghost != null) item.ghost.SetActive(false);
            for (int i = 0; i < item.dashes.Count; i++)
                if (item.dashes[i] != null) item.dashes[i].positionCount = 0;
        }
    }

    /// <summary>彻底清理（新回合 / 换局时调用）</summary>
    public void Clear()
    {
        foreach (PreviewItem item in items.Values)
        {
            if (item.ghost != null) Destroy(item.ghost);
            for (int i = 0; i < item.dashes.Count; i++)
                if (item.dashes[i] != null) Destroy(item.dashes[i].gameObject);
        }
        items.Clear();
    }

    /// <summary>移除某艘船的预览（比如它开走了）</summary>
    public void ClearFor(ShipMover mover)
    {
        PreviewItem item;
        if (!items.TryGetValue(mover, out item)) return;

        if (item.ghost != null) Destroy(item.ghost);
        for (int i = 0; i < item.dashes.Count; i++)
            if (item.dashes[i] != null) Destroy(item.dashes[i].gameObject);

        items.Remove(mover);
    }

    // ------------------------------------------------------------------ 虚影

    void BuildGhost(PreviewItem item)
    {
        if (item.ghost != null) Destroy(item.ghost);

        GameObject root = new GameObject("MoveGhost_" + item.mover.name);
        root.transform.position = item.mover.transform.position;
        root.transform.rotation = item.mover.transform.rotation;

        foreach (MeshFilter mf in item.mover.GetComponentsInChildren<MeshFilter>())
        {
            if (mf == null || mf.sharedMesh == null) continue;
            if (mf.GetComponent<Renderer>() == null) continue;

            GameObject part = new GameObject(mf.name);
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = root.transform.InverseTransformPoint(mf.transform.position);
            part.transform.localRotation = Quaternion.Inverse(root.transform.rotation) * mf.transform.rotation;
            part.transform.localScale = mf.transform.lossyScale;

            part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;

            MeshRenderer mr = part.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ghostMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        item.ghost = root;
    }

    void PlaceGhost(PreviewItem item, List<Vector3> path)
    {
        if (item.ghost == null) return;

        Vector3 end = path[path.Count - 1];
        Vector3 dir = end - path[path.Count - 2];
        dir.y = 0f;

        item.ghost.SetActive(true);
        item.ghost.transform.position = end;
        if (dir.sqrMagnitude > 0.0001f)
            item.ghost.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    // ------------------------------------------------------------------ 虚线

    void BuildDashes(PreviewItem item, List<Vector3> path)
    {
        float total = 0f;
        for (int i = 0; i < path.Count - 1; i++) total += Vector3.Distance(path[i], path[i + 1]);

        float cycle = Mathf.Max(dashLength + gapLength, 0.2f);
        float dash = Mathf.Max(dashLength, 0.05f);
        float y = seaLevel + lineHeight;

        int used = 0;

        for (float d = 0f; d < total; d += cycle)
        {
            float e = Mathf.Min(d + dash, total);
            if (e - d < 0.02f) continue;

            Vector3 a = PointAtDistance(path, d);
            Vector3 b = PointAtDistance(path, e);
            a.y = y;
            b.y = y;

            LineRenderer lr = GetDash(item, used++);
            lr.positionCount = 2;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
        }

        for (int i = used; i < item.dashes.Count; i++)
            if (item.dashes[i] != null) item.dashes[i].positionCount = 0;
    }

    static Vector3 PointAtDistance(List<Vector3> path, float distance)
    {
        float remain = distance;
        for (int i = 0; i < path.Count - 1; i++)
        {
            float seg = Vector3.Distance(path[i], path[i + 1]);
            if (remain <= seg && seg > 0.0001f)
                return Vector3.Lerp(path[i], path[i + 1], remain / seg);
            remain -= seg;
        }
        return path[path.Count - 1];
    }

    LineRenderer GetDash(PreviewItem item, int index)
    {
        while (item.dashes.Count <= index)
        {
            GameObject go = new GameObject("Dash_" + item.mover.name);
            go.transform.SetParent(transform, false);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = dashWidth;
            lr.positionCount = 0;
            lr.material = dashMaterial;
            lr.startColor = dashColor;
            lr.endColor = dashColor;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            item.dashes.Add(lr);
        }
        return item.dashes[index];
    }

    // ------------------------------------------------------------------ 材质

    void EnsureMaterials()
    {
        if (dashMaterial == null) dashMaterial = CreateUnlit(dashColor, dashColor.a);
        if (ghostMaterial == null) ghostMaterial = CreateUnlit(ghostColor, ghostColor.a);
    }

    static Material CreateUnlit(Color color, float alpha)
    {
        Shader sh = Shader.Find("HDRP/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        Material mat = new Material(sh) { name = "MovePreviewMat" };

        if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 1f);
        if (mat.HasProperty("_BlendMode")) mat.SetFloat("_BlendMode", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", 1f);
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        mat.renderQueue = 3000;

        Color c = color;
        c.a = alpha;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        return mat;
    }
}
