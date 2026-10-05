using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 底栏页签图标态刷新：当前主面板对应页签用 Selected 黑图+黑字，其余用 Normal 灰图+灰字。
/// 由 UIManager.SetOnlyPanelActive 统一驱动；记账页（Record）四页签全灰，
/// 正中黄圆钮本身即视觉焦点；发现子页（账单/预算）归发现高亮。
/// </summary>
public class BottomNavUI : MonoBehaviour
{
    public enum NavTab { Detail = 0, Discover = 1, Record = 2, Chart = 3, Settings = 4 }

    private static readonly Color ColSelectedText = FromHex(0x222222);
    private static readonly Color ColNormalText = FromHex(0x666666);

    [Header("明细")]
    [SerializeField] private Image detailIcon;
    [SerializeField] private TextMeshProUGUI detailLabel;
    [SerializeField] private Sprite detailNormal;
    [SerializeField] private Sprite detailSelected;

    [Header("发现")]
    [SerializeField] private Image discoverIcon;
    [SerializeField] private TextMeshProUGUI discoverLabel;
    [SerializeField] private Sprite discoverNormal;
    [SerializeField] private Sprite discoverSelected;

    [Header("图表")]
    [SerializeField] private Image chartIcon;
    [SerializeField] private TextMeshProUGUI chartLabel;
    [SerializeField] private Sprite chartNormal;
    [SerializeField] private Sprite chartSelected;

    [Header("设置")]
    [SerializeField] private Image settingsIcon;
    [SerializeField] private TextMeshProUGUI settingsLabel;
    [SerializeField] private Sprite settingsNormal;
    [SerializeField] private Sprite settingsSelected;

    /// <summary>
    /// 按当前主面板刷新四个页签的图标与文字态
    /// </summary>
    public void SetTab(NavTab tab)
    {
        Apply(detailIcon, detailLabel, detailNormal, detailSelected, tab == NavTab.Detail);
        Apply(discoverIcon, discoverLabel, discoverNormal, discoverSelected, tab == NavTab.Discover);
        Apply(chartIcon, chartLabel, chartNormal, chartSelected, tab == NavTab.Chart);
        Apply(settingsIcon, settingsLabel, settingsNormal, settingsSelected, tab == NavTab.Settings);
    }

    private static void Apply(Image icon, TextMeshProUGUI label, Sprite normal, Sprite selected, bool isOn)
    {
        if (icon != null && normal != null && selected != null)
        {
            icon.sprite = isOn ? selected : normal;
        }

        if (label != null)
        {
            label.color = isOn ? ColSelectedText : ColNormalText;
        }
    }

    private static Color FromHex(long hex)
    {
        return new Color(
            ((hex >> 16) & 0xFF) / 255f,
            ((hex >> 8) & 0xFF) / 255f,
            (hex & 0xFF) / 255f,
            1f);
    }
}
