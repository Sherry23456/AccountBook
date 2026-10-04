using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 发现页控制器（步骤09）：两张卡片。
/// 账单卡 = 当月收支结余摘要，整卡可点进账单子页；预算卡 = 当月总预算圆环（剩余%）
/// + 剩余/预算/支出三行，卡身可点进预算子页，右上"+ 设置预算"直接开设置弹窗。
/// 未设预算时预算卡整卡置灰（图一态）；设了预算后圆环黄色按剩余比例填充（图六态）。
/// 刷新链：OnEnable + OnDataChanged/OnBudgetChanged 订阅。
/// </summary>
public class DiscoverPanelUI : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private AccountManager accountManager;
    [SerializeField] private BudgetManager budgetManager;
    [SerializeField] private UIManager uiManager;
    [SerializeField] private BudgetDialogUI budgetDialog;

    [Header("Bill Card")]
    [SerializeField] private Button billCardButton;
    [SerializeField] private TextMeshProUGUI txtBillMonth;    // "10 月"
    [SerializeField] private TextMeshProUGUI txtBillIncome;   // 收入
    [SerializeField] private TextMeshProUGUI txtBillExpense;  // 支出
    [SerializeField] private TextMeshProUGUI txtBillBalance;  // 结余

    [Header("Budget Card")]
    [SerializeField] private Button budgetCardButton;
    [SerializeField] private Button btnSetBudget;
    [SerializeField] private TextMeshProUGUI txtBudgetCardTitle;  // "10月总预算"
    [SerializeField] private RingGraphic ringTrack;
    [SerializeField] private RingGraphic ringFill;
    [SerializeField] private TextMeshProUGUI txtRingRemain;   // "剩余"
    [SerializeField] private TextMeshProUGUI txtRingPercent;  // "0%"
    [SerializeField] private TextMeshProUGUI lbRemain;        // "剩余预算:"
    [SerializeField] private TextMeshProUGUI lbBudget;        // "本月预算:"
    [SerializeField] private TextMeshProUGUI lbSpent;         // "本月支出:"
    [SerializeField] private TextMeshProUGUI txtRemain;
    [SerializeField] private TextMeshProUGUI txtBudget;
    [SerializeField] private TextMeshProUGUI txtSpent;

    private static readonly Color ColBlack = new Color32(0x22, 0x22, 0x22, 0xFF);
    private static readonly Color ColGray = new Color32(0x66, 0x66, 0x66, 0xFF);
    private static readonly Color ColUnset = new Color32(0xC8, 0xC8, 0xC8, 0xFF);

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
    }

    /// <summary>
    /// 外部引用为空时自动查找（QuikDelivery 模式；弹窗初始未激活需包含未激活对象）
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
        RegisterButton(billCardButton, OnBillCardClicked);
        RegisterButton(budgetCardButton, OnBudgetCardClicked);
        RegisterButton(btnSetBudget, OnSetBudgetClicked);
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

    private void OnBillCardClicked()
    {
        if (uiManager != null)
        {
            uiManager.OpenBillPanel();
        }
    }

    private void OnBudgetCardClicked()
    {
        if (uiManager != null)
        {
            uiManager.OpenBudgetPanel();
        }
    }

    /// <summary>
    /// "+ 设置预算"：直接开当月总预算弹窗（图一入口），卡身其余区域进预算子页
    /// </summary>
    private void OnSetBudgetClicked()
    {
        if (budgetDialog == null)
        {
            Debug.LogWarning("[DiscoverPanelUI] 预算弹窗引用缺失（请重跑搭建菜单）。");
            return;
        }

        budgetDialog.ShowMonthTotal(DateTime.Now.ToString("yyyy-MM"),
            budgetManager != null ? budgetManager.GetMonthBudget(DateTime.Now.ToString("yyyy-MM")) : 0);
    }

    // ---------- 刷新 ----------

    private void RefreshAll()
    {
        if (accountManager == null)
        {
            return;
        }

        DateTime now = DateTime.Now;
        string monthKey = now.ToString("yyyy-MM");

        // —— 账单卡：当月收支结余 ——
        accountManager.GetMonthSummary(monthKey, out long incomeFen, out long expenseFen);

        if (txtBillMonth != null)
        {
            txtBillMonth.text = now.Month + " 月";
        }

        if (txtBillIncome != null)
        {
            txtBillIncome.text = MoneyText.FormatYuan(incomeFen);
        }

        if (txtBillExpense != null)
        {
            txtBillExpense.text = MoneyText.FormatYuan(expenseFen);
        }

        if (txtBillBalance != null)
        {
            txtBillBalance.text = MoneyText.FormatYuan(incomeFen - expenseFen);
        }

        // —— 预算卡：当月总预算圆环 ——
        long budgetFen = budgetManager != null ? budgetManager.GetMonthBudget(monthKey) : 0;
        bool hasBudget = budgetFen > 0;

        if (txtBudgetCardTitle != null)
        {
            txtBudgetCardTitle.text = now.Month + "月总预算";
        }

        long remainFen = hasBudget ? budgetFen - expenseFen : 0;
        float percent = hasBudget ? Mathf.Clamp01((float)((double)remainFen / budgetFen)) : 0f;

        if (ringFill != null)
        {
            ringFill.gameObject.SetActive(hasBudget);
            ringFill.Progress = percent;
        }

        if (txtRingPercent != null)
        {
            txtRingPercent.text = Mathf.RoundToInt(percent * 100f) + "%";
            txtRingPercent.color = hasBudget ? ColBlack : ColUnset;
        }

        if (txtRingRemain != null)
        {
            txtRingRemain.color = hasBudget ? ColGray : ColUnset;
        }

        Color labelColor = hasBudget ? ColGray : ColUnset;

        if (lbRemain != null)
        {
            lbRemain.color = labelColor;
        }

        if (lbBudget != null)
        {
            lbBudget.color = labelColor;
        }

        if (lbSpent != null)
        {
            lbSpent.color = labelColor;
        }

        if (txtRemain != null)
        {
            txtRemain.text = MoneyText.FormatYuan(hasBudget ? remainFen : 0);
            txtRemain.color = hasBudget ? ColBlack : ColUnset;
        }

        if (txtBudget != null)
        {
            txtBudget.text = MoneyText.FormatYuan(hasBudget ? budgetFen : 0);
            txtBudget.color = hasBudget ? ColGray : ColUnset;
        }

        if (txtSpent != null)
        {
            txtSpent.text = MoneyText.FormatYuan(hasBudget ? expenseFen : 0);
            txtSpent.color = hasBudget ? ColGray : ColUnset;
        }
    }
}
