using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 预算子页控制器（步骤09，图四~图六）：月预算 / 年预算两档（顶栏标题下拉切换）。
/// 未设总预算 = 居中"暂无预算"空态 + "＋ 设置预算"入口（图四/图五背景）；
/// 已设 = 总预算卡（标题+编辑、剩余圆环、剩余/预算/支出三行，图六）；
/// 月档另有分类预算区：无分类预算显示"未设置分类预算"空态，有则逐行圆环+支出/剩余，
/// 底栏"+ 添加分类预算"常驻（月档）。年档不设分类预算。
/// 刷新链：OnEnable + OnDataChanged/OnBudgetChanged 订阅；分类行全清全建（DestroyImmediate）。
/// </summary>
public class BudgetPanelUI : MonoBehaviour
{
    private static readonly Color ColBlack = new Color32(0x22, 0x22, 0x22, 0xFF);
    private static readonly Color ColGray = new Color32(0x66, 0x66, 0x66, 0xFF);
    private static readonly Color ColLightGray = new Color32(0x99, 0x99, 0x99, 0xFF);
    private static readonly Color ColWhite = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Color ColDivider = new Color32(0xEE, 0xEE, 0xEE, 0xFF);
    private static readonly Color ColOver = new Color32(0xE5, 0x4D, 0x42, 0xFF);

    [Header("Dependencies")]
    [SerializeField] private AccountManager accountManager;
    [SerializeField] private BudgetManager budgetManager;
    [SerializeField] private UIManager uiManager;
    [SerializeField] private BudgetDialogUI budgetDialog;
    [SerializeField] private TMP_FontAsset labelFont;   // 运行时动态建的分类行文字用

    [Header("Top Bar")]
    [SerializeField] private Button btnBack;
    [SerializeField] private Button btnModeTitle;
    [SerializeField] private TextMeshProUGUI txtModeTitle;   // "月预算 ▼" / "年预算 ▼"
    [SerializeField] private GameObject modeBlocker;
    [SerializeField] private GameObject modePopup;
    [SerializeField] private Button btnPickMonth;
    [SerializeField] private Button btnPickYear;

    [Header("Total Card")]
    [SerializeField] private GameObject totalCard;
    [SerializeField] private TextMeshProUGUI txtCardTitle;   // "10月总预算" / "2026年总预算"
    [SerializeField] private Button btnEdit;
    [SerializeField] private RingGraphic ringTrack;
    [SerializeField] private RingGraphic ringFill;
    [SerializeField] private TextMeshProUGUI txtRingRemain;
    [SerializeField] private TextMeshProUGUI txtRingPercent;
    [SerializeField] private TextMeshProUGUI lbRemain;       // "剩余预算:"
    [SerializeField] private TextMeshProUGUI lbBudget;       // "本月预算:" / "本年预算:"
    [SerializeField] private TextMeshProUGUI lbSpent;        // "本月支出:" / "本年支出:"
    [SerializeField] private TextMeshProUGUI txtRemain;
    [SerializeField] private TextMeshProUGUI txtBudget;
    [SerializeField] private TextMeshProUGUI txtSpent;

    [Header("Empty State")]
    [SerializeField] private GameObject emptyState;          // 暂无预算 图标+文案
    [SerializeField] private Button btnEmptySet;

    [Header("Category Section (月档)")]
    [SerializeField] private GameObject categoryArea;        // 滚动区（含空态与行）
    [SerializeField] private GameObject categoryEmpty;       // 未设置分类预算
    [SerializeField] private RectTransform categoryContent;  // 行容器
    [SerializeField] private GameObject bottomAddBar;
    [SerializeField] private Button btnAddCategory;

    private bool yearMode = false;
    private bool modePopupOpen = false;

