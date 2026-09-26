using UnityEngine;

public class UIManager : MonoBehaviour
{
    [Header("Main Panels")]
    [SerializeField] private GameObject detailPanel;
    [SerializeField] private GameObject chartPanel;
    [SerializeField] private GameObject recordPanel;

    [Header("Popup Panels")]
    [SerializeField] private GameObject exportPanel;
    [SerializeField] private GameObject importPanel;

    /// <summary>
    /// 仅激活指定主面板（Detail/Chart/Record 三选一）
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
}
