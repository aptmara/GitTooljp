namespace SimplePRClient.Models;

/// @brief git status --porcelain の結果を保持するモデル
/// 作成者: 山内陽
public class FileChangeEntry
{
    /// @brief ファイルの相対パス
    public string FilePath { get; set; } = string.Empty;

    /// @brief Index (Staged) の状態 (M/A/D/R/C/U/?)
    public char IndexStatus { get; set; }

    /// @brief WorkTree (Unstaged) の状態 (M/A/D/R/C/U/?)
    public char WorkTreeStatus { get; set; }

    /// @brief Staged 状態かどうか
    public bool IsStaged => IndexStatus != ' ' && IndexStatus != '?';

    /// @brief Conflict 状態かどうか (Unmerged)
    public bool IsConflict => IndexStatus == 'U' || WorkTreeStatus == 'U';
}
