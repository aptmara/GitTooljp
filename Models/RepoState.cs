namespace SimplePRClient.Models;

using System;

/// <summary>
/// リポジトリの状態を表すフラグ列挙型。
/// 複数の状態が同時に成立する場合がある (例: Dirty | NoUpstream)。
/// Clean (=0) は「他のフラグが一切立っていない」状態を示す。
/// </summary>
[Flags]
public enum RepoState
{
    /// <summary>
    /// 変更なし。UI表示上の "Clean" は (state == 0) で判定する。
    /// </summary>
    Clean = 0,

    /// <summary>
    /// Commit されていない変更あり (Staged または Unstaged)
    /// </summary>
    Dirty = 1 << 0,

    /// <summary>
    /// Push されていない Commit あり (Upstream がある場合のみ判定)
    /// </summary>
    Unpushed = 1 << 1,

    /// <summary>
    /// Upstream (追跡ブランチ) が未設定
    /// </summary>
    NoUpstream = 1 << 2,

    /// <summary>
    /// Rebase 進行中 (Conflict を含む可能性あり)
    /// </summary>
    Rebase = 1 << 3,

    /// <summary>
    /// GitHub CLI (gh) の認証が必要
    /// </summary>
    AuthNg = 1 << 4,
}
