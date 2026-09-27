using System;
using System.Collections.Generic;
using System.Globalization;

public class CategoryRank
{
    public string Category = string.Empty;

    /// <summary>
    /// 金额，单位：分
    /// </summary>
    public long AmountFen = 0;

    /// <summary>
    /// 占当前类型周期总额的比例 0~1
    /// </summary>
    public float Ratio = 0f;
}

public class ChartResult
{
    /// <summary>
    /// 折线 Y 值（分）：周=7 点 / 月=当月天数点 / 年=12 点
    /// </summary>
    public List<long> Points = new List<long>();

    /// <summary>
    /// X 轴标签，与 Points 等长；空串 = 该点不显示文本（月视图抽稀）
    /// </summary>
    public List<string> XLabels = new List<string>();

    public long TotalFen = 0;
    public long AvgFen = 0;
    public long MaxFen = 0;
    public int MaxIndex = -1;

    /// <summary>
    /// 归一化档位上限：数据最大值向上取整到 1/2/5×10ⁿ 元，折线按它缩放
    /// </summary>
    public long ScaleMaxFen = 0;

    /// <summary>
    /// 分类排行：金额降序，Top5 之外的合并为"其他"
    /// </summary>
    public List<CategoryRank> Ranks = new List<CategoryRank>();

    public string StartDate = string.Empty;
    public string EndDate = string.Empty;
}

/// <summary>
/// 图表聚合器（步骤04 §2.3）：区间内指定类型的记录按 周=日 / 月=日 / 年=月 分桶累加，
/// 输出折线点、X 轴标签（月视图每隔 5 天抽稀）、总额/均值/最大值与分类 Top5 排行。
/// 全程 long 分运算；聚合键一律 record.Date——补记账按账目日期计入而非补记当天（步骤04 §4）。
/// </summary>
public static class ChartCalculator
{
    private const int TopCount = 5;
    private const string OtherCategory = "其他";

    /// <summary>
    /// 聚合主入口：records 一般已按区间查好，这里再按 Date 复核一次边界（字符串比较即时间序）
    /// </summary>
    public static ChartResult Calculate(List<AccountRecord> records, string startDate, string endDate,
        ExportRange range, RecordType type)
    {
        ChartResult result = new ChartResult();
        result.StartDate = startDate ?? string.Empty;
        result.EndDate = endDate ?? string.Empty;

        int pointCount = GetPointCount(range, result.StartDate);

        if (pointCount <= 0 || !TryParseDate(result.StartDate, out DateTime start))
        {
            return result;
        }

        result.Points.AddRange(new long[pointCount]);
        result.XLabels.AddRange(new string[pointCount]);

        Dictionary<string, long> categorySum = new Dictionary<string, long>();

        if (records != null)
        {
            for (int i = 0; i < records.Count; i++)
            {
                AccountRecord record = records[i];

                if (record == null || record.Type != (int)type)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(record.Date) ||
                    record.Date.CompareTo(result.StartDate) < 0 ||
                    record.Date.CompareTo(result.EndDate) > 0)
                {
                    continue;
                }

                int bucket = GetBucketIndex(record.Date, start, range);

                if (bucket < 0 || bucket >= pointCount)
                {
                    continue;
                }

                result.Points[bucket] += record.AmountFen;
                result.TotalFen += record.AmountFen;

                string category = string.IsNullOrEmpty(record.Category) ? OtherCategory : record.Category;

                if (categorySum.ContainsKey(category))
                {
                    categorySum[category] += record.AmountFen;
                }
                else
                {
                    categorySum[category] = record.AmountFen;
                }
            }
        }

