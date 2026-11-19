using UnityEngine;

// buff 面板的全局设置
// ★ 挂到场景里任意一个物体上（比如和 CardPlayFx 放一起），改完能保存
//   信息牌和详情框都是运行时生成的，退出 Play 就丢失 —— 所以参数统一放这里
public class BuffPanelSettings : MonoBehaviour
{
    public static BuffPanelSettings Instance { get; private set; }

    [Header("状态栏图标（船舰信息牌里的小图标）")]
    public float iconSize = 44f;                 // 图标边长（像素）

    [Header("详情框（点图标弹出的那个）")]
    public Vector2 tooltipSize = new Vector2(260f, 150f);   // 框的尺寸
    public float tooltipIconSize = 40f;          // 框里那个小图标的大小
    public int tooltipTitleFontSize = 22;        // 标题字号（buff 名）
    public int tooltipBodyFontSize = 19;         // 正文字号（效果 / 剩余回合）

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
