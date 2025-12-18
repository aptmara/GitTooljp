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
        var result = await RunGhAsync("auth status", ct);
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
        return await RunGhAsync(args, ct);
    }

    /// @brief gh auth login を起動 (インタラクティブ)
    /// @param ct キャンセルトークン
    /// @return 実行結果
    public async Task<ProcessResult> RunAuthLoginAsync(CancellationToken ct = default)
    {
        // ブラウザ認証を開始
        return await RunGhAsync("auth login --web", ct, interactive: true);
    }

    /// @brief gh auth refresh -s repo を実行 (権限更新)
    public async Task<ProcessResult> RefreshAuthAsync(CancellationToken ct = default)
    {
        // また、対話プロンプトが出る可能性があるため、interactive: true (ウィンドウ表示) で実行する。
        return await RunGhAsync("auth refresh -h github.com -s repo", ct, interactive: true);
    }

    /// @brief デフォルトブランチを取得
    /// @param ct キャンセルトークン
    /// @return デフォルトブランチ名
    public async Task<string> GetDefaultBranchAsync(CancellationToken ct = default)
    {
        var result = await RunGhAsync("repo view --json defaultBranchRef --jq .defaultBranchRef.name", ct);
        return result.Success ? result.StandardOutput.Trim() : "main";
    }

    /// @brief 現在のGitHub認証ユーザー名を取得
    /// @param ct キャンセルトークン
    /// @return ユーザー名（取得できない場合は空文字）
    public async Task<string> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var result = await RunGhAsync("api user --jq .login", ct);
        return result.Success ? result.StandardOutput.Trim() : string.Empty;
    }

    private static string EscapeArg(string arg)
    {
        return arg.Replace("\"", "\\\"");
    }

    /// @brief ghコマンド共通実行ヘルパー (GITHUB_TOKEN対策)
    private Task<ProcessResult> RunGhAsync(string args, CancellationToken ct, bool interactive = false)
    {
        return _runner.RunAsync("gh", args, _repoPath, ct, env => 
        {
            // GITHUB_TOKEN が環境変数にあると、gh は内部の認証ストア(refreshされたもの)ではなく
            // その環境変数を優先して使用してしまう。
            // ユーザー環境に古い TOKEN が残っているケース対策として、
            // アプリからの実行時は常に環境変数を無視(削除)させる。
            env.Remove("GITHUB_TOKEN");
        }, interactive);
    }
}
