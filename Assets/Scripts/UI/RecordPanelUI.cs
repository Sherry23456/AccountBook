using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 记账页控制器（步骤02）：支出/收入页签 + 分类宫格 + 金额键盘（支持 3+8=11 式连加）+ 备注 + 完成/取消。
/// 金额状态机全程 long（分）运算、显示用整数分拼字符串，杜绝 float 精度坑（步骤02 §2.3/§4）。
/// 新增：AppFlowManager → SetupForNew()；编辑：明细页（步骤03）→ SetupForEdit(record)。
/// </summary>
public class RecordPanelUI : MonoBehaviour
{
    /// <summary>
    /// 整数位上限 7 位（9 位数封顶）
    /// </summary>
    private const int MaxIntegerDigits = 7;

    /// <summary>
    /// 小数位上限 2 位
    /// </summary>
    private const int MaxDecimalDigits = 2;

    private static readonly Color ColYellow = new Color32(0xFF, 0xD1, 0x00, 0xFF);
    private static readonly Color ColBlack = new Color32(0x22, 0x22, 0x22, 0xFF);
    private static readonly Color ColWhite = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Color ColDoneDisabled = new Color32(0xDD, 0xDD, 0xDD, 0xFF);

    [Header("Dependencies")]
    [SerializeField] private AccountManager accountManager;
    [SerializeField] private CategoryIconProvider iconProvider;
    [SerializeField] private UIManager uiManager;

    [Header("Tabs")]
    [SerializeField] private Button tabExpenseButton;
    [SerializeField] private Button tabIncomeButton;

    [Header("Category Grid")]
    [SerializeField] private RectTransform gridContent;
    [SerializeField] private CategoryGridItem categoryItemPrefab;

    [Header("Amount & Note")]
    [SerializeField] private TextMeshProUGUI amountText;
    [SerializeField] private TMP_InputField noteInput;

    [Header("Keyboard")]
    [SerializeField] private Button[] digitButtons = new Button[10];
    [SerializeField] private Button dotButton;
    [SerializeField] private Button plusButton;
    [SerializeField] private Button minusButton;
    [SerializeField] private Button todayButton;
    [SerializeField] private Button backspaceButton;
    [SerializeField] private Button doneButton;
    [SerializeField] private Button closeButton;

    private readonly List<CategoryGridItem> gridItems = new List<CategoryGridItem>();

    private int currentType = (int)RecordType.Expense;   // 当前页签：0=支出，1=收入
    private long committedFen = 0;                       // 已用 + 并入的累计和
    private string currentInput = "";                    // 正在输入的数字串（含小数点）
    private char lastOperator = '+';                     // 待套用到 currentInput 的运算符
    private string selectedCategory = "";                // 当前选中分类名
    private AccountRecord editingRecord = null;          // 编辑模式目标记录（null = 新增）

    private Image tabExpenseImage;
    private Image tabIncomeImage;
    private TextMeshProUGUI tabExpenseLabel;
    private TextMeshProUGUI tabIncomeLabel;

    private void OnEnable()
    {
        TryInitializeDependencies();
        CacheTabVisuals();
        RegisterEvents();
        RefreshTabVisuals();
        RefreshAmountDisplay();
        RefreshDoneButton();
    }

