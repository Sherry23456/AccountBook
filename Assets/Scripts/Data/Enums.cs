/// <summary>
/// 记录类型：0=支出，1=收入（策划案 §4.1）
/// </summary>
public enum RecordType
{
    Expense = 0,
    Income = 1
}

/// <summary>
/// 导出范围档位：周/月/年（策划案 §4.3，导出/导入/图表共用）
/// </summary>
public enum ExportRange
{
    Week = 0,
    Month = 1,
    Year = 2
}