        BuildXLabels(result, start, range);
        BuildStats(result);
        BuildRanks(result, categorySum);
        return result;
    }

    /// <summary>
    /// 折线点数：周固定 7，年固定 12，月 = 当月天数（2 月 28/29 自适应）
    /// </summary>
    public static int GetPointCount(ExportRange range, string startDate)
    {
        if (range == ExportRange.Week)
        {
            return 7;
        }

        if (range == ExportRange.Year)
        {
            return 12;
        }

        if (TryParseDate(startDate, out DateTime start))
        {
            return DateTime.DaysInMonth(start.Year, start.Month);
        }

        return 0;
    }

    private static int GetBucketIndex(string recordDate, DateTime start, ExportRange range)
    {
        if (!TryParseDate(recordDate, out DateTime date))
        {
            return -1;
        }

        if (range == ExportRange.Year)
        {
            return date.Month - 1;
        }

        return (date.Date - start.Date).Days;
    }

    /// <summary>
    /// 周标签 7 个全显 "MM-dd"；月标签只留 1/6/11/16/21/26/31（每隔 5 天，防挤爆）；
    /// 年标签 "1月".."12月"
    /// </summary>
    private static void BuildXLabels(ChartResult result, DateTime start, ExportRange range)
    {
        if (range == ExportRange.Week)
        {
            for (int i = 0; i < result.XLabels.Count; i++)
            {
                result.XLabels[i] = start.AddDays(i).ToString("MM-dd", CultureInfo.InvariantCulture);
            }
        }
        else if (range == ExportRange.Month)
        {
            for (int i = 0; i < result.XLabels.Count; i++)
            {
                result.XLabels[i] = i % 5 == 0
                    ? (i + 1).ToString("D2", CultureInfo.InvariantCulture)
                    : string.Empty;
            }
        }
        else
        {
            for (int i = 0; i < result.XLabels.Count; i++)
            {
                result.XLabels[i] = (i + 1) + "月";
            }
        }
    }

    private static void BuildStats(ChartResult result)
    {
        for (int i = 0; i < result.Points.Count; i++)
        {
            if (result.Points[i] > result.MaxFen)
            {
                result.MaxFen = result.Points[i];
                result.MaxIndex = i;
            }
        }

        if (result.Points.Count > 0)
        {
            result.AvgFen = result.TotalFen / result.Points.Count;
        }

        result.ScaleMaxFen = NiceCeilFen(result.MaxFen);
    }

    /// <summary>
    /// 数据最大值向上取整档位：取 1/2/5×10ⁿ 元（100 分起步）中最小的不小于 maxFen 的值
    /// </summary>
    public static long NiceCeilFen(long maxFen)
    {
        if (maxFen <= 0)
        {
            return 0;
        }

        long step = 100;
        int phase = 0;

        while (step < maxFen)
        {
            step = phase % 3 == 1 ? step * 5 / 2 : step * 2;
            phase++;
        }

        return step;
    }

    private static void BuildRanks(ChartResult result, Dictionary<string, long> categorySum)
    {
        List<CategoryRank> sorted = new List<CategoryRank>();

        foreach (KeyValuePair<string, long> pair in categorySum)
        {
            sorted.Add(new CategoryRank { Category = pair.Key, AmountFen = pair.Value });
        }

        // 金额降序；同额按分类名升序，保证自检与显示一致
        sorted.Sort((a, b) =>
        {
            int byAmount = b.AmountFen.CompareTo(a.AmountFen);
            return byAmount != 0 ? byAmount : string.CompareOrdinal(a.Category, b.Category);
        });

        float total = result.TotalFen;

        if (sorted.Count <= TopCount)
        {
            for (int i = 0; i < sorted.Count; i++)
            {
                sorted[i].Ratio = total > 0f ? sorted[i].AmountFen / total : 0f;
                result.Ranks.Add(sorted[i]);
            }

            return;
        }

        for (int i = 0; i < TopCount; i++)
        {
            sorted[i].Ratio = total > 0f ? sorted[i].AmountFen / total : 0f;
            result.Ranks.Add(sorted[i]);
        }

        long otherFen = 0;

        for (int i = TopCount; i < sorted.Count; i++)
        {
            otherFen += sorted[i].AmountFen;
        }

        // Top5 里本身有"其他"时并入，避免出现两行"其他"
        CategoryRank existing = result.Ranks.Find(rank => rank.Category == OtherCategory);

        if (existing != null)
        {
            existing.AmountFen += otherFen;
            existing.Ratio = total > 0f ? existing.AmountFen / total : 0f;
        }
        else
        {
            result.Ranks.Add(new CategoryRank
            {
                Category = OtherCategory,
                AmountFen = otherFen,
                Ratio = total > 0f ? otherFen / total : 0f
            });
        }
    }

    private static bool TryParseDate(string text, out DateTime date)
    {
        return DateTime.TryParseExact(text ?? string.Empty, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
