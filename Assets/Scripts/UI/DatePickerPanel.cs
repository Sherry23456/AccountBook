using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 记账日期选择弹窗（补记漏账）：迷你月历 + 上/下月切换 + 回到今天。
/// 周一为第一列；点某天即确认并关闭（DayPicked 通知）；未来日期与其他月日期不可选；点遮罩关闭不改变选择。
/// 由 RecordPanelBuilder 搭建在 RecordPanel 内（42 个日格固定，运行时只刷新文字与颜色）。
/// </summary>
public class DatePickerPanel : MonoBehaviour
{
    private static readonly Color ColYellow = new Color32(0xFF, 0xD1, 0x00, 0xFF);
    private static readonly Color ColBlack = new Color32(0x22, 0x22, 0x22, 0xFF);
    private static readonly Color ColYellowText = new Color32(0xFF, 0xD1, 0x00, 0xFF);
    private static readonly Color ColGray = new Color32(0xBB, 0xBB, 0xBB, 0xFF);
    private static readonly Color ColDayNormal = new Color32(0xF2, 0xF2, 0xF2, 0xFF);
    private static readonly Color ColDayDisabled = new Color32(0xFA, 0xFA, 0xFA, 0xFF);

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private Button prevMonthButton;
    [SerializeField] private Button nextMonthButton;

    [Header("Day Grid")]
    [SerializeField] private Button[] dayButtons = new Button[42];   // 6行×7列，周一起

    [Header("Footer & Mask")]
    [SerializeField] private Button backTodayButton;
    [SerializeField] private Button maskButton;

    private readonly List<TextMeshProUGUI> dayLabels = new List<TextMeshProUGUI>();

    private DateTime displayMonth;   // 当前展示月份（取该月 1 号）
    private DateTime selectedDate;   // 暂存选中日期
    private DateTime today;          // 打开时的"今天"（0 点）

    /// <summary>
    /// 点选某天后通知 RecordPanelUI（参数为所选日期 0 点）
    /// </summary>
    public event Action<DateTime> DayPicked;

    private void OnEnable()
    {
        today = DateTime.Now.Date;
        CacheDayLabels();
        RegisterEvents();

        if (displayMonth == default)
        {
            displayMonth = new DateTime(today.Year, today.Month, 1);
            RefreshGrid();
        }
    }

    /// <summary>
    /// 打开弹窗：定位到 initial 所在月份并高亮该日期
    /// </summary>
    public void Show(DateTime initial)
    {
        today = DateTime.Now.Date;
        selectedDate = initial.Date;
        displayMonth = new DateTime(initial.Year, initial.Month, 1);

        CacheDayLabels();
        RegisterEvents();
        RefreshGrid();
        gameObject.SetActive(true);
    }

    /// <summary>
    /// 关闭弹窗（不触发 DayPicked）
    /// </summary>
    public void Close()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 缓存 42 个日格的文字引用（结构由搭建脚本固定）
    /// </summary>
    private void CacheDayLabels()
    {
        dayLabels.Clear();

        if (dayButtons == null)
        {
            return;
        }

        for (int i = 0; i < dayButtons.Length; i++)
        {
            dayLabels.Add(dayButtons[i] != null ? dayButtons[i].GetComponentInChildren<TextMeshProUGUI>() : null);
        }
    }

    /// <summary>
    /// 按钮事件绑定（幂等，可重复调用）
    /// </summary>
    private void RegisterEvents()
    {
        RegisterButton(prevMonthButton, OnPrevMonthClicked);
        RegisterButton(nextMonthButton, OnNextMonthClicked);
        RegisterButton(backTodayButton, OnBackTodayClicked);
        RegisterButton(maskButton, Close);

        if (dayButtons != null)
        {
            for (int i = 0; i < dayButtons.Length; i++)
            {
                int index = i;   // 闭包捕获循环变量
                RegisterButton(dayButtons[i], () => OnDayCellClicked(index));
            }
        }
    }

    /// <summary>
    /// 按钮通用绑定
    /// </summary>
    private static void RegisterButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(SfxManager.PlayClick);
        button.onClick.AddListener(action);
    }

    /// <summary>
    /// 刷新标题/翻月可用性/42 个日格的文字与选中态
    /// </summary>
    private void RefreshGrid()
    {
        if (displayMonth == default)
        {
            displayMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        }

        if (titleLabel != null)
        {
            titleLabel.text = displayMonth.ToString("yyyy年M月");
        }

        if (nextMonthButton != null)
        {
            // 不允许翻到未来月份
            nextMonthButton.interactable = displayMonth < new DateTime(today.Year, today.Month, 1);
        }

        // 周一为第一列：DayOfWeek Sunday=0 → 周一偏移 1
        int leadCells = ((int)displayMonth.DayOfWeek + 6) % 7;
        DateTime gridStart = displayMonth.AddDays(-leadCells);

        if (dayButtons == null)
        {
            return;
        }

        for (int i = 0; i < dayButtons.Length; i++)
        {
            DateTime cellDate = gridStart.AddDays(i);
            Button button = dayButtons[i];
            TextMeshProUGUI label = i < dayLabels.Count ? dayLabels[i] : null;

            bool inMonth = cellDate.Month == displayMonth.Month && cellDate.Year == displayMonth.Year;
            bool isFuture = cellDate > today;
            bool isSelected = cellDate == selectedDate;
            bool isToday = cellDate == today;
            bool interactive = inMonth && isFuture == false;

            if (button != null)
            {
                button.interactable = interactive;

                if (button.image != null)
                {
                    button.image.color = isSelected ? ColYellow
                        : isToday ? ColBlack
                        : interactive ? ColDayNormal
                        : ColDayDisabled;
                }
            }

            if (label != null)
            {
                label.text = cellDate.Day.ToString();
                label.color = isSelected ? ColBlack
                    : isToday ? ColYellowText
                    : interactive ? ColBlack
                    : ColGray;
            }
        }
    }

    /// <summary>
    /// 由日格下标还原日期并校验（双保险：其他月/未来日期不生效）
    /// </summary>
    private DateTime GetCellDate(int index)
    {
        int leadCells = ((int)displayMonth.DayOfWeek + 6) % 7;
        return displayMonth.AddDays(-leadCells).AddDays(index);
    }

    private void OnDayCellClicked(int index)
    {
        if (dayButtons == null || index < 0 || index >= dayButtons.Length)
        {
            return;
        }

        DateTime cellDate = GetCellDate(index);
        bool inMonth = cellDate.Month == displayMonth.Month && cellDate.Year == displayMonth.Year;

        if (inMonth == false || cellDate > today)
        {
            return;
        }

        selectedDate = cellDate;
        DayPicked?.Invoke(cellDate);
        Close();
    }

    private void OnPrevMonthClicked()
    {
        displayMonth = displayMonth.AddMonths(-1);
        RefreshGrid();
    }

    private void OnNextMonthClicked()
    {
        if (displayMonth < new DateTime(today.Year, today.Month, 1))
        {
            displayMonth = displayMonth.AddMonths(1);
            RefreshGrid();
        }
    }

    /// <summary>
    /// 回到今天：选中今天并立即确认关闭（补记快捷键）
    /// </summary>
    private void OnBackTodayClicked()
    {
        selectedDate = today;
        displayMonth = new DateTime(today.Year, today.Month, 1);
        DayPicked?.Invoke(today);
        Close();
    }
}
