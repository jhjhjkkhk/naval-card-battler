using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 移动范围高亮
//
// 效果：选中一艘船后，把它本回合能开到的六边形画成半透明色块，
//       每个六边形中心再点一个亮点，一眼看出"这里能去"。
//
// 用法：
//   场景里建一个空物体（位置 0,0,0），挂上本脚本即可 —— 不用拖任何引用。
//   它会自动找 HexGrid、自动找被选中的船（ShipSelector.Selected），
//   找不到选中目标时会退化成"场景里第一艘带 ShipMover 的船"。
//
// 说明：默认用 HDRP/Unlit 着色器；如果你的管线不是 HDRP，
//       可以在 Inspector 里手动拖两个材质进来（材质用任意透明 Unlit 都可）。
// ============================================================================
[DisallowMultipleComponent]
public class MovementRangeVisualizer : MonoBehaviour
{
    [Header("目标船（留空自动找）")]
    public ShipMover target;

    [Tooltip("勾上：只有选中某艘船时才显示范围（默认行为）；取消：始终显示第一艘船的范围")]
    public bool onlyWhenSelected = true;

    [Header("外观")]
    [Tooltip("可达格填充颜色")]
    public Color fillColor = new Color(0.25f, 1f, 0.55f, 0.28f);

    [Tooltip("中心点颜色")]
    public Color dotColor = new Color(0.35f, 1f, 0.65f, 0.9f);

    [Tooltip("海面高度（你的 Ocean 在 y=10）")]
    public float seaLevel = 10f;

    [Tooltip("高亮比海面高多少，避免被水面盖住")]
    public float heightAboveSea = 0.5f;

    [Tooltip("中心点半径 = hexSize × 这个比例")]
    [Range(0.05f, 0.5f)]
    public float dotScale = 0.16f;

    [Header("材质（留空自动生成）")]
    public Material fillMaterial;
    public Material dotMaterial;

    [Header("刷新")]
    [Tooltip("离屏/性能敏感时可以调大，单位秒")]
    public float refreshInterval = 0.2f;

    HexGrid grid;
    MeshFilter fillFilter, dotFilter;
    MeshRenderer fillRenderer, dotRenderer;
    Mesh fillMesh, dotMesh;

    ShipMover lastTarget;
    Hex lastOrigin = new Hex(-9999, -9999);
    float nextRefresh;

    // ------------------------------------------------------------------ 生命周期

    void Awake()
    {
        grid = HexGrid.Instance;
        if (grid == null) grid = FindObjectOfType<HexGrid>();

        if (fillMaterial == null) fillMaterial = CreateUnlit(fillColor);
        if (dotMaterial == null) dotMaterial = CreateUnlit(dotColor);

        fillMesh = new Mesh { name = "ReachableFill" };
        dotMesh = new Mesh { name = "ReachableDots" };

        fillFilter = CreateChild("RangeFill", fillMesh, fillMaterial);
        dotFilter = CreateChild("RangeDots", dotMesh, dotMaterial);

        Debug.Log("[高亮] 初始化完成，材质 shader = " + fillMaterial.shader.name
                  + "，位置 = " + transform.position);
    }

    MeshFilter CreateChild(string childName, Mesh mesh, Material mat)
    {
        GameObject go = new GameObject(childName);
        go.transform.SetParent(transform, false);

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        return mf;
    }

    void Update()
    {
        if (Time.time < nextRefresh) return;
        nextRefresh = Time.time + Mathf.Max(refreshInterval, 0.02f);

        if (target == null) target = ResolveTarget();
        if (target == null || grid == null) { Clear(); return; }

        Hex origin = target.CurrentHex;
        if (target != lastTarget || origin != lastOrigin)
        {
            lastTarget = target;
            lastOrigin = origin;
            Rebuild();
        }
    }

    ShipMover ResolveTarget()
    {
        // 1) 优先用 ShipSelector 当前选中的船
        if (ShipSelector.Selected != null)
        {
            ShipMover m = ShipSelector.Selected.GetComponent<ShipMover>();
            if (m != null) return m;
        }

        // 2) 只有允许"始终显示"时，才退化成场景里第一艘
        if (onlyWhenSelected) return null;
        return FindObjectOfType<ShipMover>();
    }

