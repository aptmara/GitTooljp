namespace SimplePRClient.Models;

/// @brief ログエントリを表すモデル
/// 作成者: 山内陽
public class LogEntry
{
    /// @brief タイムスタンプ
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// @brief ログメッセージ
    public string Message { get; set; } = string.Empty;

    /// @brief エラーかどうか
    public bool IsError { get; set; }

    /// @brief 文字列変換 (コピー用)
    public override string ToString()
    {
        return $"[{Timestamp:HH:mm:ss}] {(IsError ? "[ERROR] " : "")}{Message}";
    }
}
