using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 通用确认弹窗（步骤03 §2.3）：半透明遮罩 + 白色对话框 + 文案 + 取消/确认两按钮。
/// 由 DetailPanelBuilder 搭建在 Canvas 根（全屏最上层，默认隐藏），明细页长按删除先用，后续流程可复用。
/// 取消/点遮罩只关闭；确认回调执行一次后自动清空，防止复用时误触发旧回调。
/// </summary>
public class ConfirmDialog : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI messageLabel;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private Button maskButton;

    /// <summary>
    /// 确认按钮文字（不同流程复用时可改，如"删除"/"确定"）
    /// </summary>
    public string ConfirmText
    {
        get
        {
            TextMeshProUGUI label = confirmButton != null ? confirmButton.GetComponentInChildren<TextMeshProUGUI>() : null;
            return label != null ? label.text : "";
        }
        set
        {
            TextMeshProUGUI label = confirmButton != null ? confirmButton.GetComponentInChildren<TextMeshProUGUI>() : null;

            if (label != null)
            {
                label.text = value;
            }
        }
    }

    private Action onConfirmed;

    private void OnEnable()
    {
        RegisterEvents();
    }

    /// <summary>
    /// 打开弹窗：填文案、暂存确认回调（幂等绑定按钮事件）
    /// </summary>
    public void Show(string message, Action onConfirmed)
    {
        this.onConfirmed = onConfirmed;

        if (messageLabel != null)
        {
            messageLabel.text = message;
        }

        RegisterEvents();
        gameObject.SetActive(true);
    }

    /// <summary>
    /// 关闭弹窗并清空回调（不触发确认）
    /// </summary>
    public void Close()
    {
        gameObject.SetActive(false);
        onConfirmed = null;
    }

    /// <summary>
    /// 按钮事件绑定（幂等，可重复调用）
    /// </summary>
    private void RegisterEvents()
    {
        RegisterButton(confirmButton, OnConfirmClicked);
        RegisterButton(cancelButton, Close);
        RegisterButton(maskButton, Close);
    }

    private static void RegisterButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    private void OnConfirmClicked()
    {
        Action callback = onConfirmed;
        Close();

        if (callback != null)
        {
            callback.Invoke();
        }
    }
}
