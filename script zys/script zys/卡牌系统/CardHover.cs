using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;

// 卡牌悬停效果（动画版）
// 鼠标移上去 → 平滑放大到 1.5 倍 + 向上滑半张牌高（保证放大的牌完整可见）
// 鼠标移开   → 平滑恢复原大、回到队列位置
public class CardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("悬停设置")]
    public float hoverScale = 1.5f;    // 放大到几倍
    public float animDuration = 0.15f; // 一次悬停动画的时长（秒），越小越快

    [Header("重排设置")]
    public float layoutDuration = 0.25f; // 发牌器重排手牌时，滑到新座位的时长

    private RectTransform rt;     // 自己的坐标控制器
    public Vector2 homePosition;  // 牌在队列里的"家"（由发牌器通过 SetHome 告知；CardDrag 要读它，所以是 public）
    private Vector2 homeSize;     // 牌在队列里应有的尺寸（牌多了会自动变窄）
    private Coroutine playing;    // 当前正在播放的动画

    void Awake()
    {
        rt = transform as RectTransform;
    }

    // 发牌器造出这张牌时会叫这个方法，把"家"的位置告诉它
    public void SetHome(Vector2 pos)
    {
        homePosition = pos;
    }

    // 重排专用：同时告诉它"家"在哪、该多宽（牌多了会变窄，宽度也要跟着补间）
    public void SetHome(Vector2 pos, Vector2 size)
    {
        homePosition = pos;
        homeSize = size;
    }

    public Vector2 HomeSize { get { return homeSize; } }   // 队列里应有的尺寸（拖拽飞回时要跟着复位）

    // 发牌器重排手牌时调用：平滑滑到新的座位（不瞬移）
    // animate = false 时才瞬间归位（比如刚生成还没入场）
    public void MoveHome(bool animate)
    {
        if (!animate)
        {
            if (playing != null) { StopCoroutine(playing); playing = null; }
            rt.anchoredPosition = homePosition;
            if (homeSize.x > 0f) rt.sizeDelta = homeSize;
            return;
        }

        if (playing != null) StopCoroutine(playing);
        playing = StartCoroutine(AnimateLayout(layoutDuration));
    }

    // 鼠标移到牌上时，Unity 自动调用（方法名不能改）
    public void OnPointerEnter(PointerEventData eventData)
    {
        transform.SetAsLastSibling(); // 提到最上层，不被邻牌挡住

        // 目标：原地按中心放大（pivot 在中心，牌向四周均匀变大，位置不动）
        Vector3 targetScale = Vector3.one * hoverScale;
        Vector2 targetPos = homePosition;
        Play(targetScale, targetPos);
    }

    // 鼠标移开时，Unity 自动调用
    public void OnPointerExit(PointerEventData eventData)
    {
        // 目标：恢复原大 + 回家
        Play(Vector3.one, homePosition);
    }

    // 播放动画。如果上一段还没播完，先掐掉再播新的——
    // 这样鼠标快速滑过时也不会跳变，永远从"现在的样子"接着走
    void Play(Vector3 targetScale, Vector2 targetPos)
    {
        if (playing != null)
        {
            StopCoroutine(playing);
        }
        playing = StartCoroutine(Animate(targetScale, targetPos));
    }

    // 动画本体：每帧把大小和位置向目标推进一点点
    IEnumerator Animate(Vector3 targetScale, Vector2 targetPos)
    {
        Vector3 startScale = transform.localScale; // 起点：现在的大小
        Vector2 startPos = rt.anchoredPosition;    // 起点：现在的位置
        float time = 0f;

        while (time < animDuration)
        {
            time += Time.deltaTime;                        // 走过的真实秒数
            float p = Mathf.Clamp01(time / animDuration);  // 进度 0~1
            transform.localScale = Vector3.Lerp(startScale, targetScale, p);
            rt.anchoredPosition = Vector2.Lerp(startPos, targetPos, p);
            yield return null;                             // 等一帧，继续
        }

        // 保险：精确停在终点
        transform.localScale = targetScale;
        rt.anchoredPosition = targetPos;
    }

    // 重排动画：位置和宽度一起补间（缓入缓出），大小保持不变（悬停放大的牌不会被打回原形）
    IEnumerator AnimateLayout(float duration)
    {
        Vector2 startPos = rt.anchoredPosition;
        Vector2 startSize = rt.sizeDelta;
        Vector2 targetSize = homeSize.x > 0f ? homeSize : startSize;
        float time = 0f;

        // 已经站在家了 → 不用动，省一次空动画
        if ((startPos - homePosition).sqrMagnitude < 0.01f && (startSize - targetSize).sqrMagnitude < 0.01f)
        {
            playing = null;
            yield break;
        }

        while (time < duration)
        {
            time += Time.deltaTime;
            float p = Mathf.Clamp01(time / duration);
            float e = p * p * (3f - 2f * p);          // 缓入缓出
            rt.anchoredPosition = Vector2.Lerp(startPos, homePosition, e);
            rt.sizeDelta = Vector2.Lerp(startSize, targetSize, e);
            yield return null;
        }

        rt.anchoredPosition = homePosition;
        rt.sizeDelta = targetSize;
        playing = null;
    }
}