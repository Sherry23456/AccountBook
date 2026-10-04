using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 账单子页控制器（步骤09，图二/图三）：月账单 / 年账单两态。
/// 月账单 = 年份下拉（默认当前年）+ 黄卡年结余/年收入/年支出 + 该年有流水的月份行
/// （月收入/月支出/月结余，降序，行尾＞，点击跳明细页该月）；年账单 = 全期总结余/总收入/总支出
/// + 有流水的年份行 + "年账单为自然年（1.1-12.31）"脚注。
/// 行与年份选项全清全建（DestroyImmediate，步骤03 §4 同坑规避），聚合基于一次 GetAllRecords。
/// </summary>
public class BillPanelUI : MonoBehaviour
{
    private static readonly Color ColBlack = new Color32(0x22, 0x22, 0x22, 0xFF);
    private static readonly Color ColGray = new Color32(0x99, 0x99, 0x99, 0xFF);
    private static readonly Color ColWhite = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Color ColDivider = new Color32(0xEE, 0xEE, 0xEE, 0xFF);
    private static readonly Color ColSelectedBg = new Color32(0x22, 0x22, 0x22, 0xFF);

    [Header("Dependencies")]
    [SerializeField] private AccountManager accountManager;
    [SerializeField] private UIManager uiManager;
    [SerializeField] private DetailPanelUI detailPanelUI;
    [SerializeField] private TMP_FontAsset labelFont;   // 运行时动态建的行/选项文字用

    [Header("Top Bar")]
    [SerializeField] private Button btnBack;

    [Header("Mode Toggle")]
    [SerializeField] private Button btnMonth;
    [SerializeField] private Image imgMonthBg;
    [SerializeField] private TextMeshProUGUI lbMonth;
    [SerializeField] private Button btnYear;
    [SerializeField] private Image imgYearBg;
    [SerializeField] private TextMeshProUGUI lbYear;

    [Header("Year Picker (月账单档)")]
    [SerializeField] private Button btnYearPicker;
    [SerializeField] private TextMeshProUGUI txtYearLabel;
    [SerializeField] private GameObject yearPickerBlocker;
    [SerializeField] private GameObject yearPickerPopup;
    [SerializeField] private RectTransform yearPickerContent;

    [Header("Summary Card")]
    [SerializeField] private TextMeshProUGUI txtCaption;   // 年结余 / 总结余
    [SerializeField] private TextMeshProUGUI txtValue;     // 大数字
    [SerializeField] private TextMeshProUGUI txtIncome;    // 年收入 839.00 / 总收入 1228.28
    [SerializeField] private TextMeshProUGUI txtExpense;   // 年支出 10173.29 / 总支出 23279.16

    [Header("List")]
    [SerializeField] private TextMeshProUGUI[] headerLabels;  // 月份/月收入/月支出/月结余（年档为 年份/年收入/...）
    [SerializeField] private RectTransform listContent;
    [SerializeField] private GameObject emptyLabel;
    [SerializeField] private GameObject footerNote;

    private bool yearMode = false;
    private int selectedYear;
    private bool yearPickerOpen = false;

    /// <summary>
    /// 列表容器（自检定位/取首行用）
    /// </summary>
    public RectTransform ListContent
    {
        get { return listContent; }
    }

    /// <summary>
    /// 当前行数（自检用；只数激活行，空态标签不计）
    /// </summary>
    public int RowCount
    {
        get
        {
            if (listContent == null)
            {
                return 0;
            }

            int count = 0;

            for (int i = 0; i < listContent.childCount; i++)
            {
                if (listContent.GetChild(i).gameObject.activeSelf)
                {
                    count++;
                }
            }

            return count;
        }
    }

    private void OnEnable()
    {
        TryInitializeDependencies();
        RegisterEvents();

        // 每次进入复位到 月账单/当前年（可预测态）
        yearMode = false;
        selectedYear = DateTime.Now.Year;
        CloseYearPicker();
        RefreshAll();
    }

    private void OnDisable()
    {
        CloseYearPicker();
    }

    /// <summary>
    /// 外部引用为空时自动查找（明细页初始未激活需包含未激活对象）
    /// </summary>
    private void TryInitializeDependencies()
    {
        if (accountManager == null)
        {
            accountManager = FindFirstObjectByType<AccountManager>();
        }

        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>();
        }

