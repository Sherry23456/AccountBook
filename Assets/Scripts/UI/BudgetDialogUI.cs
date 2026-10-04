using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 预算设置底部弹窗（步骤09，图四/图五）：白底圆角面板 + 标题 + 金额显示框 + 确定
/// + 4×3 数字键盘（7 8 9 / 4 5 6 / 1 2 3 / . 0 退格），分类模式另有支出分类宫格。
/// 四种用法：每月总预算 / 年度总预算 / 添加分类预算 / 编辑分类预算（带"删除预算"）。
/// 金额状态机沿用记账页约定：全程 long 分、整数位 7 位小数位 2 位、禁 float；
/// 确定 >0 才可用，分类模式须选中分类。保存走 BudgetManager，OnBudgetChanged 驱动各页刷新。
/// </summary>
public class BudgetDialogUI : MonoBehaviour
{
    /// <summary>
    /// 整数位上限 7 位（9 位数封顶，同记账页）
    /// </summary>
    private const int MaxIntegerDigits = 7;

    /// <summary>
    /// 小数位上限 2 位
    /// </summary>
    private const int MaxDecimalDigits = 2;

    private static readonly Color ColBlack = new Color32(0x22, 0x22, 0x22, 0xFF);
    private static readonly Color ColGray = new Color32(0x99, 0x99, 0x99, 0xFF);
    private static readonly Color ColYellow = new Color32(0xFF, 0xD1, 0x00, 0xFF);
    private static readonly Color ColDoneDisabled = new Color32(0xDD, 0xDD, 0xDD, 0xFF);
    private static readonly Color ColPlaceholder = new Color32(0x99, 0x99, 0x99, 0xFF);

    /// <summary>
    /// 弹窗模式
    /// </summary>
    public enum DialogMode
    {
        MonthTotal = 0,
        YearTotal = 1,
        CategoryAdd = 2,
        CategoryEdit = 3
    }

    [Header("Dependencies")]
    [SerializeField] private BudgetManager budgetManager;
    [SerializeField] private UIManager uiManager;
    [SerializeField] private CategoryIconProvider iconProvider;
    [SerializeField] private CategoryGridItem categoryItemPrefab;

    [Header("Sheet")]
    [SerializeField] private RectTransform sheet;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private Button btnClose;
    [SerializeField] private Button maskButton;
    [SerializeField] private TextMeshProUGUI txtInput;      // 金额显示（占位 "请输入预算金额"）
    [SerializeField] private Button btnConfirm;
    [SerializeField] private Image imgConfirm;
    [SerializeField] private Button btnDeleteBudget;        // 仅编辑分类模式显示
    [SerializeField] private GameObject categoryScroll;     // 分类宫格滚动区（仅分类模式显示）
    [SerializeField] private RectTransform gridContent;

    [Header("Keyboard")]
    [SerializeField] private Button[] digitButtons = new Button[10];
    [SerializeField] private Button dotButton;
    [SerializeField] private Button backspaceButton;

    private const float SheetBaseHeight = 1150f;
    private const float SheetCategoryExtra = 520f;

    private DialogMode mode = DialogMode.MonthTotal;
    private string monthKey = "";
    private string editCategory = "";
    private string currentInput = "";
    private string selectedCategory = "";
    private readonly List<CategoryGridItem> gridItems = new List<CategoryGridItem>();

    /// <summary>
    /// 当前模式（自检断言用）
    /// </summary>
    public DialogMode Mode
    {
        get { return mode; }
    }

    /// <summary>
    /// 当前输入框文本（自检断言用）
    /// </summary>
    public string InputText
    {
        get { return txtInput != null ? txtInput.text : ""; }
    }

    // ---------- 打开入口 ----------

    /// <summary>
    /// 每月总预算（图四）
    /// </summary>
    public void ShowMonthTotal(string yyyyMM, long currentFen)
    {
        Open(DialogMode.MonthTotal, "每月总预算", yyyyMM, "", currentFen);
    }

