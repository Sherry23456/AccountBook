using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 明细页流水行（步骤03 §2.2/§4）：圆形图标 + 分类名（有备注则"分类 备注"拼接）+ 右侧带符号金额。
/// 操作统一由长按（>0.5s）承载：弹 修改/删除 双选项弹窗；单击不触发任何操作（防误触，手感优化改版）。
/// 用 Pointer 接口按时长判定，不挂 onClick（步骤03 §4 坑位）。
/// 滑动列表时手指落在本行再抬起仍会收到 OnPointerUp（指针捕获），所以按压期间按位移超 touch slop
/// 判定为滚动手势：取消按压态与长按，防止滑动误弹操作窗。
/// 预制体由 DetailPanelBuilder 生成（Assets/Prefabs/RecordItemUI.prefab），DetailPanelUI 重建列表时动态实例化。
/// </summary>
public class RecordItemUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    /// <summary>
    /// 按下持续超过该秒数视为长按（删除确认入口）
    /// </summary>
    private const float LongPressSeconds = 0.5f;

    private static readonly Color ColorNormal = Color.white;
    private static readonly Color ColorPressed = new Color32(0xF0, 0xF0, 0xF0, 0xFF);

    [SerializeField] private Image backgroundImage;   // 行底色（同时是可点 raycast 面）
    [SerializeField] private Image iconCircleImage;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI amountLabel;

    /// <summary>
    /// 本行绑定的账目记录（点击/长按事件携带，编辑/删除用）
    /// </summary>
    public AccountRecord Record { get; private set; }

    /// <summary>
    /// 金额文字（自检核对格式/颜色用）
    /// </summary>
    public string AmountText
    {
        get { return amountLabel != null ? amountLabel.text : ""; }
    }

    /// <summary>
    /// 金额颜色（自检核对收入强调色用）
    /// </summary>
    public Color AmountColor
    {
        get { return amountLabel != null ? amountLabel.color : Color.white; }
    }

    /// <summary>
    /// 行分类名文字（自检核对回填用）
    /// </summary>
    public string NameText
    {
        get { return nameLabel != null ? nameLabel.text : ""; }
    }

    /// <summary>
    /// 长按通知（DetailPanelUI → 弹 修改/删除 双选项弹窗）
    /// </summary>
    public event Action<AccountRecord> LongPressed;

    private bool pressing = false;
    private bool longPressFired = false;
    private float pressStartTime = 0f;
    private Vector2 pressScreenPos = Vector2.zero;

    private void Update()
    {
        if (pressing && longPressFired == false)
        {
            if (MovedBeyondSlopNow())
            {
                // 滑动（滚动列表）手势：取消按压态与长按，抬起不再算点击
                pressing = false;
                SetPressedVisual(false);
            }
            else if (Time.unscaledTime - pressStartTime >= LongPressSeconds)
            {
                longPressFired = true;
                pressing = false;
                SetPressedVisual(false);
                LongPressed?.Invoke(Record);
            }
        }
    }

    /// <summary>
    /// 填充行数据；icon 为 null 时隐藏图标位（不影响点击与文字）
    /// </summary>
    public void Setup(AccountRecord record, Sprite icon)
    {
        Record = record;

        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        if (nameLabel != null)
        {
            string note = record != null ? record.Note : "";
            string category = record != null ? record.Category : "";
            nameLabel.text = string.IsNullOrEmpty(note) ? category : category + " " + note;
        }

        if (amountLabel != null && record != null)
        {
            RecordType type = (RecordType)record.Type;
            amountLabel.text = MoneyText.Format(record.AmountFen, type);
            amountLabel.color = MoneyText.GetColor(type);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pressing = true;
        longPressFired = false;
        pressStartTime = Time.unscaledTime;
        pressScreenPos = eventData.position;
        SetPressedVisual(true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        // 单击不触发任何操作（防误触）；操作统一由长按弹窗承载
        pressing = false;
        SetPressedVisual(false);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // 拖出判定区取消本次按压（不触发点击也不触发长按）
        pressing = false;
        SetPressedVisual(false);
    }

    /// <summary>
    /// 触控滑动阈值：Android 原生 touch slop 8dp 换算成物理像素，下限 10px（编辑器 Screen.dpi=96 时兜底）
    /// </summary>
    private static float TouchSlopPx
    {
        get { return Mathf.Max(10f, Screen.dpi / 20f); }
    }

    private bool MovedBeyondSlop(Vector2 position)
    {
        return (position - pressScreenPos).sqrMagnitude >= TouchSlopPx * TouchSlopPx;
    }

    /// <summary>
    /// 按压期间逐帧核对当前输入位置：ScrollRect 拖动中不保证派发 move/exit 事件，用原始输入兜底。
    /// 判定"所有触点都离开了按压点附近"才算滑动，避免双指场景（一指滚动、另一指点另一行）误取消。
    /// </summary>
    private bool MovedBeyondSlopNow()
    {
        if (Input.touchCount > 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                if (MovedBeyondSlop(Input.GetTouch(i).position) == false)
                {
                    return false;
                }
            }

            return true;
        }

        return MovedBeyondSlop(Input.mousePosition);
    }

    private void OnDisable()
    {
        pressing = false;
        SetPressedVisual(false);
    }

    /// <summary>
    /// 按压视觉反馈：行底色加深
    /// </summary>
    private void SetPressedVisual(bool pressed)
    {
        if (backgroundImage != null)
        {
            backgroundImage.color = pressed ? ColorPressed : ColorNormal;
        }
    }

    // ---------- 编辑器自检入口（Play 自检无法合成指针事件，直接上抛事件） ----------

    /// <summary>
    /// 模拟长按（RecordFlowCheck/截图用）
    /// </summary>
    public void SimulateLongPress()
    {
        LongPressed?.Invoke(Record);
    }
}
