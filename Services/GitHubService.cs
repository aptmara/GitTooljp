namespace SimplePRClient.Services;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// GitHub CLI (gh) ラッパーサービス
/// </summary>
public class GitHubService
{
    private readonly ProcessRunner _runner;
    private string _repoPath = string.Empty;

    public GitHubService(ProcessRunner runner)
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
    /// gh の認証状態を確認
    /// </summary>
    public async Task<bool> IsAuthenticatedAsync(CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("gh", "auth status", _repoPath, ct);
        return result.Success;
    }

    /// <summary>
    /// Pull Request を作成
    /// </summary>
    public async Task<ProcessResult> CreatePullRequestAsync(
        string title,
        string body,
        string baseBranch,
        string headBranch,
        CancellationToken ct = default)
    {
        var args = $"pr create --base \"{baseBranch}\" --head \"{headBranch}\" --title \"{EscapeArg(title)}\" --body \"{EscapeArg(body)}\"";
        return await _runner.RunAsync("gh", args, _repoPath, ct);
    }

    /// <summary>
    /// gh auth login を起動 (インタラクティブ)
    /// </summary>
    public async Task<ProcessResult> RunAuthLoginAsync(CancellationToken ct = default)
    {
        // ブラウザ認証を開始
        return await _runner.RunAsync("gh", "auth login --web", _repoPath, ct);
    }

    /// <summary>
    /// デフォルトブランチを取得
    /// </summary>
    public async Task<string> GetDefaultBranchAsync(CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("gh", "repo view --json defaultBranchRef --jq .defaultBranchRef.name", _repoPath, ct);
        return result.Success ? result.StandardOutput.Trim() : "main";
    }

    private static string EscapeArg(string arg)
    {
        return arg.Replace("\"", "\\\"");
    }
}
