using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 导出弹窗控制器（步骤05，策划案 §5.5）：周/月/年三档 Toggle（默认继承图表页传入档位）+
/// ‹ 周期 › 前后平移 + 预览行（N 笔/支出合计/收入合计，与导出同口径 GetRecordsInRange，
/// 补记历史记录按 Date 归周期）+ 导出 Excel/取消。0 笔时导出按钮置灰不生成文件。
/// 点遮罩关闭复用步骤01 壳上 UIManager.CloseExportPanel 的持久监听。
/// 刷新链：档位/周期变化 → ShiftRange 算区间 → 查询统计 → 标题/预览/按钮可用性同步。
/// </summary>
public class ExportPanelUI : MonoBehaviour
{
    private static readonly Color ColorToggleTextOn = Color.white;
    private static readonly Color ColorToggleTextOff = new Color32(0x22, 0x22, 0x22, 0xFF);

    [Header("Dependencies")]
    [SerializeField] private AccountManager accountManager;
    [SerializeField] private ExcelTransferManager excelTransferManager;

    [Header("Range Toggles")]
    [SerializeField] private Toggle toggleWeek;
    [SerializeField] private Toggle toggleMonth;
    [SerializeField] private Toggle toggleYear;
    [SerializeField] private TextMeshProUGUI toggleWeekLabel;
    [SerializeField] private TextMeshProUGUI toggleMonthLabel;
    [SerializeField] private TextMeshProUGUI toggleYearLabel;
    [SerializeField] private Image toggleWeekSelectedBg;
    [SerializeField] private Image toggleMonthSelectedBg;
    [SerializeField] private Image toggleYearSelectedBg;

    [Header("Period & Preview")]
    [SerializeField] private Button btnPrev;
    [SerializeField] private Button btnNext;
    [SerializeField] private TextMeshProUGUI txtPeriod;
    [SerializeField] private TextMeshProUGUI txtPreview;

    [Header("Actions")]
    [SerializeField] private Button btnExport;
    [SerializeField] private Button btnCancel;

    private ExportRange currentRange = ExportRange.Week;

    /// <summary>
    /// 当前周期的代表日期（Open 传入 start 解析而来：周=周一、月=1日、年=1月1日，
    /// ShiftRange 以它重算当期，‹›平移也基于它，避免存 start/end 两份状态）
    /// </summary>
    private DateTime focusDate = DateTime.Now.Date;
    private string currentStart = string.Empty;
    private string currentEnd = string.Empty;

    private void OnEnable()
    {
        TryInitializeDependencies();
        RegisterEvents();
        RefreshAll();
    }

    /// <summary>
    /// 外部引用为空时自动查找（弹窗初始未激活，需包含未激活对象）
    /// </summary>
    private void TryInitializeDependencies()
    {
        if (accountManager == null)
        {
            accountManager = FindFirstObjectByType<AccountManager>();
        }

        if (excelTransferManager == null)
        {
            excelTransferManager = FindFirstObjectByType<ExcelTransferManager>();
        }
    }

    /// <summary>
    /// 图表页导出入口：带入当前档位与周期（start 即本周期首日，作为 focus）。
    /// 弹窗已激活时 SetActive 不触发 OnEnable，必须显式刷新，否则周期/预览停留在旧状态。
    /// </summary>
    public void Open(ExportRange range, string startDate, string endDate)
    {
        if (!DateTime.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out focusDate))
        {
            focusDate = DateTime.Now.Date;
        }

        currentRange = range;

