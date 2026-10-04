using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 预算管理器（步骤09）：月总预算 / 年总预算 / 分类预算的读写门面，缓存整份 BudgetData，
/// 变更后落盘并广播 OnBudgetChanged（发现页/预算页订阅刷新）。金额全程 long 分。
/// 预算额 ≤ 0 视为删除该条（设置弹窗只在 >0 时允许确定，这里是数据层兜底）。
/// </summary>
public class BudgetManager : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private BudgetRepository budgetRepository;

    /// <summary>
    /// 预算数据变化通知：增/删/改成功后 Invoke
    /// </summary>
    public event Action OnBudgetChanged;

    private BudgetData cache;

    private void Awake()
    {
        TryInitializeDependencies();
    }

    /// <summary>
    /// 外部引用为空时自动查找（含未激活对象）
    /// </summary>
    private void TryInitializeDependencies()
    {
        if (budgetRepository == null)
        {
            budgetRepository = FindFirstObjectByType<BudgetRepository>(FindObjectsInactive.Include);
        }
    }

    /// <summary>
    /// 整份数据（懒加载，读不到文件时为空表）
    /// </summary>
    private BudgetData Data
    {
        get
        {
            if (cache == null)
            {
                TryInitializeDependencies();
                cache = budgetRepository != null ? budgetRepository.Load() : new BudgetData();
            }

            return cache;
        }
    }

    // ---------- 月总预算 ----------

    /// <summary>
    /// 某月总预算（yyyy-MM），未设置返回 0
    /// </summary>
    public long GetMonthBudget(string yyyyMM)
    {
        return FindTotal(Data.MonthTotals, yyyyMM);
    }

    /// <summary>
    /// 设置某月总预算；amountFen ≤ 0 时删除该条
    /// </summary>
    public void SetMonthBudget(string yyyyMM, long amountFen)
    {
        UpsertTotal(Data.MonthTotals, yyyyMM, amountFen);
        SaveAndNotify();
    }

    // ---------- 年总预算 ----------

    /// <summary>
    /// 某年总预算（yyyy），未设置返回 0
    /// </summary>
    public long GetYearBudget(string yyyy)
    {
        return FindTotal(Data.YearTotals, yyyy);
    }

    /// <summary>
    /// 设置某年总预算；amountFen ≤ 0 时删除该条
    /// </summary>
    public void SetYearBudget(string yyyy, long amountFen)
    {
        UpsertTotal(Data.YearTotals, yyyy, amountFen);
        SaveAndNotify();
    }

    // ---------- 分类预算 ----------

    /// <summary>
    /// 某月某分类预算（yyyy-MM + 分类名），未设置返回 0
    /// </summary>
    public long GetCategoryBudget(string yyyyMM, string category)
    {
        CategoryBudgetEntry entry = FindCategory(yyyyMM, category);
        return entry != null ? entry.AmountFen : 0;
    }

    /// <summary>
    /// 设置某月某分类预算；amountFen ≤ 0 时删除该条
    /// </summary>
    public void SetCategoryBudget(string yyyyMM, string category, long amountFen)
    {
        if (string.IsNullOrWhiteSpace(yyyyMM) || string.IsNullOrWhiteSpace(category))
        {
            Debug.LogWarning("[BudgetManager] 月键/分类名为空，不保存分类预算。");
            return;
        }

        if (amountFen <= 0)
        {
            RemoveCategoryBudget(yyyyMM, category);
            return;
        }

        CategoryBudgetEntry entry = FindCategory(yyyyMM, category);

        if (entry != null)
        {
            entry.AmountFen = amountFen;
        }
        else
        {
            entry = new CategoryBudgetEntry
            {
                MonthKey = yyyyMM,
                Category = category,
                AmountFen = amountFen
            };
            Data.CategoryBudgets.Add(entry);
        }

        SaveAndNotify();
    }

    /// <summary>
    /// 删除某月某分类预算（不存在时静默）
    /// </summary>
    public void RemoveCategoryBudget(string yyyyMM, string category)
    {
        CategoryBudgetEntry entry = FindCategory(yyyyMM, category);

        if (entry == null)
        {
            return;
        }

        Data.CategoryBudgets.Remove(entry);
        SaveAndNotify();
    }

    /// <summary>
    /// 某月全部分类预算（副本，行序为设置顺序）
    /// </summary>
    public List<CategoryBudgetEntry> GetCategoryBudgets(string yyyyMM)
    {
        List<CategoryBudgetEntry> result = new List<CategoryBudgetEntry>();

        if (string.IsNullOrWhiteSpace(yyyyMM))
        {
            return result;
        }

        List<CategoryBudgetEntry> source = Data.CategoryBudgets;

        for (int i = 0; i < source.Count; i++)
        {
            if (source[i] != null && source[i].MonthKey == yyyyMM)
            {
                result.Add(source[i]);
            }
        }

        return result;
    }

    // ---------- 自检辅助 ----------

    /// <summary>
    /// 整份数据深拷贝导出（流程自检备份/还原用，JsonUtility 往返）
    /// </summary>
    public BudgetData ExportForTest()
    {
        return JsonUtility.FromJson<BudgetData>(JsonUtility.ToJson(Data));
    }

    /// <summary>
    /// 整份数据还原并落盘（流程自检结束时恢复现场）
    /// </summary>
    public void RestoreForTest(BudgetData data)
    {
        cache = data != null ? data : new BudgetData();

        if (cache.MonthTotals == null)
        {
            cache.MonthTotals = new List<BudgetEntry>();
        }

        if (cache.YearTotals == null)
        {
            cache.YearTotals = new List<BudgetEntry>();
        }

        if (cache.CategoryBudgets == null)
        {
            cache.CategoryBudgets = new List<CategoryBudgetEntry>();
        }

        SaveAndNotify();
    }

    // ---------- 内部 ----------

    private static long FindTotal(List<BudgetEntry> list, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return 0;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && list[i].Key == key)
            {
                return list[i].AmountFen;
            }
        }

        return 0;
    }

    private static void UpsertTotal(List<BudgetEntry> list, string key, long amountFen)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            Debug.LogWarning("[BudgetManager] 预算键为空，不保存。");
            return;
        }

        BudgetEntry found = null;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && list[i].Key == key)
            {
                found = list[i];
                break;
            }
        }

        if (amountFen <= 0)
        {
            if (found != null)
            {
                list.Remove(found);
            }

            return;
        }

        if (found != null)
        {
            found.AmountFen = amountFen;
        }
        else
        {
            list.Add(new BudgetEntry { Key = key, AmountFen = amountFen });
        }
    }

    private CategoryBudgetEntry FindCategory(string yyyyMM, string category)
    {
        if (string.IsNullOrWhiteSpace(yyyyMM) || string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        List<CategoryBudgetEntry> source = Data.CategoryBudgets;

        for (int i = 0; i < source.Count; i++)
        {
            if (source[i] != null && source[i].MonthKey == yyyyMM && source[i].Category == category)
            {
                return source[i];
            }
        }

        return null;
    }

    private void SaveAndNotify()
    {
        TryInitializeDependencies();

        if (budgetRepository != null)
        {
            budgetRepository.Save(Data);
        }

        OnBudgetChanged?.Invoke();
    }
}
