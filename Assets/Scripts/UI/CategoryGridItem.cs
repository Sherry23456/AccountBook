using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 记账页分类宫格子项（步骤02）：圆底 Image + 分类图标 + 名称，选中态圆底变主黄（录屏同款）。
/// 预制体由 RecordPanelBuilder 生成（Assets/Prefabs/CategoryGridItem.prefab），RecordPanelUI 切页签时动态实例化。
/// </summary>
public class CategoryGridItem : MonoBehaviour
{
    private static readonly Color NormalColor = new Color32(0xF2, 0xF2, 0xF2, 0xFF);
    private static readonly Color SelectedColor = new Color32(0xFF, 0xD1, 0x00, 0xFF);

    [SerializeField] private Image circleImage;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private Button clickButton;

    /// <summary>
    /// 分类名（与 CategoryTable.Name 对应，保存落库用）
    /// </summary>
    public string CategoryName { get; private set; }

    /// <summary>
    /// 点击通知，RecordPanelUI 订阅后统一处理选中态
    /// </summary>
    public event Action<CategoryGridItem> Clicked;

    /// <summary>
    /// 填充分类数据；icon 为 null 时隐藏图标位（不影响点击与名称显示）
    /// </summary>
    public void Setup(string categoryName, Sprite icon)
    {
        CategoryName = categoryName;

        if (nameLabel != null)
        {
            nameLabel.text = categoryName;
        }

        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        if (clickButton != null)
        {
            clickButton.onClick.RemoveAllListeners();
            clickButton.onClick.AddListener(HandleClick);
        }
    }

    /// <summary>
    /// 选中态切换：圆底变色
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (circleImage != null)
        {
            circleImage.color = selected ? SelectedColor : NormalColor;
        }
    }

    /// <summary>
    /// 子项点击 → 上抛给 RecordPanelUI 统一处理
    /// </summary>
    private void HandleClick()
    {
        Clicked?.Invoke(this);
    }
}
