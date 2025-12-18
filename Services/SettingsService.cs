
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SimplePRClient.Services;

public class AppSettings
{
    public List<string> RecentRepositories { get; set; } = new();
    
    /// @brief 保護ブランチへのPush警告をスキップするリポジトリ+ブランチのリスト
    /// 形式: "リポジトリパス|ブランチ名" (例: "C:\repo|main")
    public List<string> SkipProtectedBranchWarning { get; set; } = new();
}

public class SettingsService
{
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SimplePRClient",
        "settings.json");

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // Ignore load errors, start fresh
            Settings = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFile);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(Settings);
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    public void AddRecentRepository(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        // Normalize path
        var normalized = Path.GetFullPath(path);

        // Remove existing to re-insert at top
        Settings.RecentRepositories.RemoveAll(r => string.Equals(r, normalized, StringComparison.OrdinalIgnoreCase));
        
        // Insert at top
        Settings.RecentRepositories.Insert(0, normalized);

        // Limit to 10
        if (Settings.RecentRepositories.Count > 10)
        {
            Settings.RecentRepositories = Settings.RecentRepositories.Take(10).ToList();
        }

        Save();
    }

    /// @brief 指定のリポジトリ+ブランチで保護ブランチ警告をスキップするかどうかを確認
    /// @param repoPath リポジトリパス
    /// @param branch ブランチ名
    /// @return スキップ設定がある場合true
    public bool ShouldSkipProtectedBranchWarning(string repoPath, string branch)
    {
        if (string.IsNullOrEmpty(repoPath) || string.IsNullOrEmpty(branch)) return false;
        var key = CreateProtectedBranchKey(repoPath, branch);
        return Settings.SkipProtectedBranchWarning.Any(k => 
            string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
    }

    /// @brief 指定のリポジトリ+ブランチで保護ブランチ警告をスキップする設定を追加
    /// @param repoPath リポジトリパス
    /// @param branch ブランチ名
    public void AddSkipProtectedBranchWarning(string repoPath, string branch)
    {
        if (string.IsNullOrEmpty(repoPath) || string.IsNullOrEmpty(branch)) return;
        var key = CreateProtectedBranchKey(repoPath, branch);
        
        if (!Settings.SkipProtectedBranchWarning.Any(k => 
            string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
        {
            Settings.SkipProtectedBranchWarning.Add(key);
            Save();
        }
    }

    private static string CreateProtectedBranchKey(string repoPath, string branch)
    {
        var normalized = Path.GetFullPath(repoPath);
        return $"{normalized}|{branch}";
    }
}
