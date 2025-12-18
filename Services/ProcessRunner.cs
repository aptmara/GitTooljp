namespace SimplePRClient.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// @brief 外部プロセス実行結果
/// 作成者: 山内陽
public record ProcessResult(bool Success, string StandardOutput, string StandardError, int ExitCode);

/// @brief 外部プロセス実行ランナー
/// 作成者: 山内陽
public class ProcessRunner
{
    // ログ出力用イベントなどが必要であればここに追加

    /// @brief コマンドを非同期で実行する
    /// @param fileName 実行ファイル名 (git, gh 等)
    /// @param arguments 引数
    /// @param workingDirectory 作業ディレクトリ
    /// @param ct キャンセルトークン
    /// @return 実行結果
    /// @brief コマンドを非同期で実行する
    /// @param fileName 実行ファイル名 (git, gh 等)
    /// @param arguments 引数
    /// @param workingDirectory 作業ディレクトリ
    /// @param ct キャンセルトークン
    /// @param configureEnvironment 環境変数を設定するアクション
    /// @return 実行結果
    /// @brief コマンドを非同期で実行する
    /// @param fileName 実行ファイル名 (git, gh 等)
    /// @param arguments 引数
    /// @param workingDirectory 作業ディレクトリ
    /// @param ct キャンセルトークン
    /// @param configureEnvironment 環境変数を設定するアクション
    /// @param interactive インタラクティブモード (ウィンドウ表示、リダイレクトなし)
    /// @return 実行結果
    public async Task<ProcessResult> RunAsync(string fileName, string arguments, string workingDirectory = "", CancellationToken ct = default, Action<System.Collections.Specialized.StringDictionary>? configureEnvironment = null, bool interactive = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = !interactive,
            RedirectStandardError = !interactive,
            UseShellExecute = false,
            CreateNoWindow = !interactive,
            StandardOutputEncoding = interactive ? null : Encoding.UTF8,
            StandardErrorEncoding = interactive ? null : Encoding.UTF8
        };

        // 環境変数の調整 - Gitの出力をUTF-8に強制
        if (!interactive) 
        {
            psi.EnvironmentVariables["LANG"] = "C.UTF-8";
            psi.EnvironmentVariables["LC_ALL"] = "C.UTF-8";
            psi.EnvironmentVariables["GIT_PAGER"] = ""; // ページャを無効化
            psi.EnvironmentVariables["LESSCHARSET"] = "utf-8";
            // Windows Git が内部的に使用するエンコーディング設定
            psi.EnvironmentVariables["GIT_OUTPUT_ENCODING"] = "utf-8";
        }
        configureEnvironment?.Invoke(psi.EnvironmentVariables);

        using var process = new Process { StartInfo = psi };
        
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        if (!interactive)
        {
            process.OutputDataReceived += (s, e) => { if (e.Data != null) stdoutBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderrBuilder.AppendLine(e.Data); };
        }

        try
        {
            process.Start();
            if (!interactive)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            await process.WaitForExitAsync(ct);

            return new ProcessResult(
                process.ExitCode == 0,
                stdoutBuilder.ToString(),
                stderrBuilder.ToString(),
                process.ExitCode
            );
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
            throw;
        }
        catch (Exception ex)
        {
            return new ProcessResult(false, string.Empty, ex.Message, -1);
        }
    }
}