    /// <summary>
    /// 年度总预算（图五）
    /// </summary>
    public void ShowYearTotal(string yyyy, long currentFen)
    {
        Open(DialogMode.YearTotal, "年度总预算", yyyy, "", currentFen);
    }

    /// <summary>
    /// 添加分类预算（支出分类宫格 + 键盘）
    /// </summary>
    public void ShowCategoryAdd(string yyyyMM)
    {
        Open(DialogMode.CategoryAdd, "添加分类预算", yyyyMM, "", 0);
    }

    /// <summary>
    /// 编辑分类预算（回填金额 + 删除预算入口）
    /// </summary>
    public void ShowCategoryEdit(string yyyyMM, string category, long currentFen)
    {
        Open(DialogMode.CategoryEdit, "编辑分类预算", yyyyMM, category, currentFen);
    }

    private void Open(DialogMode dialogMode, string title, string key, string category, long currentFen)
    {
        mode = dialogMode;
        monthKey = key;
        editCategory = category ?? "";
        selectedCategory = mode == DialogMode.CategoryEdit ? editCategory : "";
        currentInput = currentFen > 0 ? FormatFenTrimZeros(currentFen) : "";

        if (titleLabel != null)
        {
            titleLabel.text = title;
        }

        RebuildCategoryGrid();
        UpdateLayoutHeight();
        RefreshVisuals();
        gameObject.SetActive(true);
    }

    private void OnEnable()
    {
        RegisterEvents();
        RefreshVisuals();
    }

    // ---------- 分类宫格 ----------

    /// <summary>
    /// 重建支出分类宫格（仅分类模式显示；编辑模式预选原分类）
    /// </summary>
    private void RebuildCategoryGrid()
    {
        bool categoryMode = mode == DialogMode.CategoryAdd || mode == DialogMode.CategoryEdit;

        if (categoryScroll != null)
        {
            categoryScroll.SetActive(categoryMode);
        }

        if (!categoryMode)
        {
            return;
        }

        if (gridContent == null || categoryItemPrefab == null)
        {
            Debug.LogWarning("[BudgetDialogUI] 分类宫格容器/预制体引用缺失（请重跑搭建菜单）。");
            return;
        }

        for (int i = gridContent.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(gridContent.GetChild(i).gameObject);
        }

        gridItems.Clear();

        List<CategoryTable.CategoryDef> defs = CategoryTable.GetByType((int)RecordType.Expense);

        for (int i = 0; i < defs.Count; i++)
        {
            CategoryGridItem item = Instantiate(categoryItemPrefab, gridContent);
            item.Setup(defs[i].Name, iconProvider != null ? iconProvider.GetSprite(defs[i].IconName) : null);
            item.Clicked += OnCategoryItemClicked;
            item.SetSelected(defs[i].Name == selectedCategory);
            gridItems.Add(item);
        }
    }

    private void OnCategoryItemClicked(CategoryGridItem item)
    {
        selectedCategory = item != null ? item.CategoryName : "";

        for (int i = 0; i < gridItems.Count; i++)
        {
            gridItems[i].SetSelected(gridItems[i] == item);
        }

        RefreshVisuals();
    }

    // ---------- 键盘状态机（记账页同约定） ----------

