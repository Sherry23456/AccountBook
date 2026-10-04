using System;
using System.Collections.Generic;

/// <summary>
/// 预算数据模型（步骤09）：月总预算 / 年总预算 / 分类预算三张表，随 budgets.json 落盘。
/// 金额一律 long 分（步骤02 §4 全程禁 float 约定）；月键 "yyyy-MM"，年键 "yyyy"。
/// </summary>
[Serializable]
public class BudgetEntry
{
    /// <summary>月总预算 "yyyy-MM"；年总预算 "yyyy"</summary>
    public string Key;

    /// <summary>预算金额（分）</summary>
    public long AmountFen;
}

[Serializable]
public class CategoryBudgetEntry
{
    /// <summary>归属月份 "yyyy-MM"</summary>
    public string MonthKey;

    /// <summary>分类名（与 CategoryTable.Name 对应，只支持支出分类）</summary>
    public string Category;

    /// <summary>预算金额（分）</summary>
    public long AmountFen;
}

[Serializable]
public class BudgetData
{
    public List<BudgetEntry> MonthTotals = new List<BudgetEntry>();
    public List<BudgetEntry> YearTotals = new List<BudgetEntry>();
    public List<CategoryBudgetEntry> CategoryBudgets = new List<CategoryBudgetEntry>();
}
