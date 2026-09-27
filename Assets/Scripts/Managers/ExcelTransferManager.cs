using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Excel 导出总流程（步骤05，策划案 §6.2，骨架照搬 QuikDelivery BackupDataManager）：
/// 周期查询（AccountManager.GetRecordsInRange，与图表页同口径）→ 固定文件名 →
/// 先写 persistentDataPath 临时文件 → 复制到 Download（安卓直写失败走 MediaStore 兜底）→
/// 清理临时文件。导入半边（步骤06）后续在此类追加 ImportExcel。
/// </summary>
public class ExcelTransferManager : MonoBehaviour
{
    /// <summary>
    /// xlsx 的 MIME 类型（MediaStore 兜底写入时登记）
    /// </summary>
    private const string XlsxMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [Header("Dependencies")]
    [SerializeField] private AccountManager accountManager;

    private void Awake()
    {
        TryInitializeDependencies();
    }

    /// <summary>
    /// 外部引用为空时自动查找
    /// </summary>
    private void TryInitializeDependencies()
    {
        if (accountManager == null)
        {
            accountManager = FindFirstObjectByType<AccountManager>();
        }
    }

    /// <summary>
    /// 导出指定周期账目到 Download。
    /// 成功：fileName=文件名，fullPath=实际保存全路径，resultMessage="导出完成: 文件名"；
    /// 失败（空周期/写失败/复制失败）返回 false，resultMessage 带原因。
    /// </summary>
    public bool ExportExcel(ExportRange range, string startDate, string endDate,
        out string fileName, out string fullPath, out string resultMessage)
    {
        fileName = string.Empty;
        fullPath = string.Empty;
        resultMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
        {
            resultMessage = "导出周期为空";
            return false;
        }

        TryInitializeDependencies();

        if (accountManager == null)
        {
            resultMessage = "AccountManager 引用缺失";
            return false;
        }

        List<AccountRecord> records = accountManager.GetRecordsInRange(startDate, endDate);

        if (records == null || records.Count == 0)
        {
            resultMessage = "本周期没有记录";
            return false;
        }

        fileName = BuildFileName(range, startDate, endDate);

        // 先落 persistentDataPath（安卓必然可写），再复制到 Download
        string tempFullPath = Path.Combine(Application.persistentDataPath, fileName);

        if (!ExcelExportService.WriteExcel(records, tempFullPath, out string writeMessage))
        {
            resultMessage = writeMessage;
            return false;
        }

        if (TryCopyFileToDownloads(tempFullPath, fileName, out fullPath, out string copyError))
        {
            TryDeleteQuietly(tempFullPath);
            resultMessage = $"导出完成: {fileName}";
            Debug.Log($"[ExcelTransferManager] {resultMessage} / 路径: {fullPath}");
            return true;
        }

        TryDeleteQuietly(tempFullPath);
        resultMessage = copyError;
        Debug.LogWarning($"[ExcelTransferManager] 导出失败: {resultMessage}");
        return false;
    }

    /// <summary>
    /// 文件名规则（策划案 §6.1）：周 AccountBook_W_起_止 / 月 AccountBook_M_2026-09 / 年 AccountBook_Y_2026
    /// </summary>
    public static string BuildFileName(ExportRange range, string startDate, string endDate)
    {
        switch (range)
        {
            case ExportRange.Week:
                return $"AccountBook_W_{startDate}_{endDate}.xlsx";

            case ExportRange.Month:
                return startDate.Length >= 7
                    ? $"AccountBook_M_{startDate.Substring(0, 7)}.xlsx"
                    : $"AccountBook_M_{startDate}.xlsx";

            case ExportRange.Year:
                return startDate.Length >= 4
                    ? $"AccountBook_Y_{startDate.Substring(0, 4)}.xlsx"
                    : $"AccountBook_Y_{startDate}.xlsx";

            default:
                return "AccountBook_Unknown.xlsx";
        }
    }

    // ---------- Download 写入（照搬 BackupDataManager，仅 MIME 改 xlsx） ----------