    private void OnDigitPressed(int digit)
    {
        string digitText = digit.ToString();

        if (currentInput.Contains("."))
        {
            int decimalCount = currentInput.Length - currentInput.IndexOf('.') - 1;

            if (decimalCount >= MaxDecimalDigits)
            {
                return;
            }

            currentInput += digitText;
        }
        else
        {
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

        RefreshVisuals();
    }

    private void OnDotPressed()
    {
        if (currentInput.Contains("."))
        {
            return;
        }

        currentInput = string.IsNullOrEmpty(currentInput) ? "0." : currentInput + ".";
        RefreshVisuals();
    }

    private void OnBackspacePressed()
    {
        if (currentInput.Length > 0)
        {
            currentInput = currentInput.Substring(0, currentInput.Length - 1);
            RefreshVisuals();
        }
    }

    /// <summary>
    /// 纯字符串解析数字串 → 分（记账页同源，禁 float）
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

    private static string FormatFenTrimZeros(long fen)
    {
        long yuan = fen / 100;
        long fenRemainder = Math.Abs(fen % 100);
        string text = yuan.ToString() + "." + fenRemainder.ToString("D2");

        if (text.Contains("."))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text;
    }

    // ---------- 视觉与保存 ----------

    private void RefreshVisuals()
    {
        if (txtInput != null)
        {
            bool hasInput = currentInput.Length > 0;
            txtInput.text = hasInput ? currentInput : "请输入预算金额";
            txtInput.color = hasInput ? ColBlack : ColPlaceholder;
        }

        if (btnDeleteBudget != null)
        {
            btnDeleteBudget.gameObject.SetActive(mode == DialogMode.CategoryEdit);
        }

        bool canConfirm = ParseInputToFen(currentInput) > 0;

        if (mode == DialogMode.CategoryAdd || mode == DialogMode.CategoryEdit)
        {
            canConfirm &= !string.IsNullOrEmpty(selectedCategory);
        }

        if (btnConfirm != null)
        {
            btnConfirm.interactable = canConfirm;
        }

        if (imgConfirm != null)
        {
            imgConfirm.color = canConfirm ? ColYellow : ColDoneDisabled;
        }
    }

    /// <summary>
    /// 分类模式加高弹窗（宫格区展开）
    /// </summary>
    private void UpdateLayoutHeight()
    {
        if (sheet == null)
        {
            return;
        }

        bool categoryMode = mode == DialogMode.CategoryAdd || mode == DialogMode.CategoryEdit;
        sheet.sizeDelta = new Vector2(sheet.sizeDelta.x, SheetBaseHeight + (categoryMode ? SheetCategoryExtra : 0f));
    }

    private void OnConfirmClicked()
    {
        long amountFen = ParseInputToFen(currentInput);

        if (amountFen <= 0)
        {
            ToastUI.Show("请输入预算金额");
            return;
        }

        if (budgetManager == null)
        {
            Debug.LogWarning("[BudgetDialogUI] BudgetManager 引用缺失，不保存。");
            return;
        }

        if (mode == DialogMode.MonthTotal)
        {
            budgetManager.SetMonthBudget(monthKey, amountFen);
        }
        else if (mode == DialogMode.YearTotal)
        {
            budgetManager.SetYearBudget(monthKey, amountFen);
        }
        else
        {
            if (string.IsNullOrEmpty(selectedCategory))
            {
                ToastUI.Show("请选择分类");
                return;
            }

            budgetManager.SetCategoryBudget(monthKey, selectedCategory, amountFen);
        }

        SfxManager.PlaySuccess();
        Close();
    }

    private void OnDeleteClicked()
    {
        if (budgetManager != null && mode == DialogMode.CategoryEdit)
        {
            budgetManager.RemoveCategoryBudget(monthKey, editCategory);
        }

        Close();
    }

    /// <summary>
    /// 关闭弹窗（确定/删除/×/点遮罩共用），不触发任何回调
    /// </summary>
    public void Close()
    {
        if (uiManager != null)
        {
            uiManager.CloseBudgetDialog();
            return;
        }

        gameObject.SetActive(false);
    }

    /// <summary>
    /// 事件绑定（幂等，可重复调用）
    /// </summary>
    private void RegisterEvents()
    {
        RegisterButton(btnConfirm, OnConfirmClicked);
        RegisterButton(btnDeleteBudget, OnDeleteClicked);
        RegisterButton(btnClose, Close);
        RegisterButton(maskButton, Close);
        RegisterButton(dotButton, OnDotPressed);
        RegisterButton(backspaceButton, OnBackspacePressed);

        if (digitButtons != null)
        {
            for (int i = 0; i < digitButtons.Length && i < 10; i++)
            {
                int digit = i;   // 闭包捕获循环变量
                RegisterButton(digitButtons[i], () => OnDigitPressed(digit));
            }
        }
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
}
