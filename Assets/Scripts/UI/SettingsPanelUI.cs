using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 设置页（步骤08b）："音效"行走 SfxToggleUI 自管；"导出数据/导入数据"行点击打开对应弹窗，
/// 复用图表页同款 UIManager 入口（弹窗本体不动）。由 15 号 Builder 搭建并接线按钮引用。
/// </summary>
public class SettingsPanelUI : MonoBehaviour
{
    [SerializeField] private UIManager uiManager;
    [SerializeField] private Button exportButton;
    [SerializeField] private Button importButton;

    private void Awake()
    {
        if (uiManager == null)
        {
            uiManager = FindFirstObjectByType<UIManager>();
        }

        WireButton(exportButton, OpenExport);
        WireButton(importButton, OpenImport);
    }

    /// <summary>
    /// 行按钮统一绑定：先响点击音再开弹窗（幂等，可重复调用）
    /// </summary>
    private void WireButton(Button button, System.Action action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(SfxManager.PlayClick);
        button.onClick.AddListener(() => action());
    }

    private void OpenExport()
    {
        if (uiManager != null)
        {
            uiManager.OpenExportPanel();
        }
    }

    private void OpenImport()
    {
        if (uiManager != null)
        {
            uiManager.OpenImportPanel();
        }
    }
}