    /// <summary>
    /// 把临时文件复制到 Download：先普通复制，Android 上失败再用 MediaStore 尝试一次。
    /// </summary>
    private bool TryCopyFileToDownloads(string sourceFullPath, string fileName, out string destinationFullPath, out string errorMessage)
    {
        destinationFullPath = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(sourceFullPath) || !File.Exists(sourceFullPath))
        {
            errorMessage = "没有可导出的临时文件";
            return false;
        }

        string downloadDirectory = GetDownloadDirectoryPath();
        string directDestinationPath = Path.Combine(downloadDirectory, fileName);

        if (TryCopyFileDirectly(sourceFullPath, directDestinationPath, out string directError))
        {
            destinationFullPath = directDestinationPath;
            return true;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        byte[] fileBytes = File.ReadAllBytes(sourceFullPath);

        if (TrySaveBytesToAndroidDownloads(fileName, fileBytes, out string mediaStorePath, out string mediaStoreError))
        {
            destinationFullPath = mediaStorePath;
            Debug.Log("[ExcelTransferManager] 直写 Download 失败，MediaStore 兜底成功");
            return true;
        }

        errorMessage = $"{directError} / MediaStore 失败: {mediaStoreError}";
        return false;
#else
        errorMessage = directError;
        return false;
#endif
    }

    /// <summary>
    /// 普通文件复制方式写入 Download 文件夹
    /// </summary>
    private bool TryCopyFileDirectly(string sourceFullPath, string destinationFullPath, out string errorMessage)
    {
        errorMessage = string.Empty;

        try
        {
            string targetDirectory = Path.GetDirectoryName(destinationFullPath);

            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(sourceFullPath, destinationFullPath, true);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// 使用 Android MediaStore 保存到 Download 文件夹（Android 10+ 分区存储直写被拒时兜底）
    /// </summary>
    private bool TrySaveBytesToAndroidDownloads(string fileName, byte[] fileBytes, out string savedPath, out string errorMessage)
    {
        savedPath = string.Empty;
        errorMessage = string.Empty;

        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
            using (AndroidJavaClass mediaStoreDownloads = new AndroidJavaClass("android.provider.MediaStore$Downloads"))
            using (AndroidJavaObject downloadsUri = mediaStoreDownloads.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
            using (AndroidJavaObject contentValues = new AndroidJavaObject("android.content.ContentValues"))
            using (AndroidJavaClass mediaColumns = new AndroidJavaClass("android.provider.MediaStore$MediaColumns"))
            {
                string displayNameKey = mediaColumns.GetStatic<string>("DISPLAY_NAME");
                string mimeTypeKey = mediaColumns.GetStatic<string>("MIME_TYPE");
                string relativePathKey = mediaColumns.GetStatic<string>("RELATIVE_PATH");

                contentValues.Call("put", displayNameKey, fileName);
                contentValues.Call("put", mimeTypeKey, XlsxMimeType);
                contentValues.Call("put", relativePathKey, "Download/");

                using (AndroidJavaObject uri = resolver.Call<AndroidJavaObject>("insert", downloadsUri, contentValues))
                {
                    if (uri == null)
                    {
                        errorMessage = "Download URI 生成失败";
                        return false;
                    }

                    using (AndroidJavaObject outputStream = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
                    {
                        if (outputStream == null)
                        {
                            errorMessage = "OutputStream 生成失败";
                            return false;
                        }

                        outputStream.Call("write", fileBytes);
                        outputStream.Call("flush");
                        outputStream.Call("close");
                    }
                }

                savedPath = $"Download/{fileName}";
                return true;
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }
#endif

    /// <summary>
    /// 按平台返回 Download 文件夹路径（编辑器为本机用户 Downloads，真机为公共 Download）
    /// </summary>
    private static string GetDownloadDirectoryPath()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return "/storage/emulated/0/Download";
#else
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
#endif
    }

    /// <summary>
    /// 删除临时文件，失败不影响导出结果
    /// </summary>
    private static void TryDeleteQuietly(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ExcelTransferManager] 临时文件清理失败: {fullPath} / {ex.Message}");
        }
    }
}
