using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 明细页控制器（步骤03）：年月切换（‹ ›，跨年自动进位）+ 当月收/支合计 + 按日分组流水列表。
/// 列表 = 滚动区 Content（VerticalLayoutGroup）下平铺 组头 + 记录行（不做嵌套 LayoutGroup，避开
/// 子项 PreferredSize 晚一帧的坑；<千条直接全清全建，不做对象池）。
/// 点击行 → RecordPanelUI.SetupForEdit；长按行 → ConfirmDialog 确认后删除。
/// 数据刷新：OnEnable 回到当前月全量重建；AccountManager.OnDataChanged 广播后重建当前查看月。
/// </summary>
public class DetailPanelUI : MonoBehaviour
{
    /// <summary>
    /// 固定中文星期名（步骤03 §4：不依赖系统区域，防真机显示英文）
    /// </summary>
    private static readonly string[] WeekdayNames =
        { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };

    [Header("Dependencies")]
    [SerializeField] private AccountManager accountManager;
    [SerializeField] private CategoryIconProvider iconProvider;
    [SerializeField] private RecordPanelUI recordPanelUI;
    [SerializeField] private UIManager uiManager;

    [Header("Month Header")]
    [SerializeField] private Button btnPrevMonth;
    [SerializeField] private Button btnNextMonth;
    [SerializeField] private TextMeshProUGUI txtYearMonth;
    [SerializeField] private TextMeshProUGUI txtIncome;
    [SerializeField] private TextMeshProUGUI txtExpense;

    [Header("Grouped List")]
    [SerializeField] private RectTransform listContent;
    [SerializeField] private DayHeaderUI dayHeaderPrefab;
    [SerializeField] private RecordItemUI recordItemPrefab;
    [SerializeField] private GameObject emptyLabel;
    [SerializeField] private ConfirmDialog confirmDialog;

    private int viewYear;    // 当前查看年份
    private int viewMonth;   // 当前查看月份（1~12）

    /// <summary>
    /// 列表容器（自检/截图定位用）
    /// </summary>
    public RectTransform ListContent
    {
        get { return listContent; }
    }

    /// <summary>
    /// 确认弹窗（自检/截图用）
    /// </summary>
    public ConfirmDialog ConfirmBox
    {
        get { return confirmDialog; }
    }

    private void OnEnable()
    {
        TryInitializeDependencies();
        RegisterEvents();

        if (accountManager != null)
        {
            accountManager.OnDataChanged -= RefreshCurrentView;
            accountManager.OnDataChanged += RefreshCurrentView;
        }

        ResetToCurrentMonth();
    }

    private void OnDisable()
    {
        if (accountManager != null)
        {
            accountManager.OnDataChanged -= RefreshCurrentView;
        }
    }

