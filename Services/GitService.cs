namespace SimplePRClient.Services;

using SimplePRClient.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// @brief Git操作を提供するサービスクラス
/// 作成者: 山内陽
public class GitService
{
    private readonly ProcessRunner _runner;
    private string _repoPath = string.Empty;

    public GitService(ProcessRunner runner)
    {
        _runner = runner;
    }

    public void SetRepository(string path)
    {
        _repoPath = path;
    }

    public string CurrentRepositoryPath => _repoPath;
    
    public string GetInternalRepoPath() => _repoPath;

    /// @brief 現在のディレクトリから上位に .git を探す
    /// @param startPath 探索開始ディレクトリのパス
    /// @return .git のあるルートディレクトリパス。見つからない場合は null
    public static string? FindGitRoot(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }

    /// @brief git status --porcelain=v1 -b を実行し、ファイル変更一覧とブランチ情報を取得する
    /// @param ct キャンセルトークン
    /// @return 変更リスト、ブランチ名、Upstream名、Dirtyフラグのタプル
    public async Task<(List<FileChangeEntry> Changes, string Branch, string Upstream, bool IsDirty)> GetStatusAsync(CancellationToken ct = default)
    {
        // Force unquoted paths for non-ASCII characters
        var result = await _runner.RunAsync("git", "-c core.quotePath=false status --porcelain=v1 -b", _repoPath, ct);
        
        var changes = new List<FileChangeEntry>();
        string branch = "HEAD";
        string upstream = "";
        bool isDirty = false;

        if (!result.Success) return (changes, branch, upstream, isDirty);

        var lines = result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.Length < 3) continue;

            if (line.StartsWith("##"))
            {
                // Branch info: ## main...origin/main
                var branchInfo = line.Substring(3).Trim();
                var parts = branchInfo.Split(new[] { "..." }, StringSplitOptions.None);
                branch = parts[0];
                if (parts.Length > 1)
                {
                    upstream = parts[1].Split(' ')[0]; // remove ahead/behind info if any
                }
                continue;
            }

            char indexStatus = line[0];
            char workTreeStatus = line[1];
            string path = line.Substring(3).Trim();

            // Ignore rename info for now, simple path extraction
            if (path.Contains(" -> "))
            {
                path = path.Split(new[] { " -> " }, StringSplitOptions.None)[1];
            }

            // Remove quotes if present
            path = path.Trim('"');

            changes.Add(new FileChangeEntry(path, indexStatus, workTreeStatus));

