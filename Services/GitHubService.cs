namespace SimplePRClient.Services;

using System.Threading;
using System.Threading.Tasks;

/// @brief GitHub CLI (gh) ラッパーサービス
/// 作成者: 山内陽
public class GitHubService
{
    private readonly ProcessRunner _runner;
    private string _repoPath = string.Empty;

    public GitHubService(ProcessRunner runner)
    {
        _runner = runner;
    }

    /// @brief リポジトリのパスを設定
    /// @param path リポジトリのルートパス
    public void SetRepository(string path)
    {
        _repoPath = path;
    }

    /// @brief gh の認証状態を確認
    /// @param ct キャンセルトークン
    /// @return 認証済みであれば true
    public async Task<bool> IsAuthenticatedAsync(CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("gh", "auth status", _repoPath, ct);
        return result.Success;
    }

    /// @brief Pull Request を作成
    /// @param title PRタイトル
    /// @param body PR本文
    /// @param baseBranch マージ先ブランチ
    /// @param headBranch マージ元ブランチ
    /// @param ct キャンセルトークン
    /// @return 実行結果
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

    /// @brief gh auth login を起動 (インタラクティブ)
    /// @param ct キャンセルトークン
    /// @return 実行結果
    public async Task<ProcessResult> RunAuthLoginAsync(CancellationToken ct = default)
    {
        // ブラウザ認証を開始
        return await _runner.RunAsync("gh", "auth login --web", _repoPath, ct);
    }

    /// @brief デフォルトブランチを取得
    /// @param ct キャンセルトークン
    /// @return デフォルトブランチ名
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
