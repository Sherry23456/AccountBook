using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 通用确认/选择弹窗（步骤03 §2.3）：半透明遮罩 + 白色对话框 + 文案 + 左右两按钮。
/// 由 DetailPanelBuilder 搭建在 Canvas 根（全屏最上层，默认隐藏）。
/// 两种用法：Show(message, onConfirmed) = 取消/删除确认框（取消、点遮罩只关闭）；
/// Show(message, 左文案, 左回调, 右文案, 右回调) = 双动作选择框（明细页长按记录行的 修改/删除）。
/// 回调执行一次后自动清空，防止复用时误触发旧回调。
/// </summary>
public class ConfirmDialog : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI messageLabel;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private Button maskButton;

    /// <summary>
    /// 确认（右）按钮文字（不同流程复用时可改，如"删除"/"确定"）
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

    /// <summary>
    /// 取消（左）按钮文字（选择框模式下是左动作的文字，如"修改"）
    /// </summary>
    public string CancelText
    {
        get
        {
            TextMeshProUGUI label = cancelButton != null ? cancelButton.GetComponentInChildren<TextMeshProUGUI>() : null;
            return label != null ? label.text : "";
        }
        set
        {
            TextMeshProUGUI label = cancelButton != null ? cancelButton.GetComponentInChildren<TextMeshProUGUI>() : null;

            if (label != null)
            {
                label.text = value;
            }
        }
    }

    private Action onConfirmed;
    private Action onLeftAction;   // 左键动作回调；null = 左键仅关闭（普通确认框的"取消"）

    private void OnEnable()
    {
        RegisterEvents();
    }

    /// <summary>
    /// 打开确认框：文案 + 取消/删除两按钮（取消、点遮罩只关闭）
    /// </summary>
    public void Show(string message, Action onConfirmed)
    {
        Show(message, "取消", null, "删除", onConfirmed);
    }

    /// <summary>
    /// 打开双动作选择框：左键=onLeftAction，右键=onRightAction，点遮罩=关闭不触发
    /// </summary>
    public void Show(string message, string leftText, Action onLeftAction, string rightText, Action onRightAction)
    {
        this.onLeftAction = onLeftAction;
        this.onConfirmed = onRightAction;
        CancelText = leftText;
        ConfirmText = rightText;

        if (messageLabel != null)
        {
            messageLabel.text = message;
        }

        RegisterEvents();
        gameObject.SetActive(true);
    }

    /// <summary>
    /// 关闭弹窗并清空回调（不触发任何回调）
    /// </summary>
    public void Close()
    {
        gameObject.SetActive(false);
        onConfirmed = null;
        onLeftAction = null;
    }

    /// <summary>
    /// 按钮事件绑定（幂等，可重复调用）
    /// </summary>
    private void RegisterEvents()
    {
        RegisterButton(confirmButton, OnConfirmClicked);
        RegisterButton(cancelButton, OnCancelClicked);
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

    private void OnCancelClicked()
    {
        Action callback = onLeftAction;
        Close();

        if (callback != null)
        {
            callback.Invoke();
        }
    }
}
