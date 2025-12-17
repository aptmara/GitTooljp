namespace SimplePRClient.Services;

using SimplePRClient.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Git CLI ラッパーサービス
/// </summary>
public class GitService
{
    private readonly ProcessRunner _runner;
    private string _repoPath = string.Empty;

    public GitService(ProcessRunner runner)
    {
        _runner = runner;
    }

    /// <summary>
    /// リポジトリのパスを設定
    /// </summary>
    public void SetRepository(string path)
    {
        _repoPath = path;
    }

    /// <summary>
    /// 現在のリポジトリパス
    /// </summary>
    public string RepositoryPath => _repoPath;

    /// <summary>
    /// 現在のブランチ名を取得
    /// </summary>
    public async Task<string> GetCurrentBranchAsync(CancellationToken ct = default)
    {
        var result = await RunGitAsync("rev-parse --abbrev-ref HEAD", ct);
        return result.Success ? result.StandardOutput.Trim() : string.Empty;
    }

    /// <summary>
    /// リポジトリの状態を取得 (Flags)
    /// </summary>
    public async Task<RepoState> GetRepoStateAsync(CancellationToken ct = default)
    {
        var state = RepoState.Clean;

        // Rebase 検知
        if (IsRebaseInProgress())
        {
            state |= RepoState.Rebase;
        }

        // Dirty 検知 (Staged/Unstaged/Untracked)
        var statusResult = await RunGitAsync("status --porcelain", ct);
        if (statusResult.Success && !string.IsNullOrWhiteSpace(statusResult.StandardOutput))
        {
            state |= RepoState.Dirty;
        }

        // Upstream 検知
        var upstreamResult = await RunGitAsync("rev-parse --abbrev-ref --symbolic-full-name @{u}", ct);
        if (!upstreamResult.Success)
        {
            state |= RepoState.NoUpstream;
        }
        else
        {
            // Unpushed 検知 (Upstream がある場合のみ)
            var countResult = await RunGitAsync("rev-list --count @{u}..HEAD", ct);
            if (countResult.Success && int.TryParse(countResult.StandardOutput.Trim(), out var count) && count > 0)
            {
                state |= RepoState.Unpushed;
            }
        }

        return state;
    }

    /// <summary>
    /// ファイル変更一覧を取得
    /// </summary>
    public async Task<List<FileChangeEntry>> GetFileChangesAsync(CancellationToken ct = default)
    {
        var result = await RunGitAsync("status --porcelain", ct);
        var entries = new List<FileChangeEntry>();

        if (!result.Success) return entries;

        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 3) continue;

            entries.Add(new FileChangeEntry
            {
                IndexStatus = line[0],
                WorkTreeStatus = line[1],
                FilePath = line.Substring(3).Trim(),
            });
        }

        return entries;
    }

    /// <summary>
    /// Rebase 進行中かどうか
    /// </summary>
    public bool IsRebaseInProgress()
    {
        if (string.IsNullOrEmpty(_repoPath)) return false;

        var gitDir = Path.Combine(_repoPath, ".git");
        return Directory.Exists(Path.Combine(gitDir, "rebase-apply")) ||
               Directory.Exists(Path.Combine(gitDir, "rebase-merge"));
    }

    /// <summary>
    /// ファイルを Stage
    /// </summary>
    public async Task<ProcessResult> StageFileAsync(string filePath, CancellationToken ct = default)
    {
        return await RunGitAsync($"add \"{filePath}\"", ct);
    }

    /// <summary>
    /// ファイルを Unstage
    /// </summary>
    public async Task<ProcessResult> UnstageFileAsync(string filePath, CancellationToken ct = default)
    {
        return await RunGitAsync($"restore --staged \"{filePath}\"", ct);
    }

    /// <summary>
    /// Commit を実行
    /// </summary>
    public async Task<ProcessResult> CommitAsync(string message, string? body = null, CancellationToken ct = default)
    {
        var args = $"commit -m \"{EscapeMessage(message)}\"";
        if (!string.IsNullOrWhiteSpace(body))
        {
            args += $" -m \"{EscapeMessage(body)}\"";
        }
        return await RunGitAsync(args, ct);
    }

    /// <summary>
    /// Push を実行
    /// </summary>
    public async Task<ProcessResult> PushAsync(CancellationToken ct = default)
    {
        return await RunGitAsync("push", ct);
    }

    /// <summary>
    /// Push (--set-upstream) を実行
    /// </summary>
    public async Task<ProcessResult> PushSetUpstreamAsync(string branch, CancellationToken ct = default)
    {
        return await RunGitAsync($"push --set-upstream origin {branch}", ct);
    }

    /// <summary>
    /// Pull --rebase を実行
    /// </summary>
    public async Task<ProcessResult> PullRebaseAsync(CancellationToken ct = default)
    {
        return await RunGitAsync("pull --rebase", ct);
    }

    /// <summary>
    /// Stash を実行
    /// </summary>
    public async Task<ProcessResult> StashAsync(CancellationToken ct = default)
    {
        return await RunGitAsync("stash push -m \"auto-stash before pull\"", ct);
    }

    /// <summary>
    /// Rebase を continue
    /// </summary>
    public async Task<ProcessResult> RebaseContinueAsync(CancellationToken ct = default)
    {
        return await RunGitAsync("rebase --continue", ct);
    }

    /// <summary>
    /// Rebase を abort
    /// </summary>
    public async Task<ProcessResult> RebaseAbortAsync(CancellationToken ct = default)
    {
        return await RunGitAsync("rebase --abort", ct);
    }

    /// <summary>
    /// ファイルの Diff を取得
    /// </summary>
    public async Task<string> GetDiffAsync(string filePath, bool staged = false, CancellationToken ct = default)
    {
        var args = staged ? $"diff --cached \"{filePath}\"" : $"diff \"{filePath}\"";
        var result = await RunGitAsync(args, ct);
        return result.Success ? result.StandardOutput : string.Empty;
    }

    private async Task<ProcessResult> RunGitAsync(string arguments, CancellationToken ct)
    {
        return await _runner.RunAsync("git", arguments, _repoPath, ct);
    }

    private static string EscapeMessage(string message)
    {
        return message.Replace("\"", "\\\"");
    }
}
