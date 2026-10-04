using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class AppFlowManager : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private UIManager uiManager;

    [Header("Panel Controllers")]
    [SerializeField] private RecordPanelUI recordPanelUI;

    [Header("Bottom Navigation Buttons")]
    [SerializeField] private Button btnDetail;
    [SerializeField] private Button btnRecord;
    [SerializeField] private Button btnChart;
    [SerializeField] private Button btnDiscover;
    [SerializeField] private Button btnSettings;

    private void Awake()
    {
        TryInitializeDependencies();
        RegisterEvents();
    }

    /// <summary>
    /// 外部引用为空时自动查找（记账页初始为未激活，需包含未激活对象）
    /// </summary>
    private void TryInitializeDependencies()
    {
        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>();
        }

        if (recordPanelUI == null)
        {
            recordPanelUI = FindFirstObjectByType<RecordPanelUI>(FindObjectsInactive.Include);
        }
    }

    /// <summary>
    /// 底部导航按钮事件绑定
    /// </summary>
    private void RegisterEvents()
    {
        RegisterButton(btnDetail, OnClickDetail);
        RegisterButton(btnRecord, OnClickRecord);
        RegisterButton(btnChart, OnClickChart);
        RegisterButton(btnDiscover, OnClickDiscover);
        RegisterButton(btnSettings, OnClickSettings);
    }

    /// <summary>
    /// 按钮通用绑定
    /// </summary>
    private void RegisterButton(Button button, UnityAction action)
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
    /// 明细 按钮
    /// </summary>
    private void OnClickDetail()
    {
        if (uiManager != null)
        {
            uiManager.OpenDetailPanel();
        }
    }

    /// <summary>
    /// 记账 按钮：先复位为新增模式再打开（半屏覆盖式，不隐藏底栏）
    /// </summary>
    private void OnClickRecord()
    {
        if (recordPanelUI != null)
        {
            recordPanelUI.SetupForNew();
        }

        if (uiManager != null)
        {
            uiManager.OpenRecordPanel();
        }
    }

    /// <summary>
    /// 图表 按钮
    /// </summary>
    private void OnClickChart()
    {
        if (uiManager != null)
        {
            uiManager.OpenChartPanel();
        }
    }

    /// <summary>
    /// 发现 按钮（占位空白页，后续开发）
    /// </summary>
    private void OnClickDiscover()
    {
        if (uiManager != null)
        {
            uiManager.OpenDiscoverPanel();
        }
    }

    /// <summary>
    /// 设置 按钮
    /// </summary>
    private void OnClickSettings()
    {
        if (uiManager != null)
        {
            uiManager.OpenSettingsPanel();
        }
    }
}
