using UnityEngine;

public class UIManager : MonoBehaviour
{
    [Header("Main Panels")]
    [SerializeField] private GameObject detailPanel;
    [SerializeField] private GameObject chartPanel;
    [SerializeField] private GameObject recordPanel;
    [SerializeField] private GameObject discoverPanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private GameObject billPanel;      // 步骤09 发现→账单子页
    [SerializeField] private GameObject budgetPanel;    // 步骤09 发现→预算子页

    [Header("Popup Panels")]
    [SerializeField] private GameObject exportPanel;
    [SerializeField] private GameObject importPanel;
    [SerializeField] private GameObject budgetDialog;   // 步骤09 预算设置底部弹窗

    [Header("Bottom Nav")]
    [SerializeField] private BottomNavUI bottomNav;     // 步骤10 底栏图标 Normal/Selected 刷新

    private void Awake()
    {
        if (bottomNav == null)
        {
            bottomNav = FindFirstObjectByType<BottomNavUI>(FindObjectsInactive.Include);
        }

        // 场景可能带着上次自检/测试的页面显隐状态落盘（弹窗开着、停在图表页等）：
        // 启动一律回明细页、弹窗全关，运行时兜底与 15 号 Builder 落盘复位互为双保险
        SetOnlyPanelActive(detailPanel);

        if (exportPanel != null)
        {
            exportPanel.SetActive(false);
        }

        if (importPanel != null)
        {
            importPanel.SetActive(false);
        }

        if (budgetDialog != null)
        {
            budgetDialog.SetActive(false);
        }
    }

    /// <summary>
    /// 仅激活指定主面板（Detail/Chart/Record/Discover/Settings/Bill/Budget 之一）
    /// </summary>
    public void SetOnlyPanelActive(GameObject targetPanel)
    {
        if (detailPanel != null)
        {
            detailPanel.SetActive(targetPanel == detailPanel);
        }

        if (chartPanel != null)
        {
            chartPanel.SetActive(targetPanel == chartPanel);
        }

        if (recordPanel != null)
        {
            recordPanel.SetActive(targetPanel == recordPanel);
        }

        if (discoverPanel != null)
        {
            discoverPanel.SetActive(targetPanel == discoverPanel);
        }

        if (settingsPanel != null)
        {
            settingsPanel.SetActive(targetPanel == settingsPanel);
        }

        if (billPanel != null)
        {
            billPanel.SetActive(targetPanel == billPanel);
        }

        if (budgetPanel != null)
        {
            budgetPanel.SetActive(targetPanel == budgetPanel);
        }

        RefreshBottomNav(targetPanel);
    }

    /// <summary>
    /// 底栏图标态随当前主面板刷新（发现子页账单/预算归发现高亮；记账页四页签全灰）
    /// </summary>
    private void RefreshBottomNav(GameObject targetPanel)
    {
        if (bottomNav == null)
        {
            return;
        }

        BottomNavUI.NavTab tab;

        if (targetPanel == chartPanel)
        {
            tab = BottomNavUI.NavTab.Chart;
        }
        else if (targetPanel == recordPanel)
        {
            tab = BottomNavUI.NavTab.Record;
        }
        else if (targetPanel == discoverPanel || targetPanel == billPanel || targetPanel == budgetPanel)
        {
            tab = BottomNavUI.NavTab.Discover;
        }
        else if (targetPanel == settingsPanel)
        {
            tab = BottomNavUI.NavTab.Settings;
        }
        else
        {
            tab = BottomNavUI.NavTab.Detail;
        }

        bottomNav.SetTab(tab);
    }

    /// <summary>
    /// 打开明细界面（默认页）
    /// </summary>
    public void OpenDetailPanel()
    {
        SetOnlyPanelActive(detailPanel);
    }

    /// <summary>
    /// 打开图表界面
    /// </summary>
    public void OpenChartPanel()
    {
        SetOnlyPanelActive(chartPanel);
    }

    /// <summary>
    /// 打开记账界面（半屏覆盖式，不隐藏底栏）
    /// </summary>
    public void OpenRecordPanel()
    {
        SetOnlyPanelActive(recordPanel);
    }

    /// <summary>
    /// 打开发现界面
    /// </summary>
    public void OpenDiscoverPanel()
    {
        SetOnlyPanelActive(discoverPanel);
    }

    /// <summary>
    /// 打开账单子页（发现→账单）
    /// </summary>
    public void OpenBillPanel()
    {
        SetOnlyPanelActive(billPanel);
    }

    /// <summary>
    /// 打开预算子页（发现→预算）
    /// </summary>
    public void OpenBudgetPanel()
    {
        SetOnlyPanelActive(budgetPanel);
    }

    /// <summary>
    /// 打开设置界面
    /// </summary>
    public void OpenSettingsPanel()
    {
        SetOnlyPanelActive(settingsPanel);
    }

    /// <summary>
    /// 打开导出弹窗（叠加展示，不影响底下面板）
    /// </summary>
    public void OpenExportPanel()
    {
        if (exportPanel != null)
        {
            exportPanel.SetActive(true);
        }
    }

    /// <summary>
    /// 关闭导出弹窗
    /// </summary>
    public void CloseExportPanel()
    {
        if (exportPanel != null)
        {
            exportPanel.SetActive(false);
        }
    }

    /// <summary>
    /// 打开导入弹窗（叠加展示，不影响底下面板）
    /// </summary>
    public void OpenImportPanel()
    {
        if (importPanel != null)
        {
            importPanel.SetActive(true);
        }
    }

    /// <summary>
    /// 关闭导入弹窗
    /// </summary>
    public void CloseImportPanel()
    {
        if (importPanel != null)
        {
            importPanel.SetActive(false);
        }
    }

    /// <summary>
    /// 打开预算设置弹窗（叠加展示，不影响底下面板；内容态由 BudgetDialogUI.ShowXxx 指定）
    /// </summary>
    public void OpenBudgetDialog()
    {
        if (budgetDialog != null)
        {
            budgetDialog.SetActive(true);
        }
    }

    /// <summary>
    /// 关闭预算设置弹窗
    /// </summary>
    public void CloseBudgetDialog()
    {
        if (budgetDialog != null)
        {
            budgetDialog.SetActive(false);
        }
    }
}
