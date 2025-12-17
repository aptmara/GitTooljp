namespace SimplePRClient.Models;

/// <summary>
/// ログエントリを表すモデル
/// </summary>
public class LogEntry
{
    /// <summary>
    /// タイムスタンプ
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// ログメッセージ
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// エラーかどうか
    /// </summary>
    public bool IsError { get; set; }
}
