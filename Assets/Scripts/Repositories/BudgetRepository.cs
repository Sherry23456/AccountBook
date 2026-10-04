using UnityEngine;

/// <summary>
/// 预算仓储（步骤09）：budgets.json 读写，结构同 AccountRepository（JsonFileService 转传）。
/// </summary>
public class BudgetRepository : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private JsonFileService jsonFileService;

    [Header("Storage Settings")]
    [SerializeField] private string fileName = "budgets.json";

    private void Awake()
    {
        TryInitializeDependencies();
    }

    /// <summary>
    /// 外部引用为空时自动查找
    /// </summary>
    private void TryInitializeDependencies()
    {
        if (jsonFileService == null)
        {
            jsonFileService = FindFirstObjectByType<JsonFileService>();
        }
    }

    /// <summary>
    /// 全量预算读取；文件缺失/字段缺失时返回空表结构
    /// </summary>
    public BudgetData Load()
    {
        TryInitializeDependencies();

        if (jsonFileService == null)
        {
            Debug.LogWarning("[BudgetRepository] JsonFileService 引用缺失.");
            return new BudgetData();
        }

        BudgetData data = jsonFileService.LoadFromJson<BudgetData>(fileName);

        if (data == null)
        {
            data = new BudgetData();
        }

        if (data.MonthTotals == null)
        {
            data.MonthTotals = new System.Collections.Generic.List<BudgetEntry>();
        }

        if (data.YearTotals == null)
        {
            data.YearTotals = new System.Collections.Generic.List<BudgetEntry>();
        }

        if (data.CategoryBudgets == null)
        {
            data.CategoryBudgets = new System.Collections.Generic.List<CategoryBudgetEntry>();
        }

        return data;
    }

    /// <summary>
    /// 全量预算保存
    /// </summary>
    public void Save(BudgetData data)
    {
        TryInitializeDependencies();

        if (jsonFileService == null)
        {
            Debug.LogWarning("[BudgetRepository] JsonFileService 引用缺失.");
            return;
        }

        if (data == null)
        {
            Debug.LogWarning("[BudgetRepository] 要保存的 BudgetData 为 null。");
            return;
        }

        jsonFileService.SaveToJson(fileName, data);
    }
}
