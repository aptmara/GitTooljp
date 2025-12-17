namespace SimplePRClient.Models;

using CommunityToolkit.Mvvm.ComponentModel;

/// @brief git status --porcelain の結果を保持するモデル
/// 作成者: 山内陽
public partial class FileChangeEntry : ObservableObject
{
    /// @brief ファイルの相対パス
    public string FilePath { get; set; } = string.Empty;

    /// @brief Index (Staged) の状態 (M/A/D/R/C/U/?)
    public char IndexStatus { get; set; }

    /// @brief WorkTree (Unstaged) の状態 (M/A/D/R/C/U/?)
    public char WorkTreeStatus { get; set; }

    /// @brief Staged 状態かどうか (UIバインド用双方向プロパティ)
    /// 初期値は IndexStatus から判定するが、UI操作で即時変更できるようにする
    [ObservableProperty]
    private bool _isStaged;

    /// @brief コンストラクタ
    public FileChangeEntry() { }

    /// @brief 初期化用コンストラクタ
    public FileChangeEntry(string path, char index, char work)
    {
        FilePath = path;
        IndexStatus = index;
        WorkTreeStatus = work;
        // 初期判定
        IsStaged = (index != ' ' && index != '?');
    }

    /// @brief Conflict 状態かどうか (Unmerged)
    public bool IsConflict => IndexStatus == 'U' || WorkTreeStatus == 'U';
}