        if (gameObject.activeSelf)
        {
            RefreshAll();
        }
        else
        {
            gameObject.SetActive(true);   // OnEnable 里 RefreshAll
        }
    }

    /// <summary>
    /// 事件绑定（幂等，可重复调用）
    /// </summary>
    private void RegisterEvents()
    {
        RegisterButton(btnPrev, () => ShiftPeriod(-1));
        RegisterButton(btnNext, () => ShiftPeriod(1));
        RegisterButton(btnExport, OnExportClicked);
        RegisterButton(btnCancel, Close);

        RegisterToggle(toggleWeek, ExportRange.Week);
        RegisterToggle(toggleMonth, ExportRange.Month);
        RegisterToggle(toggleYear, ExportRange.Year);
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

    private void RegisterToggle(Toggle toggle, ExportRange range)
    {
        if (toggle == null)
        {
            return;
        }

        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener(isOn =>
        {
            if (isOn)
            {
                SetRange(range);
            }
        });
    }

    // ---------- 交互 ----------

    /// <summary>
    /// 切档位（Toggle 组触发）：档位变了周期跟着 focus 所在的新周期走
    /// </summary>
    private void SetRange(ExportRange range)
    {
        if (currentRange == range)
        {
            UpdateToggleVisuals();
            return;
        }

        currentRange = range;
        RefreshAll();
    }

    /// <summary>
    /// ‹ › 周期平移：周 ±7 天、月 ±1 月、年 ±1 年
    /// </summary>
    private void ShiftPeriod(int offset)
    {
        switch (currentRange)
        {
            case ExportRange.Week:
                focusDate = focusDate.AddDays(offset * 7);
                break;

            case ExportRange.Month:
                focusDate = focusDate.AddMonths(offset);
                break;

            case ExportRange.Year:
                focusDate = focusDate.AddYears(offset);
                break;
        }

        RefreshAll();
    }

    private void OnExportClicked()
    {
        if (excelTransferManager == null)
        {
            ToastUI.Show("导出服务未初始化");
            return;
        }

        bool success = excelTransferManager.ExportExcel(currentRange, currentStart, currentEnd,
            out string fileName, out string fullPath, out string message);

        if (success)
        {
            ToastUI.Show("导出完成: " + fileName);
            Close();
        }
        else
        {
            ToastUI.Show(message);
        }
    }

    /// <summary>
    /// 关闭弹窗（取消/导出成功后）；遮罩点击走 UIManager.CloseExportPanel 同效
    /// </summary>
    public void Close()
    {
        gameObject.SetActive(false);
    }

    // ---------- 刷新 ----------

    private void RefreshAll()
    {
        DateRangeUtil.ShiftRange(currentRange, focusDate, 0, out currentStart, out currentEnd);

        if (txtPeriod != null)
        {
            txtPeriod.text = FormatPeriodTitle(currentRange, currentStart, currentEnd);
        }

        int count = 0;
        long expenseFen = 0;
        long incomeFen = 0;

        if (accountManager != null)
        {
            List<AccountRecord> records = accountManager.GetRecordsInRange(currentStart, currentEnd);
            count = records.Count;

            for (int i = 0; i < records.Count; i++)
            {
                AccountRecord record = records[i];

                if (record == null)
                {
                    continue;
                }

                if (record.Type == (int)RecordType.Income)
                {
                    incomeFen += record.AmountFen;
                }
                else
                {
                    expenseFen += record.AmountFen;
                }
            }
        }

        if (txtPreview != null)
        {
            txtPreview.text = count == 0
                ? "本周期没有记录"
                : $"本周期 {count} 笔，支出 {MoneyText.FormatYuan(expenseFen)}，收入 {MoneyText.FormatYuan(incomeFen)}";
        }

        if (btnExport != null)
        {
            btnExport.interactable = count > 0;
        }

        SyncToggleStates();
        UpdateToggleVisuals();
    }

    /// <summary>
    /// 周期标题（步骤05 §2.4）：周 "2026-09-21 ~ 09-27" / 月 "2026-09" / 年 "2026 年"
    /// </summary>
    private static string FormatPeriodTitle(ExportRange range, string start, string end)
    {
        if (range == ExportRange.Week && end != null && end.Length == 10)
        {
            return start + " ~ " + end.Substring(5);
        }

        if (range == ExportRange.Month && start != null && start.Length >= 7)
        {
            return start.Substring(0, 7);
        }

        if (range == ExportRange.Year && start != null && start.Length >= 4)
        {
            return start.Substring(0, 4) + " 年";
        }

        return start;
    }

    /// <summary>
    /// 档位 Toggle 与 currentRange 强制同步（Open 带入档位后视觉必须一致；
    /// isOn 变化会触发 SetRange，同档防重入直接返回）
    /// </summary>
    private void SyncToggleStates()
    {
        if (toggleWeek != null)
        {
            toggleWeek.SetIsOnWithoutNotify(currentRange == ExportRange.Week);
        }

        if (toggleMonth != null)
        {
            toggleMonth.SetIsOnWithoutNotify(currentRange == ExportRange.Month);
        }

        if (toggleYear != null)
        {
            toggleYear.SetIsOnWithoutNotify(currentRange == ExportRange.Year);
        }
    }

    /// <summary>
    /// 选中块与文字颜色首帧同步（toggle.graphic 是 CrossFadeAlpha 控制，0 时长刷一遍，照搬图表页）
    /// </summary>
    private void UpdateToggleVisuals()
    {
        ApplyToggleVisual(toggleWeek, toggleWeekLabel, toggleWeekSelectedBg);
        ApplyToggleVisual(toggleMonth, toggleMonthLabel, toggleMonthSelectedBg);
        ApplyToggleVisual(toggleYear, toggleYearLabel, toggleYearSelectedBg);
    }

    private static void ApplyToggleVisual(Toggle toggle, TextMeshProUGUI label, Image selectedBg)
    {
        bool selected = toggle != null && toggle.isOn;

        if (label != null)
        {
            label.color = selected ? ColorToggleTextOn : ColorToggleTextOff;
        }

        if (selectedBg != null)
        {
            selectedBg.canvasRenderer.SetAlpha(selected ? 1f : 0f);
        }
    }
}
