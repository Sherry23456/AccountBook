using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// 轻量提示条（步骤05）：Canvas 根顶部悬浮黑底白字，常驻不销毁，CanvasGroup alpha 控制显隐，
/// 显示数秒后自动淡出。静态 Show 供各处调用（导出/导入结果、步骤07 统一收口），
/// raycastTarget 全关不挡点击。物体常驻 active 是为了让 Awake 一定执行、静态实例可靠注册。
/// </summary>
public class ToastUI : MonoBehaviour
{
    private const float ShowSeconds = 2.5f;
    private const float FadeSeconds = 0.25f;

    private static ToastUI instance;

    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private CanvasGroup canvasGroup;

    private Coroutine hideRoutine;

    /// <summary>
    /// 全局提示入口；场景未搭 Toast 时仅告警不崩溃
    /// </summary>
    public static void Show(string text)
    {
        if (instance == null)
        {
            Debug.LogWarning("[ToastUI] 场景中没有 ToastUI，提示被忽略: " + text);
            return;
        }

        instance.InternalShow(text);
    }

    private void Awake()
    {
        instance = this;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void InternalShow(string text)
    {
        if (label != null)
        {
            label.text = text;
        }

        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }

        hideRoutine = StartCoroutine(HideAfterDelay());
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(ShowSeconds);

        if (canvasGroup != null)
        {
            float elapsed = 0f;

            while (elapsed < FadeSeconds)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Clamp01(1f - elapsed / FadeSeconds);
                yield return null;
            }
        }

        hideRoutine = null;
    }
}
