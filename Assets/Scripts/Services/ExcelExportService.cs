using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;

/// <summary>
/// Excel 导出转换层（步骤05，策划案 §6.1/§6.2）：记录列表 → 固定格式 xlsx。
/// 纯逻辑无 Unity 依赖，编辑器自检直接调用；EPPlus 4.5 无需 LicenseContext。
/// 表格格式 v1：单 Sheet「记账明细」，首行 5 列加粗表头；日期原样 yyyy-MM-dd 文本、
/// 类型仅「支出/收入」、金额写 double（分/100，两位小数，便于在 Excel 里求和）、备注可空；
/// 排序 Date 升序、同日 AmountFen 降序。不写汇总行/样式装饰，保证导入端解析与第三方回环稳定。
/// </summary>
public static class ExcelExportService
{
    /// <summary>
    /// Sheet 名与表头（步骤06 导入按表头逐列严格校验，两侧必须一致）
    /// </summary>
    public const string SheetName = "记账明细";
    public static readonly string[] HeaderTexts = { "日期", "类型", "分类", "金额(元)", "备注" };

    /// <summary>
    /// 把记录写成 xlsx。成功 true；失败不产出半截文件，message 带原因。
    /// </summary>
    public static bool WriteExcel(List<AccountRecord> records, string fullPath, out string message)
    {
        message = string.Empty;

        if (records == null || records.Count == 0)
        {
            message = "没有可导出的记录";
            return false;
        }

        if (string.IsNullOrWhiteSpace(fullPath))
        {
            message = "导出路径为空";
            return false;
        }

        List<AccountRecord> sorted = new List<AccountRecord>(records);
        sorted.Sort(CompareForExport);

        try
        {
            // using 保证释放，否则 ExcelPackage 锁文件导致二次导出失败（步骤05 §4）
            using (ExcelPackage package = new ExcelPackage(new FileInfo(fullPath)))
            {
                ExcelWorksheet sheet = package.Workbook.Worksheets.Add(SheetName);

                for (int col = 0; col < HeaderTexts.Length; col++)
                {
                    sheet.Cells[1, col + 1].Value = HeaderTexts[col];
                }

                for (int i = 0; i < sorted.Count; i++)
                {
                    AccountRecord record = sorted[i];
                    int row = i + 2;

                    sheet.Cells[row, 1].Value = record.Date;
                    sheet.Cells[row, 2].Value = record.Type == (int)RecordType.Income ? "收入" : "支出";
                    sheet.Cells[row, 3].Value = record.Category;
                    sheet.Cells[row, 4].Value = record.AmountFen / 100.0;
                    sheet.Cells[row, 4].Style.Numberformat.Format = "0.00";
                    sheet.Cells[row, 5].Value = record.Note;
                }

                sheet.Cells[1, 1, 1, HeaderTexts.Length].Style.Font.Bold = true;

                // 列宽只能写死：AutoFitColumns 内部走 System.Drawing 的 GDI+，
                // Android 真机没有 libgdiplus，类型初始化直接抛异常（编辑器 Windows 上验证不出来）
                sheet.Column(1).Width = 12;   // 日期 yyyy-MM-dd
                sheet.Column(2).Width = 8;    // 类型 支出/收入
                sheet.Column(3).Width = 12;   // 分类
                sheet.Column(4).Width = 12;   // 金额(元)
                sheet.Column(5).Width = 30;   // 备注

                package.Save();
            }

            message = "导出完成";
            return true;
        }
        catch (Exception ex)
        {
            message = "写文件失败: " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Date 升序、同日 AmountFen 降序（策划案 §6.1 阅读友好要求）
    /// </summary>
    private static int CompareForExport(AccountRecord a, AccountRecord b)
    {
        int dateCompare = string.CompareOrdinal(a.Date, b.Date);

        if (dateCompare != 0)
        {
            return dateCompare;
        }

        return b.AmountFen.CompareTo(a.AmountFen);
    }
}
