using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 排行榜行（步骤04 §2.5）：圆底图标 + 分类名 + 占比文本 + 灰底条上叠黄填充条
/// （Image Type=Filled/Horizontal，fillOrigin=Right 从右往左填，步骤04 §4）+ 右侧金额。
/// 预制体由 ChartPanelBuilder 生成（Assets/Prefabs/RankItemUI.prefab），ChartPanelUI 刷新时动态实例化。
/// </summary>
public class RankItemUI : MonoBehaviour
{
    [SerializeField] private Image iconCircleImage;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI percentLabel;
    [SerializeField] private Image barBackgroundImage;
    [SerializeField] private Image barFillImage;
    [SerializeField] private TextMeshProUGUI amountLabel;

    /// <summary>
    /// 分类名文字（自检核对用）
    /// </summary>
    public string NameText
    {
        get { return nameLabel != null ? nameLabel.text : ""; }
    }

    /// <summary>
    /// 占比文字（自检核对用）
    /// </summary>
    public string PercentText
    {
        get { return percentLabel != null ? percentLabel.text : ""; }
    }

    /// <summary>
    /// 金额文字（自检核对用）
    /// </summary>
    public string AmountText
    {
        get { return amountLabel != null ? amountLabel.text : ""; }
    }

    /// <summary>
    /// 占比条填充值（自检核对用）
    /// </summary>
    public float FillAmountValue
    {
        get { return barFillImage != null ? barFillImage.fillAmount : 0f; }
    }

    /// <summary>
    /// 填充行数据：占比取整百分比；条长 = Ratio
    /// </summary>
    public void Setup(CategoryRank rank, Sprite icon)
    {
        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        if (nameLabel != null)
        {
            nameLabel.text = rank != null ? rank.Category : "";
        }

        if (percentLabel != null)
        {
            percentLabel.text = rank != null ? Mathf.RoundToInt(rank.Ratio * 100f) + "%" : "";
        }

        if (amountLabel != null)
        {
            amountLabel.text = rank != null ? MoneyText.FormatTrim(rank.AmountFen) : "";
        }

        if (barFillImage != null)
        {
            barFillImage.fillAmount = rank != null ? Mathf.Clamp01(rank.Ratio) : 0f;
        }
    }
}