    /// <summary>
    /// 当前行数（自检用；只数激活行，空态标签不计）
    /// </summary>
    public int CategoryRowCount
    {
        get
        {
            if (categoryContent == null)
            {
                return 0;
            }

            int count = 0;

            for (int i = 0; i < categoryContent.childCount; i++)
            {
                if (categoryContent.GetChild(i).gameObject.activeSelf)
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

        if (accountManager != null)
        {
            accountManager.OnDataChanged -= RefreshAll;
            accountManager.OnDataChanged += RefreshAll;
        }

        if (budgetManager != null)
        {
            budgetManager.OnBudgetChanged -= RefreshAll;
            budgetManager.OnBudgetChanged += RefreshAll;
        }

        yearMode = false;
        CloseModePopup();
        RefreshAll();
    }

    private void OnDisable()
    {
        if (accountManager != null)
        {
            accountManager.OnDataChanged -= RefreshAll;
        }

        if (budgetManager != null)
        {
            budgetManager.OnBudgetChanged -= RefreshAll;
        }

        CloseModePopup();
    }

    /// <summary>
    /// 外部引用为空时自动查找
    /// </summary>
    private void TryInitializeDependencies()
    {
        if (accountManager == null)
        {
            accountManager = FindFirstObjectByType<AccountManager>();
        }

        if (budgetManager == null)
        {
            budgetManager = FindFirstObjectByType<BudgetManager>(FindObjectsInactive.Include);
        }

        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>();
        }

        if (budgetDialog == null)
        {
            budgetDialog = FindFirstObjectByType<BudgetDialogUI>(FindObjectsInactive.Include);
        }
    }

    /// <summary>
    /// 事件绑定（幂等，可重复调用）
    /// </summary>
    private void RegisterEvents()
    {
        RegisterButton(btnBack, OnBackClicked);
        RegisterButton(btnModeTitle, OnModeTitleClicked);
        RegisterButton(btnPickMonth, () => SetYearMode(false));
        RegisterButton(btnPickYear, () => SetYearMode(true));
        RegisterButton(btnEmptySet, OnEmptySetClicked);
        RegisterButton(btnEdit, OnEditClicked);
        RegisterButton(btnAddCategory, OnAddCategoryClicked);
        RegisterButton(modeBlocker != null ? modeBlocker.GetComponent<Button>() : null, CloseModePopup);
    }

    private static void RegisterButton(Button button, Action action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(SfxManager.PlayClick);
        button.onClick.AddListener(new UnityEngine.Events.UnityAction(action));
    }

    // ---------- 交互 ----------

    private void OnBackClicked()
    {
        if (uiManager != null)
        {
            uiManager.OpenDiscoverPanel();
        }
    }

    private void OnModeTitleClicked()
    {
        if (modePopupOpen)
        {
            CloseModePopup();
            return;
        }

        modePopupOpen = true;

        if (modeBlocker != null)
        {
            modeBlocker.SetActive(true);
        }

        if (modePopup != null)
        {
            modePopup.SetActive(true);
        }
    }

    private void CloseModePopup()
    {
        modePopupOpen = false;

        if (modeBlocker != null)
        {
            modeBlocker.SetActive(false);
        }

        if (modePopup != null)
        {
            modePopup.SetActive(false);
        }
    }

    private void SetYearMode(bool toYear)
    {
        CloseModePopup();

        if (yearMode == toYear)
        {
            RefreshAll();
            return;
        }

        yearMode = toYear;
        RefreshAll();
    }

    /// <summary>
    /// 空态/编辑共用入口：开当月（当年）总预算弹窗
    /// </summary>
    private void OnEmptySetClicked()
    {
        OpenTotalBudgetDialog();
    }

    private void OnEditClicked()
    {
        OpenTotalBudgetDialog();
    }

    private void OnAddCategoryClicked()
    {
        if (budgetDialog == null)
        {
            Debug.LogWarning("[BudgetPanelUI] 预算弹窗引用缺失（请重跑搭建菜单）。");
            return;
        }

        budgetDialog.ShowCategoryAdd(GetMonthKey());
    }

    private void OpenTotalBudgetDialog()
    {
        if (budgetDialog == null)
        {
            Debug.LogWarning("[BudgetPanelUI] 预算弹窗引用缺失（请重跑搭建菜单）。");
            return;
        }

        if (yearMode)
        {
            string year = DateTime.Now.Year.ToString("D4");
            budgetDialog.ShowYearTotal(year, budgetManager != null ? budgetManager.GetYearBudget(year) : 0);
        }
        else
        {
            string monthKey = GetMonthKey();
            budgetDialog.ShowMonthTotal(monthKey, budgetManager != null ? budgetManager.GetMonthBudget(monthKey) : 0);
        }
    }

    private static string GetMonthKey()
    {
        return DateTime.Now.ToString("yyyy-MM");
    }

    // ---------- 刷新 ----------

    private void RefreshAll()
    {
        CloseModePopup();

        if (txtModeTitle != null)
        {
            txtModeTitle.text = yearMode ? "年预算 ▼" : "月预算 ▼";
        }

        DateTime now = DateTime.Now;
        long budgetFen;
        long spentFen;
        string title;

        if (yearMode)
        {
            string year = now.Year.ToString("D4");
            budgetFen = budgetManager != null ? budgetManager.GetYearBudget(year) : 0;
            spentFen = GetRangeSpent(
                new DateTime(now.Year, 1, 1).ToString("yyyy-MM-dd"),
                new DateTime(now.Year, 12, 31).ToString("yyyy-MM-dd"));
            title = now.Year + "年总预算";
        }
        else
        {
            string monthKey = GetMonthKey();
            budgetFen = budgetManager != null ? budgetManager.GetMonthBudget(monthKey) : 0;
            spentFen = GetRangeSpent(
                new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd"),
                new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month)).ToString("yyyy-MM-dd"));
            title = now.Month + "月总预算";
        }

