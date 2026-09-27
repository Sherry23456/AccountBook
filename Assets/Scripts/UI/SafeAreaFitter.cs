using UnityEngine;

/// <summary>
/// 全局安全区适配组件（步骤00 §2.4）：挂到顶栏内容容器（Mode=Top）或底栏内容容器（Mode=Bottom）。
/// 背景色块不挂（沉浸式延伸到屏边），只有交互内容容器挂。
/// 运行时按 Screen.safeArea 把自身 RectTransform 的上下边抬进安全区，窗口尺寸变化自动重算。
/// 换算基准必须是 Canvas 全屏高度，不能拿直接父容器：顶栏父容器只有 240~300 单位高，
/// 拿它算出的单位比例会缩水六七倍，真机上内容几乎不下移，年月标题被前置摄像头挖孔遮住（手感优化踩过）。
/// </summary>
public class SafeAreaFitter : MonoBehaviour
{
    /// <summary>
    /// 顶部额外留白（Canvas 单位）：安全区之外再垫一点空气感，挖孔屏日期不贴着状态栏下沿
    /// </summary>
    private const float ExtraTopPadding = 20f;

    public enum Mode
    {
        Top,
        Bottom,
        Both
    }

    [SerializeField] private Mode mode = Mode.Both;

    private RectTransform rectTransform;
    private Vector2 originalAnchorMin;
    private Vector2 originalAnchorMax;
    private Vector2 originalOffsetMin;
    private Vector2 originalOffsetMax;
    private bool originalCached = false;
    private bool applying = false;

    private void Awake()
    {
        CacheOriginal();
        Apply();
    }

    private void OnEnable()
    {
        CacheOriginal();
        Apply();
    }

    private void OnRectTransformDimensionsChange()
    {
        // Apply() 自身修改 offsets 会再触发本回调，用重入标志防止无限递归
        if (applying)
        {
            return;
        }

        Apply();
    }

    /// <summary>
    /// 记录初始锚点与偏移，保证重复 Apply 幂等
    /// </summary>
    private void CacheOriginal()
    {
        if (originalCached)
        {
            return;
        }

        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }

        if (rectTransform == null)
        {
            return;
        }

        originalAnchorMin = rectTransform.anchorMin;
        originalAnchorMax = rectTransform.anchorMax;
        originalOffsetMin = rectTransform.offsetMin;
        originalOffsetMax = rectTransform.offsetMax;
        originalCached = true;
    }

    /// <summary>
    /// 按 safeArea 重算上下内边距（换算成 Canvas 单位）
    /// </summary>
    private void Apply()
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }

        if (rectTransform == null)
        {
            return;
        }

        CacheOriginal();

        Canvas canvas = rectTransform.GetComponentInParent<Canvas>();

        if (canvas == null)
        {
            return;
        }

        RectTransform canvasRect = canvas.transform as RectTransform;
        float canvasHeight = canvasRect != null ? canvasRect.rect.height : 0f;

        if (canvasHeight <= 0f || Screen.height <= 0f)
        {
            return;
        }

        float topUnits;
        float bottomUnits;
        ComputeInsets(canvasHeight, Screen.height, Screen.safeArea, ExtraTopPadding, out topUnits, out bottomUnits);

        // 从初始值重算，避免叠加
        rectTransform.anchorMin = originalAnchorMin;
        rectTransform.anchorMax = originalAnchorMax;

        Vector2 offsetMin = originalOffsetMin;
        Vector2 offsetMax = originalOffsetMax;

        if (mode == Mode.Top || mode == Mode.Both)
        {
            // 顶栏：内容顶边下移，避开刘海/挖孔/状态栏
            offsetMax.y -= topUnits;
        }

        if (mode == Mode.Bottom || mode == Mode.Both)
        {
            // 底栏：内容底边上抬，避开手势条
            offsetMin.y += bottomUnits;
        }

        // 值没变化就不再写回，避免触发布局回调空转
        if (rectTransform.offsetMin == offsetMin && rectTransform.offsetMax == offsetMax)
        {
            return;
        }

        applying = true;

        try
        {
            rectTransform.offsetMin = offsetMin;
            rectTransform.offsetMax = offsetMax;
        }
        finally
        {
            applying = false;
        }
    }

    /// <summary>
    /// 纯换算：屏幕像素安全区 → Canvas 单位内边距（顶部额外留白只加在 top 上）。
    /// 抽成静态是为了编辑器自检（SafeAreaCalcCheck）可以在不进 Play 的情况下锁住换算基准。
    /// </summary>
    public static void ComputeInsets(float canvasHeight, float screenHeight, Rect safeArea, float extraTop,
        out float topUnits, out float bottomUnits)
    {
        if (canvasHeight <= 0f || screenHeight <= 0f)
        {
            topUnits = 0f;
            bottomUnits = 0f;
            return;
        }

        float unitsPerScreenPx = canvasHeight / screenHeight;

        // 顶部刘海/状态栏高度、底部手势条高度（屏幕像素）
        float topInsetPx = Mathf.Max(0f, screenHeight - safeArea.yMax);
        float bottomInsetPx = Mathf.Max(0f, safeArea.y);

        topUnits = topInsetPx * unitsPerScreenPx + Mathf.Max(0f, extraTop);
        bottomUnits = bottomInsetPx * unitsPerScreenPx;
    }
}
