using System;
using System.IO;
using UnityEngine;

public class JsonFileService : MonoBehaviour
{
    /// <summary>
    /// 将文件名转换为实际保存路径。
    /// </summary>
    public string GetFullPath(string fileName)
    {
        return Path.Combine(Application.persistentDataPath, fileName);
    }

    /// <summary>
    /// JSON 文件 保存
    /// </summary>
    public void SaveToJson<T>(string fileName, T data, bool prettyPrint = true)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            Debug.LogWarning("[JsonFileService] fileName 为空。");
            return;
        }

        if (data == null)
        {
            Debug.LogWarning("[JsonFileService] 要保存的 data 为 null。");
            return;
        }

        try
        {
            string fullPath = GetFullPath(fileName);
            string json = JsonUtility.ToJson(data, prettyPrint);

            File.WriteAllText(fullPath, json);
            Debug.Log($"[JsonFileService] 保存完成: {fullPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[JsonFileService] 保存失败: {ex.Message}");
        }
    }

    /// <summary>
    /// JSON 文件 读取
    /// 文件不存在或失败时返回 new T()
    /// </summary>
    public T LoadFromJson<T>(string fileName) where T : new()
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            Debug.LogWarning("[JsonFileService] fileName 为空。");
            return new T();
        }

        try
        {
            string fullPath = GetFullPath(fileName);

            if (!File.Exists(fullPath))
            {
                return new T();
            }

            string json = File.ReadAllText(fullPath);

            if (string.IsNullOrWhiteSpace(json))
            {
                return new T();
            }

            T loadedData = JsonUtility.FromJson<T>(json);

            if (loadedData == null)
            {
                return new T();
            }

            return loadedData;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[JsonFileService] 读取失败: {ex.Message}");
            return new T();
        }
    }

    /// <summary>
    /// 检查文件是否存在
    /// </summary>
    public bool Exists(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string fullPath = GetFullPath(fileName);
        return File.Exists(fullPath);
    }

    /// <summary>
    /// 文件 删除
    /// </summary>
    public void DeleteFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            string fullPath = GetFullPath(fileName);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                Debug.Log($"[JsonFileService] 文件 删除 完成: {fullPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[JsonFileService] 文件 删除 失败: {ex.Message}");
        }
    }
}
