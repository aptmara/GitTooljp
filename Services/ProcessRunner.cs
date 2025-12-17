namespace SimplePRClient.Services;

using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 外部プロセス実行結果
/// </summary>
public class ProcessResult
{
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public bool Success => ExitCode == 0;
}

/// <summary>
/// 外部CLI実行のラッパーサービス
/// </summary>
public class ProcessRunner
{
    /// <summary>
    /// シークレット情報をマスクするための正規表現パターン
    /// </summary>
    private static readonly Regex[] MaskPatterns = new[]
    {
        new Regex(@"ghp_[a-zA-Z0-9]{36}", RegexOptions.Compiled),
        new Regex(@"github_pat_[a-zA-Z0-9_]{22,}", RegexOptions.Compiled),
        new Regex(@"Authorization:\s*.+", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"https://[^:]+:[^@]+@", RegexOptions.Compiled),
    };

    public event Action<string, bool>? OnOutput;

    /// <summary>
    /// コマンドを非同期で実行
    /// </summary>
    public async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var maskedArgs = MaskSecrets(arguments);
        OnOutput?.Invoke($"> {fileName} {maskedArgs}", false);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                var masked = MaskSecrets(e.Data);
                stdout.AppendLine(masked);
                OnOutput?.Invoke(masked, false);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                var masked = MaskSecrets(e.Data);
                stderr.AppendLine(masked);
                OnOutput?.Invoke(masked, true);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        OnOutput?.Invoke($"Exit code: {process.ExitCode}", process.ExitCode != 0);

        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdout.ToString(),
            StandardError = stderr.ToString(),
        };
    }

    /// <summary>
    /// シークレット情報をマスク
    /// </summary>
    private static string MaskSecrets(string input)
    {
        foreach (var pattern in MaskPatterns)
        {
            input = pattern.Replace(input, "[MASKED]");
        }
        return input;
    }
}
