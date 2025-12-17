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
        var result = await _runner.RunAsync("git", "status --porcelain=v1 -b", _repoPath, ct);
        
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

            changes.Add(new FileChangeEntry
            {
                FilePath = path,
                IndexStatus = indexStatus,
                WorkTreeStatus = workTreeStatus
            });

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
        // git commit -m "title" -m "body"
        // Argument escaping is critical here. Using a simpler approach for now.
        // For production, consider using a temporary file for the message if it contains complex chars.
        
        // Simple escaping for CLI (basic quotes handling)
        var msgArg = EscapeArg(message);
        var bodyArg = string.IsNullOrEmpty(body) ? "" : $"-m \"{EscapeArg(body)}\"" ;

        return await _runner.RunAsync("git", $"commit -m \"{msgArg}\" {bodyArg}", _repoPath, ct);
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

    private static string EscapeArg(string arg)
    {
        // Basic escaping for Windows command line
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
}