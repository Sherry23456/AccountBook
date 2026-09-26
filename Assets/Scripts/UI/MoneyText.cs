using UnityEngine;

/// <summary>
/// 金额显示统一工具（步骤03 §2.2）：全部由分（long）拼字符串，禁 float（步骤02 §4 同源约定）。
/// 流水行：支出 -10.30（黑）/ 收入 +600.00（强调橙黄）；组头当日支出：去尾零无符号（"支出: 28.8"）。
/// </summary>
public static class MoneyText
{
    /// <summary>
    /// 支出金额色（近黑）
    /// </summary>
    public static readonly Color ColorExpense = new Color32(0x22, 0x22, 0x22, 0xFF);

    /// <summary>
    /// 收入金额强调色（#FFA000，白底上比主黄更醒目）
    /// </summary>
    public static readonly Color ColorIncome = new Color32(0xFF, 0xA0, 0x00, 0xFF);

    /// <summary>
    /// 分 → 带符号两位小数：支出 "-10.30"，收入 "+600.00"
    /// </summary>
    public static string Format(long fen, RecordType type)
    {
        string sign = type == RecordType.Income ? "+" : "-";
        return sign + FormatYuan(fen);
    }

    /// <summary>
    /// 分 → 无符号两位小数："54.10"（页眉本月合计用，收支正数累计）
    /// </summary>
    public static string FormatYuan(long fen)
    {
        long yuan = fen / 100;
        long fenRemainder = System.Math.Abs(fen % 100);
        return yuan.ToString() + "." + fenRemainder.ToString("D2");
    }

    /// <summary>
    /// 分 → 无符号去尾零："28.80"→"28.8"，"600.00"→"600"（组头当日支出合计用，录屏同款）
    /// </summary>
    public static string FormatTrim(long fen)
    {
        string text = FormatYuan(fen);

        if (text.Contains("."))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text;
    }

    /// <summary>
    /// 流水行金额颜色：收入强调色，支出版近黑
    /// </summary>
    public static Color GetColor(RecordType type)
    {
        return type == RecordType.Income ? ColorIncome : ColorExpense;
    }
}
