using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 明细页按日分组组头（步骤03 §2.2）：左侧"09月26日 星期六"，右侧"支出: 28.8"（当日支出合计，收入不上组头）。
/// 预制体由 DetailPanelBuilder 生成（Assets/Prefabs/DayHeaderUI.prefab），DetailPanelUI 重建列表时动态实例化。
/// </summary>
public class DayHeaderUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI dateLabel;
    [SerializeField] private TextMeshProUGUI expenseLabel;

    /// <summary>
    /// 组头日期文字（自检定位用）
    /// </summary>
    public string DateText
    {
        get { return dateLabel != null ? dateLabel.text : ""; }
    }

    /// <summary>
    /// 组头当日支出文字（自检核对合计用）
    /// </summary>
    public string ExpenseText
    {
        get { return expenseLabel != null ? expenseLabel.text : ""; }
    }

    /// <summary>
    /// 填充组头：日期文字与当日支出文字均由 DetailPanelUI 格式化后传入
    /// </summary>
    public void Setup(string dateText, string expenseText)
    {
        if (dateLabel != null)
        {
            dateLabel.text = dateText;
        }

        if (expenseLabel != null)
        {
            expenseLabel.text = expenseText;
        }
    }
}
