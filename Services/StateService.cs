namespace SimplePRClient.Services;

using SimplePRClient.Models;
using System.Threading;
using System.Threading.Tasks;

/// @brief アプリケーションの状態 (RepoState) を管理・判定するサービス
/// 作成者: 山内陽
public class StateService
{
    private readonly GitService _gitService;
    private readonly GitHubService _gitHubService;

    public StateService(GitService gitService, GitHubService gitHubService)
    {
        _gitService = gitService;
        _gitHubService = gitHubService;
    }

    /// @brief 現在の全ての状態を判定して RepoState を返す
    /// @param ct キャンセルトークン
    /// @return 現在のリポジトリ状態フラグ
    public async Task<RepoState> GetCurrentStateAsync(CancellationToken ct = default)
    {
        var state = RepoState.Clean;

        // 1. Rebase Check
        if (await _gitService.IsRebaseInProgressAsync())
        {
            state |= RepoState.Rebase;
            // Rebase 中は他の判定をスキップしても良いが、
            // Conflict 状態なども知りたい場合は続行する。
            // 仕様上、Rebase 中は操作が制限されるので、ここで確定させても良い。
            // ただし Dirty 表示などはあっても良いかもしれない。
            // 今回は Rebase が最強のフラグとして扱うが、Dirty も併記されるようにする。
        }

        // 2. Git Status Check (Dirty, Branch, Upstream)
        var (changes, branch, upstream, isDirty) = await _gitService.GetStatusAsync(ct);

        if (isDirty)
        {
            state |= RepoState.Dirty;
        }

        if (string.IsNullOrEmpty(upstream))
        {
            state |= RepoState.NoUpstream;
        }
        else
        {
            // 3. Unpushed Check (Upstream がある場合のみ)
            // Rebase 中はそもそも Push できないのでチェック不要かもしれないが、
            // 情報として表示するために取得しても良い。
            // ただし Rebase 中は HEAD が遊離していたりするので、
            // 正確な rev-list が取れない可能性がある。
            // 安全のため Rebase 中は Unpushed チェックをスキップするという手もあるが、
            // GitService 側でエラーハンドリングされていれば問題ない。
            
            // ここでは Rebase 中でなければチェックする方針にする
            if ((state & RepoState.Rebase) == 0)
            {
                if (await _gitService.HasUnpushedCommitsAsync(branch, upstream, ct))
                {
                    state |= RepoState.Unpushed;
                }
            }
        }

        // 4. Auth Check (AuthNg)
        // 毎回 gh auth status を叩くと遅い可能性があるので、
        // キャッシュするか、特定のタイミングのみ実行する設計もありうる。
        // ここでは都度実行とするが、パフォーマンスに問題あれば見直す。
        if (!await _gitHubService.IsAuthenticatedAsync(ct))
        {
            state |= RepoState.AuthNg;
        }

        return state;
    }
}