        bool hasBudget = budgetFen > 0;

        if (txtCardTitle != null)
        {
            txtCardTitle.text = title;
        }

        if (lbBudget != null)
        {
            lbBudget.text = yearMode ? "本年预算:" : "本月预算:";
        }

        if (lbSpent != null)
        {
            lbSpent.text = yearMode ? "本年支出:" : "本月支出:";
        }

        if (totalCard != null)
        {
            totalCard.SetActive(hasBudget);
        }

        if (emptyState != null)
        {
            emptyState.SetActive(!hasBudget);
        }

        if (categoryArea != null)
        {
            categoryArea.SetActive(!yearMode && hasBudget);
        }

        if (bottomAddBar != null)
        {
            bottomAddBar.SetActive(!yearMode && hasBudget);
        }

        if (hasBudget)
        {
            long remainFen = budgetFen - spentFen;
            float percent = Mathf.Clamp01((float)((double)remainFen / budgetFen));

            if (ringFill != null)
            {
                ringFill.gameObject.SetActive(true);
                ringFill.Progress = percent;
            }

            if (txtRingPercent != null)
            {
                txtRingPercent.text = Mathf.RoundToInt(percent * 100f) + "%";
                txtRingPercent.color = ColBlack;
            }

            if (txtRingRemain != null)
            {
                txtRingRemain.color = ColGray;
            }

            if (txtRemain != null)
            {
                txtRemain.text = MoneyText.FormatYuan(remainFen);
                txtRemain.color = remainFen < 0 ? ColOver : ColBlack;
            }

            if (txtBudget != null)
            {
                txtBudget.text = MoneyText.FormatYuan(budgetFen);
            }

            if (txtSpent != null)
            {
                txtSpent.text = MoneyText.FormatYuan(spentFen);
            }
        }

