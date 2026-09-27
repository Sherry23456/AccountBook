using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;

/// <summary>
/// 一次 ReadExcel 的解析产物：Valid 为可入库记录（每条全新 Id），
/// Errors 为行级错误（"第N行：原因"，不中断整文件），TotalRows 为有效数据行数（全空行不计）。
/// </summary>
public class ImportParseResult
{
    /// <summary>
    /// 解析成功的记录
    /// </summary>
    public List<AccountRecord> Valid = new List<AccountRecord>();

    /// <summary>
    /// 行级错误原因（前缀"第N行："）
    /// </summary>
    public List<string> Errors = new List<string>();

    /// <summary>
    /// 有效数据行数（第 2 行起非全空行）
    /// </summary>
    public int TotalRows;
}

/// <summary>
/// Excel 导入转换层（步骤06，策划案 §6.3）：xlsx → 记录列表。纯逻辑无 Unity 依赖，自检直接调用。
/// 表头按 ExcelExportService.HeaderTexts 逐列严格校验；日期三形态（DateTime / 序列数 double / yyyy-MM-dd 文本）
/// 都要兜住（Excel 单元格显示日期但底层常是序列数）；类型仅「支出/收入」；金额恒为正、四舍五入到分；
/// 分类不在 CategoryTable 时归「其他」不报错（不丢数据）；备注可空；Id 不读——v1 格式无 Id 列，
/// 导入记录一律全新 Id，去重由 ExcelTransferManager 用三元组在入库前完成。
/// </summary>
public static class ExcelImportService
{
    /// <summary>
    /// 读取并解析 xlsx。成功 true（含 0 行空表，Valid/Errors 空）；表头不符/文件问题返回 false，message 带原因。
    /// </summary>
    public static bool ReadExcel(string path, out ImportParseResult result, out string message)
    {
        result = null;
        message = string.Empty;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            message = "文件不存在或无法访问";
            return false;
        }

        try
        {
            // using 保证释放，否则包句柄锁文件导致二次读取/删除失败（步骤05 §4 同坑）
            using (ExcelPackage package = new ExcelPackage(new FileInfo(path)))
            {
                ExcelWorksheet sheet = FindFirstNonEmptySheet(package);

                if (sheet == null)
                {
                    message = "表格内容为空";
                    return false;
                }

                for (int col = 1; col <= ExcelExportService.HeaderTexts.Length; col++)
                {
                    if (sheet.Cells[1, col].Text?.Trim() != ExcelExportService.HeaderTexts[col - 1])
                    {
                        message = "表格格式不符";
                        return false;
                    }
                }

                result = new ImportParseResult();
                int lastRow = sheet.Dimension.End.Row;

                for (int row = 2; row <= lastRow; row++)
                {
                    object amountValue = sheet.Cells[row, 4].Value;
                    string dateText = sheet.Cells[row, 1].Text?.Trim();
                    string typeText = sheet.Cells[row, 2].Text?.Trim();
                    string categoryText = sheet.Cells[row, 3].Text?.Trim();
                    string noteText = sheet.Cells[row, 5].Text;

                    if (IsEmptyRow(amountValue, dateText, typeText, categoryText, noteText))
                    {
                        continue;
                    }

                    result.TotalRows++;

                    if (!TryParseDate(sheet.Cells[row, 1].Value, dateText, out string date))
                    {
                        result.Errors.Add($"第{row}行：日期无法识别（{dateText}）");
                        continue;
                    }

                    int type;

                    if (typeText == "支出")
                    {
                        type = (int)RecordType.Expense;
                    }
                    else if (typeText == "收入")
                    {
                        type = (int)RecordType.Income;
                    }
                    else
                    {
                        result.Errors.Add($"第{row}行：类型不是 支出/收入（{typeText}）");
                        continue;
                    }

                    if (!TryParseAmountFen(amountValue, out long amountFen))
                    {
                        result.Errors.Add($"第{row}行：金额不是正数");
                        continue;
                    }

                    // 不在分类表的分类归「其他」，不丢数据也不报错（步骤06 §2.2）
                    string category = CategoryTable.Contains(categoryText) ? categoryText : "其他";

                    result.Valid.Add(new AccountRecord
                    {
                        Type = type,
                        Category = category,
                        AmountFen = amountFen,
                        Date = date,
                        Note = noteText ?? string.Empty,
                    });
                }

                return true;
            }
        }
        catch (Exception ex)
        {
            result = null;
            message = "读取失败: " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 首个有内容的 worksheet（策划案 §6.3：取首个非空 worksheet）
    /// </summary>
    private static ExcelWorksheet FindFirstNonEmptySheet(ExcelPackage package)
    {
        for (int i = 1; i <= package.Workbook.Worksheets.Count; i++)
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets[i];

            if (sheet != null && sheet.Dimension != null && sheet.Dimension.End.Row >= 1)
            {
                return sheet;
            }
        }

        return null;
    }

    /// <summary>
    /// 五列全空视为空行（第三方文件常见尾随空行），静默跳过不计 TotalRows
    /// </summary>
    private static bool IsEmptyRow(object amountValue, string dateText, string typeText,
        string categoryText, string noteText)
    {
        return amountValue == null &&
               string.IsNullOrEmpty(dateText) &&
               string.IsNullOrEmpty(typeText) &&
               string.IsNullOrEmpty(categoryText) &&
               string.IsNullOrEmpty(noteText);
    }

    /// <summary>
    /// 日期三形态（步骤06 §4 常见坑）：单元格底层 DateTime / 日期序列数 double / yyyy-MM-dd 文本。
    /// Value 是日期序列数时 Text 会给出格式化文本，但统一以 Value 优先、文本兜底。
    /// </summary>
    private static bool TryParseDate(object value, string displayText, out string date)
    {
        date = string.Empty;
        DateTime parsed;

        if (value is DateTime dateTime)
        {
            date = dateTime.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }

        if (value is double serial)
        {
            // 序列数是 1899-12-30 起算的 OADate（步骤06 §4：double 45361 之类）
            date = DateTime.FromOADate(serial).Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }

        string text = value as string;

        if (string.IsNullOrWhiteSpace(text))
        {
            text = displayText;
        }

        if (!string.IsNullOrWhiteSpace(text) &&
            DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out parsed))
        {
            date = parsed.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 金额：double/decimal/int 直取，文本 InvariantCulture 解析；四舍五入到分，须为正数
    /// </summary>
    private static bool TryParseAmountFen(object value, out long amountFen)
    {
        amountFen = 0;

        double amount;

        if (value is double d)
        {
            amount = d;
        }
        else if (value is decimal m)
        {
            amount = (double)m;
        }
        else if (value is int i)
        {
            amount = i;
        }
        else
        {
            string text = value as string;

            if (string.IsNullOrWhiteSpace(text) ||
                !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
            {
                return false;
            }
        }

        // 防溢出/NaN：金额上限 1e12 元，远超真实账目
        if (!IsFinite(amount) || amount <= 0 || amount > 1e12)
        {
            return false;
        }

        amountFen = (long)Math.Round(amount * 100, MidpointRounding.AwayFromZero);
        return amountFen > 0;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