            if (indexStatus != '?' && indexStatus != ' ') isDirty = true; // Staged changes
            if (workTreeStatus != '?' && workTreeStatus != ' ') isDirty = true; // Unstaged changes (tracked files)
            // Untracked files (??) might not be considered "Dirty" for commit purposes unless added, 
            // but for "Working directory clean" they are relevant. 
            // The spec definition of DIRTY is "commitされていない変更あり".
        }

        return (changes, branch, upstream, isDirty);
    }

    public async Task<bool> IsRebaseInProgressAsync()
    {
        if (string.IsNullOrEmpty(_repoPath)) return false;
        var gitDir = Path.Combine(_repoPath, ".git");
        return Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || 
               Directory.Exists(Path.Combine(gitDir, "rebase-apply"));
    }

    public async Task<List<string>> GetConflictedFilesAsync(CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("git", "diff --name-only --diff-filter=U", _repoPath, ct);
        if (!result.Success) return new List<string>();
        return result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public async Task<ProcessResult> StageFileAsync(string filePath, CancellationToken ct = default)
    {
        return await _runner.RunAsync("git", $"add \"{filePath}\"", _repoPath, ct);
    }

    public async Task<ProcessResult> UnstageFileAsync(string filePath, CancellationToken ct = default)
    {
        return await _runner.RunAsync("git", $"restore --staged \"{filePath}\"", _repoPath, ct);
    }

    public async Task<ProcessResult> CommitAsync(string message, string body, CancellationToken ct = default)
    {
        // Use a temporary file to avoid command line escaping issues
        var tmpFile = Path.GetTempFileName();
        try
        {
             var fullMessage = message;
             if (!string.IsNullOrEmpty(body))
             {
                 fullMessage += "\n\n" + body;
             }
             
             await File.WriteAllTextAsync(tmpFile, fullMessage, ct);
             
             return await _runner.RunAsync("git", $"commit -F \"{tmpFile}\"", _repoPath, ct);
        }
        finally
        {
             if (File.Exists(tmpFile))
             {
                 try { File.Delete(tmpFile); } catch { }
             }
        }
    }

    public async Task<ProcessResult> PushAsync(string branch, bool setUpstream = false, CancellationToken ct = default)
    {
        var args = "push";
        if (setUpstream)
        {
            args += $" --set-upstream origin {branch}";
        }
        return await _runner.RunAsync("git", args, _repoPath, ct);
    }

    public async Task<ProcessResult> PullRebaseAsync(CancellationToken ct = default)
    {
        return await _runner.RunAsync("git", "pull --rebase", _repoPath, ct);
    }

    public async Task<ProcessResult> StashAsync(string message, CancellationToken ct = default)
    {
        return await _runner.RunAsync("git", $"stash push -m \"{EscapeArg(message)}\"", _repoPath, ct);
    }

    public async Task<ProcessResult> RebaseContinueAsync(CancellationToken ct = default)
    {
        // Must set environment variable to open editor? No, usually continue doesn't need editor if conflicts resolved.
        // But if it asks for commit message edit, it might hang.
        // Using GIT_EDITOR=true (shell no-op) or similar might be safer if we want to avoid interactive mode.
        // For now, simple run.
        return await _runner.RunAsync("git", "rebase --continue", _repoPath, ct);
    }

    public async Task<ProcessResult> RebaseAbortAsync(CancellationToken ct = default)
    {
        return await _runner.RunAsync("git", "rebase --abort", _repoPath, ct);
    }

    public async Task<bool> HasUnpushedCommitsAsync(string branch, string upstream, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(upstream)) return true; // Assuming unpushed if no upstream
        
        // git log origin/main..main --oneline
        // If output is not empty, there are unpushed commits.
        var result = await _runner.RunAsync("git", $"log {upstream}..{branch} --oneline", _repoPath, ct);
        return result.Success && !string.IsNullOrWhiteSpace(result.StandardOutput);
    }

    public async Task<ProcessResult> GetDiffAsync(string filePath, bool cached, CancellationToken ct = default)
    {
        var args = $"diff {(cached ? "--cached" : "")} -- \"{filePath}\"" ;
        return await _runner.RunAsync("git", args, _repoPath, ct);
    }

    private string EscapeArg(string arg)
    {
        return arg.Replace("\"", "\\\"");
    }

    /// @brief ローカルブランチ一覧を取得する
    public async Task<List<string>> GetLocalBranchesAsync(CancellationToken ct = default)
    {
        // git branch --format="%(refname:short)"
        var result = await _runner.RunAsync("git", "branch --format=\"%(refname:short)\"", _repoPath, ct);
        if (!result.Success) return new List<string>();
        
        return result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();
    }

    /// @brief ブランチをチェックアウトする
    public async Task<ProcessResult> CheckoutAsync(string branchName, CancellationToken ct = default)
    {
        return await _runner.RunAsync("git", $"checkout \"{EscapeArg(branchName)}\"", _repoPath, ct);
    }

    /// @brief ブランチを新規作成してチェックアウトする (-b)
    public async Task<ProcessResult> CreateBranchAsync(string branchName, CancellationToken ct = default)
    {
        return await _runner.RunAsync("git", $"checkout -b \"{EscapeArg(branchName)}\"", _repoPath, ct);
    }

    /// @brief 直近のコミットメッセージを取得
    public async Task<string> GetLastCommitMessageAsync(CancellationToken ct = default)
    {
        var res = await _runner.RunAsync("git", "log -1 --pretty=%B", _repoPath, ct);
        return res.Success ? res.StandardOutput.Trim() : string.Empty;
    }

    /// @brief ブランチを削除する
    /// @param branchName 削除するブランチ名
    /// @param force 強制削除するかどうか (-D)
    public async Task<ProcessResult> DeleteBranchAsync(string branchName, bool force = false, CancellationToken ct = default)
    {
        var flag = force ? "-D" : "-d";
        return await _runner.RunAsync("git", $"branch {flag} \"{EscapeArg(branchName)}\"", _repoPath, ct);
    }

    /// @brief リポジトリをCloneする
    public async Task<ProcessResult> CloneAsync(string url, string destinationPath, CancellationToken ct = default)
    {
        var workingDir = Path.GetDirectoryName(destinationPath);
        // git clone handles absolute path, working dir doesn't matter much if we provide full path
        // but ProcessRunner might need a valid dir.
        if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir))
        {
             workingDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); 
        }

        return await _runner.RunAsync("git", $"clone \"{url}\" \"{destinationPath}\"", workingDir, ct);
    }

    /// @brief リポジトリを初期化する
    public async Task<ProcessResult> InitAsync(string path, CancellationToken ct = default)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return await _runner.RunAsync("git", "init", path, ct);
    }

    /// @brief 指定したリモートのURLを取得する
    public async Task<string> GetRemoteUrlAsync(string remote = "origin", CancellationToken ct = default)
    {
        var res = await _runner.RunAsync("git", $"remote get-url {remote}", _repoPath, ct);
        return res.Success ? res.StandardOutput.Trim() : string.Empty;
    }

    /// @brief リモートURLからリポジトリオーナー名を抽出する
    /// @param remoteUrl リモートURL（SSH/HTTPS形式）
    /// @return オーナー名（抽出できない場合は空文字）
    public static string ExtractOwnerFromRemoteUrl(string remoteUrl)
    {
        if (string.IsNullOrEmpty(remoteUrl)) return string.Empty;

        // SSH形式: git@github.com:owner/repo.git
        if (remoteUrl.StartsWith("git@"))
        {
            var colonIdx = remoteUrl.IndexOf(':');
            if (colonIdx > 0 && colonIdx < remoteUrl.Length - 1)
            {
                var pathPart = remoteUrl.Substring(colonIdx + 1);
                var slashIdx = pathPart.IndexOf('/');
                if (slashIdx > 0)
                {
                    return pathPart.Substring(0, slashIdx);
                }
            }
        }
        // HTTPS形式: https://github.com/owner/repo.git
        else if (remoteUrl.StartsWith("https://") || remoteUrl.StartsWith("http://"))
        {
            try
            {
                var uri = new Uri(remoteUrl);
                var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 1)
                {
                    return segments[0];
                }
            }
            catch { }
        }

        return string.Empty;
    }
}