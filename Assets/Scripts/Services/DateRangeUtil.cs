using System;
using System.Globalization;

/// <summary>
/// 公共周期工具（步骤04 §2.1）：周/月/年三档区间计算与平移，统一输出首尾两个
/// yyyy-MM-dd 字符串，直接喂给 AccountRepository.GetRecordsInRange；图表页与
/// 步骤 05/06 导入导出共用。周为周一~周日：((int)DayOfWeek + 6) % 7 为距周一的天数。
/// </summary>
public static class DateRangeUtil
{
    /// <summary>
    /// anyDay 所在周的周一~周日
    /// </summary>
    public static void GetWeekRange(DateTime anyDay, out string start, out string end)
    {
        DateTime monday = anyDay.Date.AddDays(-(((int)anyDay.DayOfWeek + 6) % 7));
        start = Format(monday);
        end = Format(monday.AddDays(6));
    }

    /// <summary>
    /// 自然月：月末用 DateTime.DaysInMonth（2 月 28/29 自适应）
    /// </summary>
    public static void GetMonthRange(int year, int month, out string start, out string end)
    {
        start = Format(new DateTime(year, month, 1));
        end = Format(new DateTime(year, month, DateTime.DaysInMonth(year, month)));
    }

    /// <summary>
    /// 自然年
    /// </summary>
    public static void GetYearRange(int year, out string start, out string end)
    {
        start = Format(new DateTime(year, 1, 1));
        end = Format(new DateTime(year, 12, 31));
    }

    /// <summary>
    /// 周期平移：offset=0 为 focus 所在周期（本期），-1 上一周期，+1 下一周期
    /// </summary>
    public static void ShiftRange(ExportRange range, DateTime focus, int offset, out string start, out string end)
    {
        switch (range)
        {
            case ExportRange.Week:
                GetWeekRange(focus.AddDays(offset * 7), out start, out end);
                break;

            case ExportRange.Month:
                DateTime month = focus.AddMonths(offset);
                GetMonthRange(month.Year, month.Month, out start, out end);
                break;

            case ExportRange.Year:
                GetYearRange(focus.AddYears(offset).Year, out start, out end);
                break;

            default:
                start = string.Empty;
                end = string.Empty;
                break;
        }
    }

    private static string Format(DateTime date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