        if (!yearMode && hasBudget)
        {
            RebuildCategoryRows();
        }
    }

    /// <summary>
    /// 区间支出合计（yyyy-MM-dd 含首尾）
    /// </summary>
    private long GetRangeSpent(string start, string end)
    {
        if (accountManager == null)
        {
            return 0;
        }

        List<AccountRecord> records = accountManager.GetRecordsInRange(start, end);
        long spent = 0;

        for (int i = 0; i < records.Count; i++)
        {
            AccountRecord record = records[i];

            if (record != null && record.Type == (int)RecordType.Expense)
            {
                spent += record.AmountFen;
            }
        }

        return spent;
    }

    /// <summary>
    /// 分类预算行全清全建：圆环(已用比例) + 分类名 + 右侧 支出/剩余 两行；点击行进编辑弹窗
    /// </summary>
    private void RebuildCategoryRows()
    {
        if (categoryContent == null || categoryEmpty == null)
        {
            return;
        }

        for (int i = categoryContent.childCount - 1; i >= 0; i--)
        {
            Transform child = categoryContent.GetChild(i);

            // 空态标签是常驻子项（非布局行），只切显隐不销毁
            if (child.name == "CategoryEmpty")
            {
                continue;
            }

            DestroyImmediate(child.gameObject);
        }

        if (accountManager == null || budgetManager == null)
        {
            return;
        }

        string monthKey = GetMonthKey();
        List<CategoryBudgetEntry> entries = budgetManager.GetCategoryBudgets(monthKey);

        if (categoryEmpty != null)
        {
            categoryEmpty.SetActive(entries.Count == 0);
        }

        Debug.Log($"[BudgetPanelUI] RebuildCategoryRows monthKey={monthKey} entries={entries.Count}");

        // 当月各分类支出
        Dictionary<string, long> spentByCategory = new Dictionary<string, long>();
        List<AccountRecord> records = accountManager.GetRecordsByMonth(monthKey);

        for (int i = 0; i < records.Count; i++)
        {
            AccountRecord record = records[i];

            if (record == null || record.Type != (int)RecordType.Expense)
            {
                continue;
            }

            if (!spentByCategory.ContainsKey(record.Category))
            {
                spentByCategory[record.Category] = 0;
            }

            spentByCategory[record.Category] += record.AmountFen;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            CategoryBudgetEntry entry = entries[i];

            if (entry == null || string.IsNullOrEmpty(entry.Category))
            {
                continue;
            }

            long spent = spentByCategory.ContainsKey(entry.Category) ? spentByCategory[entry.Category] : 0;
            CreateCategoryRow(entry.Category, entry.AmountFen, spent);
        }
    }

    /// <summary>
    /// 单行：白底 + 小圆环(左) + 分类名 + 右侧两行（本月支出 x / 剩余 x，超支红色），点击编辑
    /// </summary>
    private void CreateCategoryRow(string category, long budgetFen, long spentFen)
    {
        if (categoryContent == null)
        {
            return;
        }

        GameObject row = new GameObject("Row_" + category, typeof(RectTransform));
        row.transform.SetParent(categoryContent, false);
        RectTransform rowRect = (RectTransform)row.transform;
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);

        Image bg = row.AddComponent<Image>();
        bg.color = ColWhite;
        bg.raycastTarget = true;

        row.AddComponent<Button>();
        row.AddComponent<LayoutElement>().preferredHeight = 150f;

        // 底部分隔线
        GameObject divider = new GameObject("Divider", typeof(RectTransform));
        divider.transform.SetParent(row.transform, false);
        RectTransform dividerRect = (RectTransform)divider.transform;
        dividerRect.anchorMin = new Vector2(0f, 0f);
        dividerRect.anchorMax = new Vector2(1f, 0f);
        dividerRect.pivot = new Vector2(0.5f, 0f);
        dividerRect.sizeDelta = new Vector2(0f, 2f);
        Image dividerImage = divider.AddComponent<Image>();
        dividerImage.color = ColDivider;
        dividerImage.raycastTarget = false;

        // 小圆环（已用比例）
        RectTransform ringRoot = CreateChildRect(row.transform, "Ring", new Vector2(0.04f, 0.5f),
            new Vector2(0.04f, 0.5f), new Vector2(0f, 0.5f));
        ringRoot.sizeDelta = new Vector2(96f, 96f);

        AddRing(ringRoot, ColDivider, 10f, 1f);
        long usedClamped = Math.Min(spentFen, budgetFen);
        float usedPercent = budgetFen > 0 ? (float)((double)usedClamped / budgetFen) : 0f;
        AddRing(ringRoot, ColBlack, 10f, usedPercent);

        TextMeshProUGUI ringText = AddTmp(ringRoot, "Percent", Mathf.RoundToInt(usedPercent * 100f) + "%",
            24f, ColGray, TextAlignmentOptions.Center);
        Stretch(ringText.rectTransform);

        // 分类名
        TextMeshProUGUI name = AddTmp(row.transform, "Name", category, 38f, ColBlack, TextAlignmentOptions.Left);
        name.rectTransform.anchorMin = new Vector2(0.22f, 0f);
        name.rectTransform.anchorMax = new Vector2(0.55f, 1f);
        name.rectTransform.offsetMin = Vector2.zero;
        name.rectTransform.offsetMax = Vector2.zero;

        // 右侧两行：支出 / 剩余
        TextMeshProUGUI spentLabel = AddTmp(row.transform, "Spent", "本月支出 " + MoneyText.FormatTrim(spentFen),
            28f, ColLightGray, TextAlignmentOptions.Right);
        spentLabel.rectTransform.anchorMin = new Vector2(0.55f, 0.5f);
        spentLabel.rectTransform.anchorMax = new Vector2(0.95f, 1f);
        spentLabel.rectTransform.offsetMin = Vector2.zero;
        spentLabel.rectTransform.offsetMax = Vector2.zero;

        long remain = budgetFen - spentFen;
        TextMeshProUGUI remainLabel = AddTmp(row.transform, "Remain", "剩余 " + MoneyText.FormatYuan(remain),
            32f, remain < 0 ? ColOver : ColBlack, TextAlignmentOptions.Right);
        remainLabel.rectTransform.anchorMin = new Vector2(0.55f, 0f);
        remainLabel.rectTransform.anchorMax = new Vector2(0.95f, 0.5f);
        remainLabel.rectTransform.offsetMin = Vector2.zero;
        remainLabel.rectTransform.offsetMax = Vector2.zero;

        string capturedCategory = category;
        Button button = row.GetComponent<Button>();
        button.onClick.AddListener(SfxManager.PlayClick);
        button.onClick.AddListener(() =>
        {
            if (budgetDialog != null)
            {
                budgetDialog.ShowCategoryEdit(GetMonthKey(), capturedCategory,
                    budgetManager != null ? budgetManager.GetCategoryBudget(GetMonthKey(), capturedCategory) : 0);
            }
        });
    }

    // ---------- UI 工具 ----------

    private static RectTransform CreateChildRect(Transform parent, string name, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 pivot)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static RingGraphic AddRing(Transform parent, Color color, float thickness, float progress)
    {
        GameObject go = new GameObject("Ring", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>());

        RingGraphic ring = go.AddComponent<RingGraphic>();
        ring.color = color;
        ring.Thickness = thickness;
        ring.Progress = progress;
        ring.raycastTarget = false;
        return ring;
    }

    private TextMeshProUGUI AddTmp(Transform parent, string name, string text, float fontSize,
        Color color, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = labelFont;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
