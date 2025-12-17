namespace SimplePRClient.Models;

using System;

/// @brief リポジトリの状態を表すフラグ列挙型。
/// 複数の状態が同時に成立する場合がある (例: Dirty | NoUpstream)。
/// Clean (=0) は「他のフラグが一切立っていない」状態を示す。
/// 作成者: 山内陽
[Flags]
public enum RepoState
{
    /// @brief 変更なし。UI表示上の "Clean" は (state == 0) で判定する。
    Clean = 0,

    /// @brief Commit されていない変更あり (Staged または Unstaged)
    Dirty = 1 << 0,

    /// @brief Push されていない Commit あり (Upstream がある場合のみ判定)
    Unpushed = 1 << 1,

    /// @brief Upstream (追跡ブランチ) が未設定
    NoUpstream = 1 << 2,

    /// @brief Rebase 進行中 (Conflict を含む可能性あり)
    Rebase = 1 << 3,

    /// @brief GitHub CLI (gh) の認証が必要
    AuthNg = 1 << 4,
}
