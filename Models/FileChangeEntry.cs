namespace SimplePRClient.Models;

/// <summary>
/// git status --porcelain の結果を保持するモデル
/// </summary>
public class FileChangeEntry
{
    /// <summary>
    /// ファイルの相対パス
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Index (Staged) の状態 (M/A/D/R/C/U/?)
    /// </summary>
    public char IndexStatus { get; set; }

    /// <summary>
    /// WorkTree (Unstaged) の状態 (M/A/D/R/C/U/?)
    /// </summary>
    public char WorkTreeStatus { get; set; }

    /// <summary>
    /// Staged 状態かどうか
    /// </summary>
    public bool IsStaged => IndexStatus != ' ' && IndexStatus != '?';

    /// <summary>
    /// Conflict 状態かどうか (Unmerged)
    /// </summary>
    public bool IsConflict => IndexStatus == 'U' || WorkTreeStatus == 'U';
}