    /// <summary>
    /// 外部引用为空时自动查找（QuikDelivery 模式；记录页初始未激活需包含未激活对象）
    /// </summary>
    private void TryInitializeDependencies()
    {
        if (accountManager == null)
        {
            accountManager = FindFirstObjectByType<AccountManager>();
        }

        if (iconProvider == null)
        {
            iconProvider = FindFirstObjectByType<CategoryIconProvider>();
        }

        if (recordPanelUI == null)
        {
            recordPanelUI = FindFirstObjectByType<RecordPanelUI>(FindObjectsInactive.Include);
        }

        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>();
        }
    }

    /// <summary>
    /// 按钮事件绑定（幂等，可重复调用）
    /// </summary>
    private void RegisterEvents()
    {
        RegisterButton(btnPrevMonth, OnPrevMonthClicked);
        RegisterButton(btnNextMonth, OnNextMonthClicked);
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

    // ---------- 年月切换 ----------

    private void OnPrevMonthClicked()
    {
        viewMonth--;

        if (viewMonth < 1)
        {
            viewMonth = 12;
            viewYear--;
        }

        RefreshAll();
    }

    private void OnNextMonthClicked()
    {
        viewMonth++;

        if (viewMonth > 12)
        {
            viewMonth = 1;
            viewYear++;
        }

        RefreshAll();
    }

    /// <summary>
    /// 回到当前月（面板每次打开都回到今天所在月份，保存/删除后看到最新数据）
    /// </summary>
    private void ResetToCurrentMonth()
    {
        DateTime now = DateTime.Now;
        viewYear = now.Year;
        viewMonth = now.Month;
        RefreshAll();
    }

    /// <summary>
    /// yyyy-MM（仓储查询键）
    /// </summary>
    private string GetMonthKey()
    {
        return viewYear.ToString("D4") + "-" + viewMonth.ToString("D2");
    }

    // ---------- 刷新 ----------

    /// <summary>
    /// OnDataChanged 订阅目标：明细页激活中才重建（编辑时本页被隐藏，回到页面时 OnEnable 已刷新）
    /// </summary>
    private void RefreshCurrentView()
    {
        if (isActiveAndEnabled)
        {
            RefreshAll();
        }
    }

    /// <summary>
    /// 全量刷新：年月标题 + 收/支合计 + 分组列表重建（<千条全清全建，步骤03 §4）
    /// </summary>
    private void RefreshAll()
    {
        if (txtYearMonth != null)
        {
            txtYearMonth.text = viewYear.ToString("D4") + "年 " + viewMonth.ToString("D2") + "月";
        }

        if (accountManager == null)
        {
            return;
        }

        accountManager.GetMonthSummary(GetMonthKey(), out long incomeFen, out long expenseFen);

        if (txtIncome != null)
        {
            txtIncome.text = MoneyText.FormatYuan(incomeFen);
        }

        if (txtExpense != null)
        {
            txtExpense.text = MoneyText.FormatYuan(expenseFen);
        }

        RebuildList(accountManager.GetRecordsByMonth(GetMonthKey()));
    }

    /// <summary>
    /// 清空并按日分组重建列表：日期降序（最近在上），同日按 CreatedAt 降序（补记账排组内最上，属预期）。
    /// 组头 = "MM月dd日 星期X" + "支出: 28.8"（只统计当日支出）；组内每条一行。
    /// </summary>
    private void RebuildList(List<AccountRecord> records)
    {
        if (listContent == null)
        {
            return;
        }

        // DestroyImmediate 而非 Destroy：OnDataChanged 可能同帧连发（如自检连加多笔），
        // Destroy 要到帧末才移除子项，同帧内 childCount 会包含幽灵旧行导致重复重建（步骤03 §4 坑位）
        for (int i = listContent.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(listContent.GetChild(i).gameObject);
        }

        records.Sort(CompareRecordForList);

        int index = 0;

        while (index < records.Count)
        {
            int groupEnd = index + 1;
            string dayKey = records[index].Date;

            while (groupEnd < records.Count && records[groupEnd].Date == dayKey)
            {
                groupEnd++;
            }

            long dayExpenseFen = 0;

            for (int i = index; i < groupEnd; i++)
            {
                if (records[i].Type == (int)RecordType.Expense)
                {
                    dayExpenseFen += records[i].AmountFen;
                }
            }

            if (dayHeaderPrefab != null)
            {
                DayHeaderUI header = Instantiate(dayHeaderPrefab, listContent);
                header.Setup(FormatGroupDate(dayKey), "支出: " + MoneyText.FormatTrim(dayExpenseFen));
            }

            for (int i = index; i < groupEnd; i++)
            {
                if (recordItemPrefab == null)
                {
                    continue;
                }

                RecordItemUI item = Instantiate(recordItemPrefab, listContent);
                item.Setup(records[i], GetIconSprite(records[i].Category));
                item.Clicked += OnItemClicked;
                item.LongPressed += OnItemLongPressed;
            }

            index = groupEnd;
        }

        if (emptyLabel != null)
        {
            emptyLabel.SetActive(records.Count == 0);
        }
    }

    /// <summary>
    /// 列表排序：Date 降序（yyyy-MM-dd 字典序即时间序），同日 CreatedAt 降序
    /// </summary>
    private static int CompareRecordForList(AccountRecord a, AccountRecord b)
    {
        int byDate = string.CompareOrdinal(b.Date, a.Date);

        if (byDate != 0)
        {
            return byDate;
        }

        return string.CompareOrdinal(b.CreatedAt, a.CreatedAt);
    }

    /// <summary>
    /// 组头日期："09月26日 星期六"（固定中文星期数组）；解析失败原样返回
    /// </summary>
    private static string FormatGroupDate(string yyyyMMdd)
    {
        DateTime date;

        if (DateTime.TryParseExact(yyyyMMdd ?? "", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out date))
        {
            return date.ToString("MM月dd日") + " " + WeekdayNames[(int)date.DayOfWeek];
        }

        return yyyyMMdd ?? "";
    }

    /// <summary>
    /// 分类名 → 图标 sprite（CategoryTable 反查 IconName），找不到返回 null
    /// </summary>
    private Sprite GetIconSprite(string categoryName)
    {
        if (iconProvider == null || string.IsNullOrEmpty(categoryName))
        {
            return null;
        }

        CategoryTable.CategoryDef def = CategoryTable.GetByName(categoryName);
        return def != null ? iconProvider.GetSprite(def.IconName) : null;
    }

    // ---------- 行交互 ----------

    /// <summary>
    /// 点击记录行 → 编辑模式打开记账页（含日期在内全字段回填）
    /// </summary>
    private void OnItemClicked(AccountRecord record)
    {
        if (record == null)
        {
            return;
        }

        if (recordPanelUI != null)
        {
            recordPanelUI.SetupForEdit(record);
        }

        if (uiManager != null)
        {
            uiManager.OpenRecordPanel();
        }
    }

    /// <summary>
    /// 长按记录行 → 删除确认弹窗，确认后按 Id 删除（OnDataChanged 自动刷新列表）
    /// </summary>
    private void OnItemLongPressed(AccountRecord record)
    {
        if (record == null || confirmDialog == null)
        {
            return;
        }

        string message = "删除这笔" + record.Category + "记录？\n删除后无法恢复。";
        string recordId = record.Id;
        confirmDialog.Show(message, () =>
        {
            if (accountManager != null)
            {
                accountManager.DeleteRecord(recordId);
            }
        });
    }
}