        if (detailPanelUI == null)
        {
            detailPanelUI = FindFirstObjectByType<DetailPanelUI>(FindObjectsInactive.Include);
        }
    }

    /// <summary>
    /// 事件绑定（幂等，可重复调用）
    /// </summary>
    private void RegisterEvents()
    {
        RegisterButton(btnBack, OnBackClicked);
        RegisterButton(btnMonth, OnMonthModeClicked);
        RegisterButton(btnYear, OnYearModeClicked);
        RegisterButton(btnYearPicker, OnYearPickerClicked);
        RegisterButton(yearPickerBlocker != null ? yearPickerBlocker.GetComponent<Button>() : null, CloseYearPicker);
    }

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

    // ---------- 交互 ----------

    private void OnBackClicked()
    {
        if (uiManager != null)
        {
            uiManager.OpenDiscoverPanel();
        }
    }

    private void OnMonthModeClicked()
    {
        if (!yearMode)
        {
            UpdateToggleVisuals();
            return;
        }

        yearMode = false;
        RefreshAll();
    }

    private void OnYearModeClicked()
    {
        if (yearMode)
        {
            UpdateToggleVisuals();
            return;
        }

        yearMode = true;
        CloseYearPicker();
        RefreshAll();
    }

    private void OnYearPickerClicked()
    {
        if (yearPickerOpen)
        {
            CloseYearPicker();
            return;
        }

        RebuildYearOptions();
        yearPickerOpen = true;

        if (yearPickerBlocker != null)
        {
            yearPickerBlocker.SetActive(true);
        }

        if (yearPickerPopup != null)
        {
            yearPickerPopup.SetActive(true);
        }
    }

    private void CloseYearPicker()
    {
        yearPickerOpen = false;

        if (yearPickerBlocker != null)
        {
            yearPickerBlocker.SetActive(false);
        }

        if (yearPickerPopup != null)
        {
            yearPickerPopup.SetActive(false);
        }
    }

    /// <summary>
    /// 年份选项 = 有流水的年份 ∪ 当前年，降序全清全建
    /// </summary>
    private void RebuildYearOptions()
    {
        if (yearPickerContent == null)
        {
            return;
        }

        for (int i = yearPickerContent.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(yearPickerContent.GetChild(i).gameObject);
        }

        if (accountManager == null)
        {
            return;
        }

        HashSet<int> years = new HashSet<int>
        {
            DateTime.Now.Year
        };

        List<AccountRecord> records = accountManager.GetAllRecords();

        for (int i = 0; i < records.Count; i++)
        {
            AccountRecord record = records[i];

            if (record == null || string.IsNullOrWhiteSpace(record.Date) || record.Date.Length < 4)
            {
                continue;
            }

            int year;

            if (int.TryParse(record.Date.Substring(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out year))
            {
                years.Add(year);
            }
        }

        List<int> sorted = new List<int>(years);
        sorted.Sort((a, b) => b.CompareTo(a));

        RectTransform popupRect = yearPickerPopup != null ? yearPickerPopup.GetComponent<RectTransform>() : null;

        if (popupRect != null)
        {
            popupRect.sizeDelta = new Vector2(popupRect.sizeDelta.x, 12f + sorted.Count * 110f);
        }

        for (int i = 0; i < sorted.Count; i++)
        {
            int capturedYear = sorted[i];
            GameObject option = CreateTextRow(yearPickerContent, "YearOption_" + capturedYear, 110f);
            TextMeshProUGUI label = AddColumnText(option.transform, "Text", capturedYear + " 年", 0.12f, 0.76f,
                38f, capturedYear == selectedYear ? ColBlack : ColGray, TextAlignmentOptions.Left);

            Button button = option.GetComponent<Button>();
            button.onClick.AddListener(SfxManager.PlayClick);
            button.onClick.AddListener(() =>
            {
                selectedYear = capturedYear;
                CloseYearPicker();
                RefreshAll();
            });

            if (capturedYear == selectedYear && label != null)
            {
                // 当前年加对勾感（左对齐黑字 + 右侧圆点省略，参考件无图省略为高亮即可）
                label.fontStyle = FontStyles.Bold;
            }
        }
    }

    // ---------- 刷新 ----------

    private void RefreshAll()
    {
        UpdateToggleVisuals();
        CloseYearPicker();

        // 年账单档无年份选择（图三），仅月账单档显示
        if (btnYearPicker != null)
        {
            btnYearPicker.gameObject.SetActive(!yearMode);
        }

        if (txtYearLabel != null)
        {
            txtYearLabel.text = selectedYear + " 年 ▼";
        }

        bool hasAccount = accountManager != null;
        List<AccountRecord> records = hasAccount ? accountManager.GetAllRecords() : new List<AccountRecord>();

        // 年月聚合：字典 monthKey → (income, expense)
        Dictionary<int, long[]> monthTotals = new Dictionary<int, long[]>();
        Dictionary<int, long[]> yearTotals = new Dictionary<int, long[]>();

        for (int i = 0; i < records.Count; i++)
        {
            AccountRecord record = records[i];

            if (record == null || string.IsNullOrWhiteSpace(record.Date) || record.Date.Length < 7)
            {
                continue;
            }

            int year;
            int month;

            if (!int.TryParse(record.Date.Substring(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out year) ||
                !int.TryParse(record.Date.Substring(5, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out month))
            {
                continue;
            }

            long amount = record.Type == (int)RecordType.Income ? record.AmountFen : -record.AmountFen;

            if (!yearTotals.ContainsKey(year))
            {
                yearTotals[year] = new long[2];
            }

            if (amount >= 0)
            {
                yearTotals[year][0] += amount;
            }
            else
            {
                yearTotals[year][1] -= amount;
            }

            if (!yearMode && year == selectedYear)
            {
                if (!monthTotals.ContainsKey(month))
                {
                    monthTotals[month] = new long[2];
                }

                if (amount >= 0)
                {
                    monthTotals[month][0] += amount;
                }
                else
                {
                    monthTotals[month][1] -= amount;
                }
            }
        }

        // —— 汇总黄卡 ——
        long totalIncome = 0;
        long totalExpense = 0;

        if (yearMode)
        {
            foreach (KeyValuePair<int, long[]> kv in yearTotals)
            {
                totalIncome += kv.Value[0];
                totalExpense += kv.Value[1];
            }
        }
        else if (yearTotals.ContainsKey(selectedYear))
        {
            totalIncome = yearTotals[selectedYear][0];
            totalExpense = yearTotals[selectedYear][1];
        }

        if (txtCaption != null)
        {
            txtCaption.text = yearMode ? "总结余" : "年结余";
        }

        if (txtValue != null)
        {
            txtValue.text = MoneyText.FormatYuan(totalIncome - totalExpense);
        }

        if (txtIncome != null)
        {
            txtIncome.text = (yearMode ? "总收入 " : "年收入 ") + MoneyText.FormatYuan(totalIncome);
        }

        if (txtExpense != null)
        {
            txtExpense.text = (yearMode ? "总支出 " : "年支出 ") + MoneyText.FormatYuan(totalExpense);
        }

        // —— 表头 ——
        if (headerLabels != null && headerLabels.Length == 4)
        {
            headerLabels[0].text = yearMode ? "年份" : "月份";
            headerLabels[1].text = yearMode ? "年收入" : "月收入";
            headerLabels[2].text = yearMode ? "年支出" : "月支出";
            headerLabels[3].text = yearMode ? "年结余" : "月结余";
        }

        // —— 行区 ——
        if (listContent != null)
        {
            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(listContent.GetChild(i).gameObject);
            }
        }

        bool showFooter = yearMode;

        if (footerNote != null)
        {
            footerNote.SetActive(showFooter);
        }

        if (!hasAccount)
        {
            if (emptyLabel != null)
            {
                emptyLabel.SetActive(true);
            }

            return;
        }

        if (yearMode)
        {
            List<int> years = new List<int>(yearTotals.Keys);
            years.Sort((a, b) => b.CompareTo(a));

            for (int i = 0; i < years.Count; i++)
            {
                int year = years[i];
                long income = yearTotals[year][0];
                long expense = yearTotals[year][1];
                CreateDataRow(year + "年", MoneyText.FormatYuan(income),
                    MoneyText.FormatYuan(expense), MoneyText.FormatYuan(income - expense), null);
            }

            if (emptyLabel != null)
            {
                emptyLabel.SetActive(years.Count == 0);
            }
        }
        else
        {
            List<int> months = new List<int>(monthTotals.Keys);
            months.Sort((a, b) => b.CompareTo(a));

            for (int i = 0; i < months.Count; i++)
            {
                int month = months[i];
                long income = monthTotals[month][0];
                long expense = monthTotals[month][1];
                CreateDataRow(month + "月", MoneyText.FormatYuan(income),
                    MoneyText.FormatYuan(expense), MoneyText.FormatYuan(income - expense), month);
            }

            if (emptyLabel != null)
            {
                emptyLabel.SetActive(months.Count == 0);
            }
        }
    }

    /// <summary>
    /// 档位切换视觉：选中黑底白字，未选中白底黑字（图二/图三分段控件）
    /// </summary>
    private void UpdateToggleVisuals()
    {
        if (imgMonthBg != null)
        {
            imgMonthBg.color = yearMode ? ColWhite : ColSelectedBg;
        }

        if (lbMonth != null)
        {
            lbMonth.color = yearMode ? ColBlack : ColWhite;
        }

        if (imgYearBg != null)
        {
            imgYearBg.color = yearMode ? ColSelectedBg : ColWhite;
        }

        if (lbYear != null)
        {
            lbYear.color = yearMode ? ColWhite : ColBlack;
        }
    }

    // ---------- 行构建 ----------

    /// <summary>
    /// 数据行：白底按钮 + 四列文字 + 行尾＞（月档），底部 2px 分隔线。
    /// month 传空 = 年档行（不可点，参考图三无箭头）。
    /// </summary>
    private void CreateDataRow(string col0, string col1, string col2, string col3, int? month)
    {
        if (listContent == null)
        {
            return;
        }

        GameObject row = CreateTextRow(listContent, "Row_" + col0, 150f);
        AddDivider(row.transform);
        AddColumnText(row.transform, "Col0", col0, 0.07f, 0.20f, 38f, ColBlack, TextAlignmentOptions.Left);
        AddColumnText(row.transform, "Col1", col1, 0.30f, 0.20f, 38f, ColBlack, TextAlignmentOptions.Left);
        AddColumnText(row.transform, "Col2", col2, 0.53f, 0.20f, 38f, ColBlack, TextAlignmentOptions.Left);
        AddColumnText(row.transform, "Col3", col3, 0.76f, 0.20f, 38f, ColBlack, TextAlignmentOptions.Left);

        if (month.HasValue)
        {
            AddColumnText(row.transform, "Chevron", "＞", 0.93f, 0.07f, 34f, ColGray, TextAlignmentOptions.Center);
            int capturedMonth = month.Value;
            Button button = row.GetComponent<Button>();
            button.onClick.AddListener(SfxManager.PlayClick);
            button.onClick.AddListener(() => OpenMonthInDetail(capturedMonth));
        }
    }

    /// <summary>
    /// 月账单行点击 → 明细页定位该月（先登记 pending 再开页面，OnEnable 消费）
    /// </summary>
    private void OpenMonthInDetail(int month)
    {
        if (detailPanelUI != null)
        {
            detailPanelUI.OpenMonth(selectedYear, month);
        }

        if (uiManager != null)
        {
            uiManager.OpenDetailPanel();
        }
    }

    /// <summary>
    /// 行基座：白底 + Button（透明可点）+ LayoutElement 固定行高（VerticalLayoutGroup 控高依据）。
    /// 供数据行/年份选项复用。
    /// </summary>
    private static GameObject CreateTextRow(Transform parent, string name, float height)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);

        Image bg = go.AddComponent<Image>();
        bg.color = ColWhite;
        bg.raycastTarget = true;

        go.AddComponent<Button>();
        go.AddComponent<LayoutElement>().preferredHeight = height;
        return go;
    }

    /// <summary>
    /// 行底 2px 分隔线（通栏，图二行间细线）
    /// </summary>
    private static void AddDivider(Transform row)
    {
        GameObject go = new GameObject("Divider", typeof(RectTransform));
        go.transform.SetParent(row, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(0f, 2f);

        Image image = go.AddComponent<Image>();
        image.color = ColDivider;
        image.raycastTarget = false;
    }

    /// <summary>
    /// 行内列文字：父宽比例区间拉伸（anchorMin.x=leftFrac，anchorMax.x=leftFrac+widthFrac），
    /// 与表头（Builder 侧）同约定，保证列对齐
    /// </summary>
    private TextMeshProUGUI AddColumnText(Transform parent, string name, string text,
        float leftFrac, float widthFrac, float fontSize, Color color, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(leftFrac, 0f);
        rect.anchorMax = new Vector2(Mathf.Min(1f, leftFrac + widthFrac), 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = labelFont;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.raycastTarget = false;
        return tmp;
    }
}
