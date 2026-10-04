using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 设置页"音效"行（步骤08b）：整行为按钮，左"音效"标签、右"开/关"值，点击切换 SfxManager
/// 全局音效（PlayerPrefs 持久化）。由 15 号 Builder 搭建并接线 valueLabel；静态"音效"文字由 Builder 直接创建。
/// </summary>
public class SfxToggleUI : MonoBehaviour
{
    private static readonly Color ValueColorOn = new Color(0.13f, 0.13f, 0.13f);
    private static readonly Color ValueColorOff = new Color(0.6f, 0.6f, 0.6f);

    [SerializeField] private TextMeshProUGUI valueLabel;

    private void Awake()
    {
        Button button = GetComponent<Button>();

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(SfxManager.PlayClick);
            button.onClick.AddListener(HandleToggleClicked);
        }
    }

    private void OnEnable()
    {
        Refresh();
    }

    /// <summary>
    /// 切换全局开关并刷新"开/关"；开启瞬间再响一声点击音作确认
    /// </summary>
    private void HandleToggleClicked()
    {
        SfxManager.SetSoundEnabled(!SfxManager.SoundEnabled);
        Refresh();

        if (SfxManager.SoundEnabled)
        {
            SfxManager.PlayClick();
        }
    }

    private void Refresh()
    {
        bool on = SfxManager.SoundEnabled;

        if (valueLabel != null)
        {
            valueLabel.text = on ? "开" : "关";
            valueLabel.color = on ? ValueColorOn : ValueColorOff;
        }
    }
}
