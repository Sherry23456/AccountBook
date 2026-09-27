using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 周期条按钮（步骤04 §2.2）：白底黑字 / 选中黑底白字，由 ChartPanelBuilder 生成预制体，
/// ChartPanelUI 每次档位切换重建 6 个（左旧右新，本期在最右）。
/// </summary>
public class PeriodStripButton : MonoBehaviour
{
    private static readonly Color ColorNormalBg = Color.white;
    private static readonly Color ColorSelectedBg = new Color32(0x22, 0x22, 0x22, 0xFF);
    private static readonly Color ColorNormalText = new Color32(0x22, 0x22, 0x22, 0xFF);
    private static readonly Color ColorSelectedText = Color.white;

    [SerializeField] private Image backgroundImage;
    [SerializeField] private TextMeshProUGUI label;

    /// <summary>
    /// 相对本期偏移：0=本期，-1 上一周期……（SelectPeriod 的入参）
    /// </summary>
    public int Offset { get; private set; }

    /// <summary>
    /// 按钮文字（自检核对用）
    /// </summary>
    public string LabelText
    {
        get { return label != null ? label.text : ""; }
    }

    public void Setup(int offset, string text, bool selected)
    {
        Offset = offset;

        if (label != null)
        {
            label.text = text;
            label.color = selected ? ColorSelectedText : ColorNormalText;
        }

        if (backgroundImage != null)
        {
            backgroundImage.color = selected ? ColorSelectedBg : ColorNormalBg;
        }
    }
}
