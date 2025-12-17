namespace SimplePRClient.Services;

using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

/// @brief 外部ツール検出サービス
/// 作成者: 山内陽
public class ToolDetector
{
    /// @brief git が利用可能か確認
    /// @return gitコマンドが実行可能であればtrue
    public async Task<bool> IsGitAvailableAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// @brief gh (GitHub CLI) が利用可能か確認
    /// @return ghコマンドが実行可能であればtrue
    public async Task<bool> IsGhAvailableAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("gh", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// @brief Visual Studio のパスを取得 (vswhere 経由)
    /// @return devenv.exeのパス。見つからない場合はnull
    public string? GetVisualStudioPath()
    {
        // vswhere の標準パス
        var vswherePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");

        if (!File.Exists(vswherePath))
        {
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo(vswherePath, "-latest -property installationPath")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (string.IsNullOrEmpty(output)) return null;

            var devenvPath = Path.Combine(output, "Common7", "IDE", "devenv.exe");
            return File.Exists(devenvPath) ? devenvPath : null;
        }
        catch
        {
            return null;
        }
    }

    /// @brief Visual Studio でリポジトリを開く
    /// @param repoPath リポジトリのパス
    /// @param vsPath Visual Studioのパス (nullの場合は自動検出)
    /// @return Visual Studioで開けた場合はtrue、フォールバックした場合はfalse
    public bool OpenInVisualStudio(string repoPath, string? vsPath = null)
    {
        vsPath ??= GetVisualStudioPath();

        if (string.IsNullOrEmpty(vsPath))
        {
            // フォールバック: エクスプローラーで開く
            Process.Start("explorer.exe", repoPath);
            return false;
        }

        Process.Start(vsPath, $"\"{repoPath}\"");
        return true;
    }

    /// @brief 既定のエディタでファイルを開く
    /// @param filePath ファイルパスまたはURL
    public void OpenFileInDefaultEditor(string filePath)
    {
        Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
    }
}
