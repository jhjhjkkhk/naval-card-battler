using UnityEngine;
using UnityEngine.EventSystems;

// 点击选中船舰：点船 = 白色线框高亮 + 展开信息牌；点水面/空处 = 取消选中
// 挂在 Main Camera 上即可（挂在别处则自动找主摄像机）
public class ShipSelector : MonoBehaviour
{
    public static ShipStats Selected { get; private set; }   // 全局唯一：当前选中的目标（卡牌要打的对象）
    public static ShipSelector Instance { get; private set; } // 全局入口：别的脚本想"选中某艘船"就找它

    void Awake()
    {
        Instance = this;   // 全场只有一个 ShipSelector，登记自己
    }

    public float maxRayDistance = 2000f;   // 射线最远能点多远
    public Color outlineColor = Color.white;
    public bool useCircle = false;      // true = 正圆（取长边）；false = 椭圆（跟着船的长宽走）
    public float outlineMargin = 1.5f;  // 圈比船本身大出多少（留白，别贴着船身）
    public int outlineSegments = 48;    // 圈用多少段线画（越多越圆滑，48 已经很圆了）
    public float outlineYOffset = 0.2f; // 圈比船底抬高多少（防止插进海里看不见）
    public float seaLevel = 0f;         // 海面高度，如果海水在 y=0 就保持 0

    private Camera cam;
    private LineRenderer outline;          // 当前选中目标的白框
    private float outlineRx;               // 缓存：横半轴
    private float outlineRz;               // 缓存：纵半轴
    private float outlineY;                // 缓存：圈的固定世界 y（海面高度）
    private Vector3 outlineCenterOffset;   // 缓存：圈心相对船 pivot 的偏移（模型 pivot 不在几何中心时要用）

    void Start()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
    }

    void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;   // 只在"按下的一瞬间"判定，按住不算

        // 鼠标正压在UI上（卡牌/按钮/信息牌）→ 这次点击不参与选船
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, maxRayDistance))
        {
            ShipStats ship = hit.collider.GetComponentInParent<ShipStats>();
            if (ship != null) { Select(ship); return; }
        }
        Deselect();   // 点到水面或空处 → 取消选中
    }

    // 选中某艘船（点船时走这里；信息牌展开时也会通过 SelectShip 调用本方法）
    public void Select(ShipStats ship)
    {
        if (Selected == ship) return;   // 点的是已选中的船 → 保持不动
        Deselect();
        Selected = ship;
        if (ship.panel != null) ship.panel.SetExpanded(true);   // 信息牌展开成三行
        BuildOutline(ship);
    }

    // 静态入口：别的脚本（比如信息牌）想选中某艘船，直接 ShipSelector.SelectShip(船)
    public static void SelectShip(ShipStats ship)
    {
        if (Instance != null && ship != null) Instance.Select(ship);
    }

    public static void DeselectShip()
    {
        if (Instance != null) Instance.Deselect();
    }

    public void Deselect()
    {
        if (Selected != null && Selected.panel != null) Selected.panel.SetExpanded(false);
        Selected = null;
        if (outline != null) { Destroy(outline.gameObject); outline = null; }
    }

    // 用白色线圈沿海面把船围一圈（世界空间，不会跟着船身倾斜）
    void BuildOutline(ShipStats ship)
    {
        // ① 把整艘船所有渲染部件的包围盒，合成一个大框
        Bounds b = new Bounds();
        bool found = false;
        foreach (Renderer r in ship.GetComponentsInChildren<Renderer>())
        {
            if (!found) { b = r.bounds; found = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!found) b = new Bounds(ship.transform.position, new Vector3(8, 2, 8));

        // ② 生成线框物体：直接挂在场景根上，自身位置/旋转/缩放全部归零 → 彻底活在世界坐标里
        GameObject go = new GameObject("选中白框");
        go.transform.SetParent(null);                  // 不认任何父物体，杜绝被父物体旋转带歪
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;   // 旋转归零（不带任何倾斜）
        go.transform.localScale = Vector3.one;
        outline = go.AddComponent<LineRenderer>();
        outline.useWorldSpace = true;                  // 所有点按世界坐标解释
        outline.loop = true;
        outline.widthMultiplier = Mathf.Max(0.2f, b.size.x * 0.01f);  // 船越大线越粗
        Shader s = Shader.Find("Sprites/Default");
        if (s == null) s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (s != null) outline.material = new Material(s) { color = outlineColor };
        outline.startColor = outlineColor;
        outline.endColor = outlineColor;

        // ③ 计算圈大小（只算一次， LateUpdate 每帧负责把圈挪到船的新位置）
        outlineRx = b.size.x * 0.5f + outlineMargin;  // 横半轴 = 船宽的一半 + 留白
        outlineRz = b.size.z * 0.5f + outlineMargin;  // 纵半轴 = 船长的一半 + 留白
        if (useCircle) outlineRx = outlineRz = Mathf.Max(outlineRx, outlineRz);  // 正圆：取最长边
        outlineY = seaLevel + outlineYOffset;  // 圈固定在世界 y = 海面（不再跟船底走）
        outlineCenterOffset = b.center - ship.transform.position;    // 圈心 = 几何中心，不是模型 pivot

        outline.positionCount = Mathf.Max(12, outlineSegments);
        RefreshOutlinePositions();

        // 自检：把圈的关键数字打到 Console，方便对照调试
        Debug.Log($"[选中圈] 船={ship.shipName} | 包围盒中心={b.center} 尺寸={b.size} | " +
                  $"圈心偏移={outlineCenterOffset} | rx={outlineRx} rz={outlineRz} | " +
                  $"海面y={outlineY}(seaLevel={seaLevel}+offset={outlineYOffset}) | " +
                  $"船位置={ship.transform.position}");
    }

    // 每帧 LateUpdate 让圈跟着船跑，但高度永远锁在海面
    void LateUpdate()
    {
        if (outline != null && Selected != null)
            RefreshOutlinePositions();
    }

    // 在世界海面高度画一圈（世界坐标，圈永远水平）
    void RefreshOutlinePositions()
    {
        if (outline == null || Selected == null) return;

        Vector3 center = Selected.transform.position + outlineCenterOffset;
        center.y = outlineY;                  // 高度锁死在海面
        int seg = outline.positionCount;
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;  // 0 → 2π
            float x = center.x + Mathf.Cos(a) * outlineRx;
            float z = center.z + Mathf.Sin(a) * outlineRz;
            outline.SetPosition(i, new Vector3(x, outlineY, z));
        }
    }
}