    /// <summary>外部（点击选中）设置当前要显示范围的船</summary>
    public void SetTarget(ShipMover m)
    {
        target = m;
        lastTarget = null;
        lastOrigin = new Hex(-9999, -9999);
        Rebuild();
    }

    /// <summary>清空并隐藏范围</summary>
    public void HideAll()
    {
        target = null;
        Clear();
    }

    // ------------------------------------------------------------------ 构建

    public void Rebuild()
    {
        if (target == null || grid == null) { Clear(); return; }

        List<Hex> reachable = target.GetReachableHexes();
        if (reachable == null || reachable.Count == 0) { Clear(); return; }

        Debug.Log("[高亮] " + target.name + " 可达 " + reachable.Count + " 格（移动力 " + target.moveRangeHexes + "）");

        float size = grid.hexSize;
        int n = reachable.Count;

        Vector3[] fillVerts = new Vector3[n * 7];     // 每格：中心 + 6 个顶点
        int[] fillTris = new int[n * 6 * 3];          // 每格 6 个三角形

        Vector3[] dotVerts = new Vector3[n * 7];
        int[] dotTris = new int[n * 6 * 3];

        float dotR = size * dotScale;

        // 顶点用「相对本物体的局部坐标」，否则物体不在原点时整块高亮会偏移
        Vector3 origin = transform.position;

        for (int i = 0; i < n; i++)
        {
            Vector3 c = grid.HexToWorld(reachable[i]);
            c.y = 0f;
            c -= origin;   // 世界 → 局部

            // ---- 填充六边形 ----
            float localY = (seaLevel + heightAboveSea) - origin.y;
            int fb = i * 7;
            fillVerts[fb] = new Vector3(c.x, localY, c.z);
            for (int k = 0; k < 6; k++)
            {
                float a = Mathf.Deg2Rad * (60f * k);   // 平顶：顶点 0°/60°/120°…
                fillVerts[fb + 1 + k] = new Vector3(
                    c.x + Mathf.Cos(a) * size, localY, c.z + Mathf.Sin(a) * size);
            }
            int tb = i * 18;
            for (int k = 0; k < 6; k++)
            {
                fillTris[tb + k * 3] = fb;
                fillTris[tb + k * 3 + 1] = fb + 1 + ((k + 1) % 6);
                fillTris[tb + k * 3 + 2] = fb + 1 + k;
            }

            // ---- 中心点 ----
            int db = i * 7;
            dotVerts[db] = new Vector3(c.x, localY + 0.01f, c.z);
            for (int k = 0; k < 6; k++)
            {
                float a = Mathf.Deg2Rad * (60f * k);
                dotVerts[db + 1 + k] = new Vector3(
                    c.x + Mathf.Cos(a) * dotR, localY + 0.01f, c.z + Mathf.Sin(a) * dotR);
            }
            int dtb = i * 18;
            for (int k = 0; k < 6; k++)
            {
                dotTris[dtb + k * 3] = db;
                dotTris[dtb + k * 3 + 1] = db + 1 + ((k + 1) % 6);
                dotTris[dtb + k * 3 + 2] = db + 1 + k;
            }
        }

        ApplyMesh(fillMesh, fillVerts, fillTris);
        ApplyMesh(dotMesh, dotVerts, dotTris);
    }

    static void ApplyMesh(Mesh mesh, Vector3[] verts, int[] tris)
    {
        mesh.Clear();
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    void Clear()
    {
        if (fillMesh != null) fillMesh.Clear();
        if (dotMesh != null) dotMesh.Clear();
        lastTarget = null;
        lastOrigin = new Hex(-9999, -9999);
    }

    // ------------------------------------------------------------------ 材质

    static Material CreateUnlit(Color color)
    {
        Shader sh = Shader.Find("HDRP/Unlit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        Material mat = new Material(sh) { name = "RangeHighlight" };

        // HDRP：切到透明表面
        if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 1f);
        if (mat.HasProperty("_BlendMode")) mat.SetFloat("_BlendMode", 0f);
        if (mat.HasProperty("_AlphaCutoffEnable")) mat.SetFloat("_AlphaCutoffEnable", 0f);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        // 双面渲染：万一绕序还是反的也不至于看不见
        if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", 1f);
        if (mat.HasProperty("_CullMode")) mat.SetFloat("_CullMode", 0f);
        mat.EnableKeyword("_DOUBLE_SIDED");
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

        mat.renderQueue = 3000;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

        return mat;
    }
}
