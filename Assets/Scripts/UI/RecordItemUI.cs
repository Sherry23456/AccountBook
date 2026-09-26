using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 明细页流水行（步骤03 §2.2/§4）：圆形图标 + 分类名（有备注则"分类 备注"拼接）+ 右侧带符号金额。
/// 点击与长按（>0.5s）用 Pointer 接口按时长判定，不挂 onClick（步骤03 §4 坑位）；长按触发后本次抬起不再算点击。
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
    /// 短按点击通知（DetailPanelUI → 打开编辑）
    /// </summary>
    public event Action<AccountRecord> Clicked;

    /// <summary>
    /// 长按通知（DetailPanelUI → 删除确认弹窗）
    /// </summary>
    public event Action<AccountRecord> LongPressed;

    private bool pressing = false;
    private bool longPressFired = false;
    private float pressStartTime = 0f;

    private void Update()
    {
        if (pressing && longPressFired == false &&
            Time.unscaledTime - pressStartTime >= LongPressSeconds)
        {
            longPressFired = true;
            pressing = false;
            SetPressedVisual(false);
            LongPressed?.Invoke(Record);
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
        SetPressedVisual(true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (pressing && longPressFired == false)
        {
            Clicked?.Invoke(Record);
        }

        pressing = false;
        SetPressedVisual(false);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // 拖出判定区取消本次按压（不触发点击也不触发长按）
        pressing = false;
        SetPressedVisual(false);
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
    /// 模拟短按点击（RecordFlowCheck 用）
    /// </summary>
    public void SimulateClick()
    {
        Clicked?.Invoke(Record);
    }

    /// <summary>
    /// 模拟长按（RecordFlowCheck/截图用）
    /// </summary>
    public void SimulateLongPress()
    {
        LongPressed?.Invoke(Record);
    }
}
