using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class AppFlowManager : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private UIManager uiManager;

    [Header("Bottom Navigation Buttons")]
    [SerializeField] private Button btnDetail;
    [SerializeField] private Button btnRecord;
    [SerializeField] private Button btnChart;

    private void Awake()
    {
        TryInitializeDependencies();
        RegisterEvents();
    }

    /// <summary>
    /// UIManager 自动 查找
    /// </summary>
    private void TryInitializeDependencies()
    {
        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>();
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
    /// 记账 按钮
    /// </summary>
    private void OnClickRecord()
    {
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
}
