using UnityEngine;

/// <summary>
/// 全局安全区适配组件（步骤00 §2.4）：挂到顶栏内容容器（Mode=Top）或底栏内容容器（Mode=Bottom）。
/// 背景色块不挂（沉浸式延伸到屏边），只有交互内容容器挂。
/// 运行时按 Screen.safeArea 把自身 RectTransform 的上下边抬进安全区，窗口尺寸变化自动重算。
/// </summary>
public class SafeAreaFitter : MonoBehaviour
{
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
    /// 按 safeArea 重算上下内边距（换算成父容器 canvas 单位）
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

        RectTransform parent = rectTransform.parent as RectTransform;

        if (parent == null)
        {
            return;
        }

        float parentHeight = parent.rect.height;

        if (parentHeight <= 0f || Screen.height <= 0f)
        {
            return;
        }

        float unitsPerScreenPx = parentHeight / Screen.height;

        // 顶部刘海/状态栏高度、底部手势条高度（屏幕像素）
        float topInsetPx = Mathf.Max(0f, Screen.height - Screen.safeArea.yMax);
        float bottomInsetPx = Mathf.Max(0f, Screen.safeArea.y);

        float topUnits = topInsetPx * unitsPerScreenPx;
        float bottomUnits = bottomInsetPx * unitsPerScreenPx;

        // 从初始值重算，避免叠加
        rectTransform.anchorMin = originalAnchorMin;
        rectTransform.anchorMax = originalAnchorMax;

        Vector2 offsetMin = originalOffsetMin;
        Vector2 offsetMax = originalOffsetMax;

        if (mode == Mode.Top || mode == Mode.Both)
        {
            // 顶栏：内容顶边下移，避开刘海/状态栏
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
}