    /// <summary>
    /// 外部引用为空时自动查找（QuikDelivery 模式）
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

        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>();
        }
    }

    /// <summary>
    /// 页签/键盘按钮事件绑定（幂等，可重复调用）
    /// </summary>
    private void RegisterEvents()
    {
        RegisterButton(tabExpenseButton, OnExpenseTabClicked);
        RegisterButton(tabIncomeButton, OnIncomeTabClicked);
        RegisterButton(dotButton, OnDotPressed);
        RegisterButton(plusButton, OnPlusPressed);
        RegisterButton(minusButton, OnMinusPressed);
        RegisterButton(todayButton, OnTodayClicked);
        RegisterButton(backspaceButton, OnBackspacePressed);
        RegisterButton(doneButton, OnDoneClicked);
        RegisterButton(closeButton, OnCancelClicked);

        if (digitButtons != null)
        {
            for (int i = 0; i < digitButtons.Length && i < 10; i++)
            {
                int digit = i;   // 闭包捕获循环变量
                RegisterButton(digitButtons[i], () => OnDigitPressed(digit));
            }
        }
    }

    /// <summary>
    /// 按钮通用绑定
    /// </summary>
    private static void RegisterButton(Button button, UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    /// <summary>
    /// 缓存页签底图与文字引用（子结构由搭建脚本固定）
    /// </summary>
    private void CacheTabVisuals()
    {
        if (tabExpenseButton != null)
        {
            tabExpenseImage = tabExpenseButton.image;
            tabExpenseLabel = tabExpenseButton.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (tabIncomeButton != null)
        {
            tabIncomeImage = tabIncomeButton.image;
            tabIncomeLabel = tabIncomeButton.GetComponentInChildren<TextMeshProUGUI>();
        }
    }

    // ---------- 对外入口 ----------

    /// <summary>
    /// 新增模式进入记账页：复位全部输入态，默认支出页签（底栏记账按钮调用）
    /// </summary>
    public void SetupForNew()
    {
        editingRecord = null;
        ClearInputState();

        if (noteInput != null)
        {
            noteInput.text = "";
        }

        SetType((int)RecordType.Expense);
    }

    /// <summary>
    /// 编辑模式进入记账页（步骤03 明细页调用）：回填页签/分类/金额/备注，完成时走 UpdateRecord 而非 Add
    /// </summary>
    public void SetupForEdit(AccountRecord record)
    {
        if (record == null)
        {
            SetupForNew();
            return;
        }

        editingRecord = record;
        committedFen = 0;
        lastOperator = '+';
        currentInput = FormatFen(record.AmountFen);

        if (noteInput != null)
        {
            noteInput.text = record.Note ?? "";
        }

        SetType(record.Type);
        SelectCategoryByName(record.Category);
        RefreshAmountDisplay();
        RefreshDoneButton();
    }

    /// <summary>
    /// 当前宫格子项数（自检/外部查询用）
    /// </summary>
    public int GridItemCount
    {
        get { return gridItems.Count; }
    }

    // ---------- 页签与宫格 ----------

    private void OnExpenseTabClicked()
    {
        SetType((int)RecordType.Expense);
    }

    private void OnIncomeTabClicked()
    {
        SetType((int)RecordType.Income);
    }

    /// <summary>
    /// 切换支出/收入：刷页签黑块高亮 + 销毁重建宫格子项（步骤02 §2.2）
    /// </summary>
    private void SetType(int recordType)
    {
        currentType = recordType;
        RefreshTabVisuals();
        RebuildCategoryGrid();
    }

    /// <summary>
    /// 页签视觉：选中 = 黑块白字，未选中 = 黄底黑字（与顶栏融为一体，录屏同款）
    /// </summary>
    private void RefreshTabVisuals()
    {
        bool expenseSelected = currentType == (int)RecordType.Expense;

        if (tabExpenseImage != null)
        {
            tabExpenseImage.color = expenseSelected ? ColBlack : ColYellow;
        }

        if (tabExpenseLabel != null)
        {
            tabExpenseLabel.color = expenseSelected ? ColWhite : ColBlack;
        }

        if (tabIncomeImage != null)
        {
            tabIncomeImage.color = expenseSelected ? ColYellow : ColBlack;
        }

        if (tabIncomeLabel != null)
        {
            tabIncomeLabel.color = expenseSelected ? ColBlack : ColWhite;
        }
    }

    /// <summary>
    /// 销毁重建宫格子项：读取 CategoryTable，默认选中第一项
    /// </summary>
    private void RebuildCategoryGrid()
    {
        if (gridContent == null || categoryItemPrefab == null)
        {
            Debug.LogWarning("[RecordPanelUI] 宫格容器/子项预制体引用缺失。");
            return;
        }

        for (int i = gridContent.childCount - 1; i >= 0; i--)
        {
            Destroy(gridContent.GetChild(i).gameObject);
        }

        gridItems.Clear();

        List<CategoryTable.CategoryDef> defs = CategoryTable.GetByType(currentType);

        for (int i = 0; i < defs.Count; i++)
        {
            CategoryGridItem item = Instantiate(categoryItemPrefab, gridContent);
            item.Setup(defs[i].Name, iconProvider != null ? iconProvider.GetSprite(defs[i].IconName) : null);
            item.Clicked += OnCategoryItemClicked;
            gridItems.Add(item);
        }

        if (gridItems.Count > 0)
        {
            SelectItem(gridItems[0]);
        }
    }

    private void OnCategoryItemClicked(CategoryGridItem item)
    {
        SelectItem(item);
    }

    /// <summary>
    /// 选中宫格项：底圈变主黄
    /// </summary>
    private void SelectItem(CategoryGridItem target)
    {
        for (int i = 0; i < gridItems.Count; i++)
        {
            gridItems[i].SetSelected(gridItems[i] == target);
        }

        selectedCategory = target != null ? target.CategoryName : "";
    }

    /// <summary>
    /// 按分类名选中（编辑模式回填用），找不到时保持当前选择
    /// </summary>
    private void SelectCategoryByName(string categoryName)
    {
        if (string.IsNullOrEmpty(categoryName))
        {
            return;
        }

        for (int i = 0; i < gridItems.Count; i++)
        {
            if (gridItems[i].CategoryName == categoryName)
            {
                SelectItem(gridItems[i]);
                return;
            }
        }
    }

    // ---------- 键盘与金额状态机 ----------

    private void OnDigitPressed(int digit)
    {
        string digitText = digit.ToString();

        if (currentInput.Contains("."))
        {
            // 已有小数点：小数位最多 2 位
            int decimalCount = currentInput.Length - currentInput.IndexOf('.') - 1;

            if (decimalCount >= MaxDecimalDigits)
            {
                return;
            }

            currentInput += digitText;
        }
        else
        {
            // 整数位上限 7 位；开头 0 直接替换（0 后按 5 → 5）
            if (currentInput == "0")
            {
                currentInput = digitText;
            }
            else if (currentInput.Length >= MaxIntegerDigits)
            {
                return;
            }
            else
            {
                currentInput += digitText;
            }
        }

        RefreshInputVisuals();
    }

    private void OnDotPressed()
    {
        if (currentInput.Contains("."))
        {
            return;
        }

        // 空输入按小数点 → "0."，避免裸 "." 解析歧义
        currentInput = string.IsNullOrEmpty(currentInput) ? "0." : currentInput + ".";
        RefreshInputVisuals();
    }

    private void OnPlusPressed()
    {
        OnOperatorPressed('+');
    }

    private void OnMinusPressed()
    {
        OnOperatorPressed('-');
    }

    /// <summary>
    /// +/-：currentInput 按 pending 运算符并入 committedFen，再记录新运算符（步骤02 §2.3）
    /// </summary>
    private void OnOperatorPressed(char op)
    {
        if (currentInput.Length > 0)
        {
            long termFen = ParseInputToFen(currentInput);
            committedFen = lastOperator == '-' ? committedFen - termFen : committedFen + termFen;
            currentInput = "";
        }

        lastOperator = op;
        RefreshInputVisuals();
    }

    private void OnBackspacePressed()
    {
        // 简化版（步骤02 §2.3）：只退 currentInput 末位；空了不退连加累计
        if (currentInput.Length > 0)
        {
            currentInput = currentInput.Substring(0, currentInput.Length - 1);
            RefreshInputVisuals();
        }
    }

    private void OnTodayClicked()
    {
        // 本版日期固定为当天，「今天」为占位按钮（补记历史日期不做）
        Debug.Log("[RecordPanelUI] 「今天」：日期固定为当天（占位）。");
    }

    /// <summary>
    /// 当前总计（含 pending 运算），完成键可用性依据
    /// </summary>
    private long GetTotalFen()
    {
        long termFen = ParseInputToFen(currentInput);
        return lastOperator == '-' ? committedFen - termFen : committedFen + termFen;
    }

    /// <summary>
    /// 纯字符串解析数字串 → 分：整数×100 + 小数右补零（步骤02 §2.3，禁 float）
    /// </summary>
    private static long ParseInputToFen(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return 0;
        }

        int dotIndex = input.IndexOf('.');
        long intPart = 0;
        long fracPart = 0;

        if (dotIndex < 0)
        {
            long.TryParse(input, out intPart);
        }
        else
        {
            long.TryParse(input.Substring(0, dotIndex), out intPart);
            string frac = dotIndex + 1 < input.Length ? input.Substring(dotIndex + 1) : "";

            if (frac.Length > MaxDecimalDigits)
            {
                frac = frac.Substring(0, MaxDecimalDigits);
            }

            frac = frac.PadRight(MaxDecimalDigits, '0');
            long.TryParse(frac, out fracPart);
        }

        if (intPart < 0)
        {
            intPart = 0;
        }

        return intPart * 100 + fracPart;
    }

    /// <summary>
    /// 分 → "11.00"（整数分拼接两位小数，步骤02 §4）
    /// </summary>
    private static string FormatFen(long fen)
    {
        long yuan = fen / 100;
        long fenRemainder = Math.Abs(fen % 100);
        return yuan.ToString() + "." + fenRemainder.ToString("D2");
    }

    /// <summary>
    /// 分 → 去尾零（"11.00"→"11"，"12.30"→"12.3"），进行中式显示用
    /// </summary>
    private static string FormatFenTrimZeros(long fen)
    {
        string text = FormatFen(fen);

        if (text.Contains("."))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text;
    }

    /// <summary>
    /// 进行中式显示："12.3+4"（步骤02 §2.3 显示规则）
    /// </summary>
    private void RefreshAmountDisplay()
    {
        if (amountText == null)
        {
            return;
        }

        if (currentInput.Length > 0)
        {
            if (committedFen != 0)
            {
                amountText.text = FormatFenTrimZeros(committedFen) + lastOperator + currentInput;
            }
            else if (lastOperator == '-')
            {
                amountText.text = "-" + currentInput;
            }
            else
            {
                amountText.text = currentInput;
            }
        }
        else if (committedFen != 0)
        {
            amountText.text = FormatFenTrimZeros(committedFen);
        }
        else
        {
            amountText.text = "0.00";
        }
    }

    /// <summary>
    /// 总计 ≤ 0（含双 0）时完成键置灰（步骤02 §2.3）
    /// </summary>
    private void RefreshDoneButton()
    {
        if (doneButton == null)
        {
            return;
        }

        bool canSave = GetTotalFen() > 0;
        doneButton.interactable = canSave;
        Image doneImage = doneButton.image;

        if (doneImage != null)
        {
            doneImage.color = canSave ? ColYellow : ColDoneDisabled;
        }
    }

    private void RefreshInputVisuals()
    {
        RefreshAmountDisplay();
        RefreshDoneButton();
    }

    /// <summary>
    /// 复位金额状态机（连加累计/当前输入/运算符）
    /// </summary>
    private void ClearInputState()
    {
        committedFen = 0;
        currentInput = "";
        lastOperator = '+';
        RefreshInputVisuals();
    }

    // ---------- 保存流程 ----------

    /// <summary>
    /// 完成：组装 AccountRecord 落库（新增 AddRecord / 编辑 UpdateRecord 分流），成功后复位并回明细页
    /// </summary>
    private void OnDoneClicked()
    {
        long totalFen = GetTotalFen();

        if (totalFen <= 0)
        {
            Debug.LogWarning("[RecordPanelUI] 总计金额无效，不保存。");
            return;
        }

        if (string.IsNullOrEmpty(selectedCategory))
        {
            Debug.LogWarning("[RecordPanelUI] 未选中分类，不保存。");
            return;
        }

        AccountRecord record = editingRecord != null ? editingRecord : new AccountRecord();
        record.Type = currentType;
        record.Category = selectedCategory;
        record.AmountFen = totalFen;

        if (editingRecord == null)
        {
            // 新增：日期/创建时间取当下；编辑保留原值（编辑不改记账日期）
            record.Date = DateTime.Now.ToString("yyyy-MM-dd");
            record.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        record.Note = noteInput != null ? noteInput.text : "";

        bool success = editingRecord != null
            ? accountManager.UpdateRecord(record)
            : accountManager.AddRecord(record);

        if (success == false)
        {
            Debug.LogWarning("[RecordPanelUI] 保存失败（AccountManager 拒绝），面板保持打开。");
            return;
        }

        Debug.Log($"[RecordPanelUI] 已保存：{(RecordType)record.Type} {record.Category} {FormatFen(record.AmountFen)} 元（{(editingRecord != null ? "编辑" : "新增")}）。");
        ResetAndClose();
    }

    /// <summary>
    /// 复位输入态并关闭面板（回明细页）；OnDataChanged 由 AccountManager 自动广播驱动刷新
    /// </summary>
    private void ResetAndClose()
    {
        editingRecord = null;
        ClearInputState();

        if (noteInput != null)
        {
            noteInput.text = "";
        }

        selectedCategory = "";

        if (uiManager != null)
        {
            uiManager.OpenDetailPanel();
        }
    }

    /// <summary>
    /// 取消：不写入任何数据，直接复位并关闭（步骤02 验收项）
    /// </summary>
    private void OnCancelClicked()
    {
        ResetAndClose();
    }
}
