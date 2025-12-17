namespace SimplePRClient.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimplePRClient.Models;
using SimplePRClient.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using System.Diagnostics;

/// @brief メイン画面のViewModel
/// 作成者: 山内陽
public partial class MainViewModel : ObservableObject
{
    private readonly GitService _gitService;
    private readonly GitHubService _gitHubService;
    private readonly StateService _stateService;

    private readonly ToolDetector _toolDetector;
    private readonly SettingsService _settingsService;

    // Git Operation Lock (Semaphore for background tasks)
    private readonly SemaphoreSlim _gitLock = new(1, 1);
    private int _pendingOperations = 0; // Track background ops

    // UI Locking
    /// @brief ビジー状態かどうか
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    [NotifyCanExecuteChangedFor(nameof(PushCommand))]
    [NotifyCanExecuteChangedFor(nameof(PullCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreatePRCommand))]
    [NotifyCanExecuteChangedFor(nameof(AbortRebaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueRebaseCommand))]
    private bool _isBusy;

    /// @brief ビジー状態でないかどうか
    public bool IsNotBusy => !IsBusy;

    // State
    /// @brief 現在のリポジトリ状態
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusMessage))]
    [NotifyPropertyChangedFor(nameof(IsRebaseMode))]
    private RepoState _currentState;

    /// @brief 状態表示メッセージ
    public string StatusMessage
    {
        get
        {
            if (CurrentState.HasFlag(RepoState.Rebase)) return "状態：REBASE 進行中 (Conflict あり)";
            if (CurrentState.HasFlag(RepoState.AuthNg)) return "状態：gh の認証が必要です";
            if (CurrentState.HasFlag(RepoState.Dirty)) return "状態：commit されていない変更があります";
            if (CurrentState.HasFlag(RepoState.Unpushed)) return "状態：push されていない commit があります";
            if (CurrentState.HasFlag(RepoState.NoUpstream)) return "状態：upstream が設定されていません";
            return "状態：変更はありません (CLEAN)";
        }
    }

    /// @brief Rebaseモードかどうか
    public bool IsRebaseMode => CurrentState.HasFlag(RepoState.Rebase);

    // Repository Management
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRepositoryValid))]
    [NotifyPropertyChangedFor(nameof(StatusMessage))]
    private bool _isRepositoryValid = false;
    
    public bool IsNotRepositoryValid => !IsRepositoryValid;

    public ObservableCollection<string> RecentRepositories { get; } = new();

    // Data
    /// @brief リポジトリのパス
    [ObservableProperty]
    private string _repositoryPath = string.Empty;

    /// @brief 現在のブランチ名
    [ObservableProperty]
    private string _currentBranch = string.Empty;

    /// @brief コミットメッセージ
    [ObservableProperty]
    private string _commitMessage = string.Empty;

    /// @brief コミット詳細（Body）
    [ObservableProperty]
    private string _commitBody = string.Empty;

    /// @brief 差分テキスト
    [ObservableProperty]
    private string _diffText = string.Empty;

    /// @brief 選択された変更ファイル
    [ObservableProperty]
    private FileChangeEntry? _selectedChange;

    async partial void OnSelectedChangeChanged(FileChangeEntry? value)
    {
        if (value == null)
        {
            DiffText = "";
            return;
        }

        try
        {
            // Staged or Unstaged? 
            // If checking a staged file, we usually want staged diff.
            // If checking an unstaged file, unstaged diff.
            // But FileChangeEntry has both statuses.
            // For simplicity, prioritize Unstaged diff if M, else Staged.
            
            bool tryStaged = value.IsStaged && !value.WorkTreeStatus.Equals('M');
            
            var result = await _gitService.GetDiffAsync(value.FilePath, tryStaged);
            DiffText = result.Success ? result.StandardOutput : "Error loading diff: " + result.StandardError;
        }
        catch (Exception ex)
        {
            DiffText = "Error loading diff: " + ex.Message;
        }
    }

    /// @brief 変更ファイルリスト
    public ObservableCollection<FileChangeEntry> Changes { get; } = new();
    
    /// @brief ログリスト
    public ObservableCollection<LogEntry> Logs { get; } = new();

    /// @brief ブランチ一覧
    public ObservableCollection<string> Branches { get; } = new();

    /// @brief 選択されたブランチ (UIバインド用)
    [ObservableProperty]
    private string _selectedBranch = string.Empty;

    private bool _isChangingBranch = false;

    async partial void OnSelectedBranchChanged(string value)
    {
        if (_isChangingBranch) return; 
        if (string.IsNullOrEmpty(value)) return;

        if (value == "[作成...]")
        {
            _isChangingBranch = true;
            SelectedBranch = CurrentBranch;
            _isChangingBranch = false;
            Application.Current.Dispatcher.Invoke(CreateNewBranchFlow);
            return;
        }

        if (value == CurrentBranch) return; 

        if (CurrentState.HasFlag(RepoState.Dirty))
        {
            var res = MessageBox.Show("変更が残っています。stash して切り替えますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.No)
            {
                 _isChangingBranch = true;
                 SelectedBranch = CurrentBranch;
                 _isChangingBranch = false;
                 return;
            }
            
            IsBusy = true;
            await _gitService.StashAsync($"Auto-stash before checkout {value}");
            IsBusy = false;
        }

        IsBusy = true;
        try
        {
            Log($"Checkout {value}...", false);
            var result = await _gitService.CheckoutAsync(value);
            if (result.Success)
            {
                Log($"Checkout 完了: {value}", false);
                await RefreshInternalAsync(true);
            }
            else
            {
                Log($"Checkout 失敗:\n{result.StandardError}", true);
                _isChangingBranch = true;
                SelectedBranch = CurrentBranch;
                _isChangingBranch = false;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Repository Management Commands
    [RelayCommand]
    private void CloseRepository()
    {
        IsRepositoryValid = false;
        RepositoryPath = "";
        CurrentBranch = "";
        OnPropertyChanged(nameof(StatusMessage));
        Changes.Clear();
        Logs.Clear();
    }

    [RelayCommand]
    private async Task OpenRepositoryFlowAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        dialog.Title = "リポジトリフォルダを選択";
        if (dialog.ShowDialog() == true)
        {
             await OpenRepositoryAsync(dialog.FolderName);
        }
    }

    [RelayCommand]
    private async Task OpenRecentRepositoryAsync(string path)
    {
        await OpenRepositoryAsync(path);
    }

    private async Task OpenRepositoryAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return; 
        
        IsBusy = true;
        try
        {
            var root = GitService.FindGitRoot(path);
            if (root == null)
            {
                var res = MessageBox.Show($"\"{System.IO.Path.GetFileName(path)}\" はGitリポジトリではありません。\n初期化(git init)しますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    await InitRepositoryAsync(path);
                    return;
                }
                IsBusy = false;
                return;
            }

            _gitService.SetRepository(root);
            IsRepositoryValid = true;
            
            // Save settings
            _settingsService.AddRecentRepository(path);
            LoadRecentRepositories();

            await RefreshInternalAsync(false);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Open Error: " + ex.Message);
            IsRepositoryValid = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadRecentRepositories()
    {
        RecentRepositories.Clear();
        foreach(var r in _settingsService.Settings.RecentRepositories)
        {
            RecentRepositories.Add(r);
        }
    }

    private async Task InitRepositoryAsync(string path)
    {
         IsBusy = true;
         try
         {
             var result = await _gitService.InitAsync(path);
             if (result.Success)
             {
                 _gitService.SetRepository(path);
                 IsRepositoryValid = true;
                 _settingsService.AddRecentRepository(path);
                 LoadRecentRepositories();
                 
                 await RefreshInternalAsync(true);
                 MessageBox.Show("初期化完了", "完了");
             }
             else
             {
                 MessageBox.Show("初期化失敗:\n" + result.StandardError, "エラー");
             }
         }
         catch(Exception ex)
         {
             MessageBox.Show("Error: " + ex.Message);
         }
         finally
         {
             IsBusy = false;
         }
    }

    [RelayCommand]
    private async Task CloneRepositoryFlowAsync()
    {
        var inputWindow = new InputWindow("リポジトリURLを入力:", "Clone Repository");
        inputWindow.Owner = Application.Current.MainWindow;
        if (inputWindow.ShowDialog() != true) return; 
        
        var url = inputWindow.InputText;
        if (string.IsNullOrWhiteSpace(url)) return;

        var dialog = new Microsoft.Win32.OpenFolderDialog();
        dialog.Title = "保存先フォルダを選択";
        if (dialog.ShowDialog() != true) return; 
        
        var repoName = url.TrimEnd('/').Split('/').Last().Replace(".git", "");
        var dest = System.IO.Path.Combine(dialog.FolderName, repoName);
        
        if (System.IO.Directory.Exists(dest) && System.IO.Directory.GetFileSystemEntries(dest).Any())
        {
             if (MessageBox.Show($"\"{repoName}\" は既に存在します。続行しますか？", "確認", MessageBoxButton.YesNo) == MessageBoxResult.No) return;
        }

        IsBusy = true;
        try 
        {
             Log($"Cloning {url}...", false);
             var result = await _gitService.CloneAsync(url, dest);
             if (result.Success)
             {
                 Log("Clone 完了", false);
                 await OpenRepositoryAsync(dest);
             }
             else
             {
                 Log("Clone 失敗:\n" + result.StandardError, true);
                 MessageBox.Show("Clone失敗", "エラー");
             }
        }
        catch (Exception ex)
        {
             Log("Clone Error: " + ex.Message, true);
        }
        finally
        {
             IsBusy = false;
        }
    }

    // Branch Creation Logic
    private void CreateNewBranchFlow()
    {
        var inputWindow = new InputWindow("新しいブランチ名:", "ブランチ作成");
        inputWindow.Owner = Application.Current.MainWindow;
        if (inputWindow.ShowDialog() == true)
        {
            var newBranch = inputWindow.InputText;
            if (string.IsNullOrWhiteSpace(newBranch)) return;
            Task.Run(async () => await CreateBranchAsync(newBranch));
        }
    }

    private async Task CreateBranchAsync(string branchName)
    {
         Application.Current.Dispatcher.Invoke(() => IsBusy = true);
         try
         {
             Log($"Create Branch: {branchName}...", false);
             var result = await _gitService.CreateBranchAsync(branchName);
             if (result.Success)
             {
                 Log("作成完了", false);
                 await RefreshInternalAsync(true);
             }
             else
             {
                 Log($"作成失敗:\n{result.StandardError}", true);
                 Application.Current.Dispatcher.Invoke(() => IsBusy = false);
             }
         }
         catch (Exception ex)
         {
             Log("Error: " + ex.Message, true);
             Application.Current.Dispatcher.Invoke(() => IsBusy = false);
         }
    }

    [RelayCommand]
    private void DeleteBranchFlow()
    {
        // Filter out current branch to prevent deleting it
        var deletableBranches = Branches.Where(b => b != CurrentBranch && b != "[作成...]").ToList();

        if (!deletableBranches.Any())
        {
            MessageBox.Show("削除可能なブランチがありません（現在のブランチは削除できません）。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new BranchDeletionWindow(deletableBranches);
        window.Owner = Application.Current.MainWindow;
        if (window.ShowDialog() == true)
        {
            var targets = window.SelectedBranches;
            if (targets.Any())
            {
                Task.Run(async () => await DeleteBranchesAsync(targets));
            }
        }
    }

    private async Task DeleteBranchesAsync(List<string> branches)
    {
        Application.Current.Dispatcher.Invoke(() => IsBusy = true);
        try
        {
            foreach (var branch in branches)
            {
                Log($"Delete Branch: {branch}...", false);
                var result = await _gitService.DeleteBranchAsync(branch);
                
                if (!result.Success && (result.StandardError.Contains("not fully merged") || result.StandardError.Contains("not merged")))
                {
                   var res = MessageBox.Show($"ブランチ '{branch}' はマージされていません。\n強制削除しますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                   if (res == MessageBoxResult.Yes)
                   {
                        result = await _gitService.DeleteBranchAsync(branch, true);
                   }
                }

                if (result.Success)
                {
                    Log($"削除完了: {branch}", false);
                }
                else
                {
                    Log($"削除失敗 ({branch}):\n{result.StandardError}", true);
                }
            }
            await RefreshInternalAsync(true);
        }
        catch (Exception ex)
        {
            Log("Error: " + ex.Message, true);
            Application.Current.Dispatcher.Invoke(() => IsBusy = false);
        }
    }

    // README Viewer Properties
    [ObservableProperty]
    private bool _isReadmeVisible = false;

    [ObservableProperty]
    private string _readmeHtml = "";

    [ObservableProperty]
    private string _readmeButtonText = "READMEを表示";

    [RelayCommand]
    private async Task ToggleReadmeAsync()
    {
        if (IsReadmeVisible)
        {
            IsReadmeVisible = false;
            ReadmeButtonText = "READMEを表示";
        }
        else
        {
            await LoadReadmeAsync();
            IsReadmeVisible = true;
            ReadmeButtonText = "Diffに戻る";
        }
    }

    private async Task LoadReadmeAsync()
    {
        try
        {
            var repoPath = _gitService.GetInternalRepoPath();
            var readmePath = System.IO.Path.Combine(repoPath, "README.md");
            if (!System.IO.File.Exists(readmePath)) readmePath = System.IO.Path.Combine(repoPath, "readme.md");

            if (System.IO.File.Exists(readmePath))
            {
                var content = await System.IO.File.ReadAllTextAsync(readmePath);
                ReadmeHtml = ConvertMarkdownToHtml(content);
            }
            else
            {
                ReadmeHtml = "<html><body><h3>README.md not found</h3></body></html>";
            }
        }
        catch (Exception ex)
        {
            ReadmeHtml = $"<html><body><h3>Error</h3><p>{ex.Message}</p></body></html>";
        }
    }

    private string ConvertMarkdownToHtml(string md)
    {
        var html = System.Net.WebUtility.HtmlEncode(md);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<html><body style='font-family: sans-serif; padding: 10px;'>");
        
        var lines = md.Split('\n');
        bool inCodeBlock = false;
        
        foreach (var originalLine in lines)
        {
            var line = originalLine.TrimEnd();
            if (line.StartsWith("```"))
            {
                inCodeBlock = !inCodeBlock;
                sb.AppendLine(inCodeBlock ? "<pre style='background:#f0f0f0; padding:10px;'>" : "</pre>");
                continue;
            }
            if (inCodeBlock)
            {
                sb.AppendLine(System.Net.WebUtility.HtmlEncode(line));
                continue;
            }

            if (line.StartsWith("# ")) sb.AppendLine($"<h1>{System.Net.WebUtility.HtmlEncode(line.Substring(2))}</h1>");
            else if (line.StartsWith("## ")) sb.AppendLine($"<h2>{System.Net.WebUtility.HtmlEncode(line.Substring(3))}</h2>");
            else if (line.StartsWith("- ")) sb.AppendLine($"<li>{System.Net.WebUtility.HtmlEncode(line.Substring(2))}</li>");
            else sb.AppendLine($"{System.Net.WebUtility.HtmlEncode(line)}<br/>");
        }
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    // Commands
    
    /// @brief コンストラクタ
    public MainViewModel(GitService gitService, GitHubService gitHubService, StateService stateService, ToolDetector toolDetector, SettingsService settingsService)
    {
        _gitService = gitService;
        _gitHubService = gitHubService;
        _stateService = stateService;
        _toolDetector = toolDetector;
        _settingsService = settingsService;
    }

    /// @brief 状態を更新するコマンド (UIバインド用)
    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private Task RefreshAsync() => RefreshInternalAsync(true);

    /// @brief 初期化
    public async Task InitializeAsync()
    {
        // 1. Dependency Check
        await CheckDependenciesAsync();
        
        // 2. Load
        _settingsService.Load();
        LoadRecentRepositories();
        await RefreshInternalAsync(false);
    }
    
    private async Task CheckDependenciesAsync()
    {
        try
        {
            // Check Git
            using var gitProc = new Process();
            gitProc.StartInfo = new ProcessStartInfo("git", "--version") { UseShellExecute = false, CreateNoWindow = true };
            gitProc.Start();
            await gitProc.WaitForExitAsync();
            if (gitProc.ExitCode != 0) 
            {
                MessageBox.Show("Git が正しくインストールされていないか、PATHに通っていません。\nGitをインストールしてください。", "致命的エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            // Check gh
            using var ghProc = new Process();
            ghProc.StartInfo = new ProcessStartInfo("gh", "--version") { UseShellExecute = false, CreateNoWindow = true };
            ghProc.Start();
            await ghProc.WaitForExitAsync();
             if (ghProc.ExitCode != 0) 
            {
                // Just log header? Or flag?
            }
        }
        catch
        {
             // Ignore for now or handle gracefully
        }
    }

    private async Task RefreshInternalAsync(bool showBusy)
    {
        // IsBusy is ObservableProperty. We should set it on UI thread.
        if (showBusy) Application.Current.Dispatcher.Invoke(() => IsBusy = true);
        try
        {
            Log("状態を更新しています...", false);
            
            // Background Fetch
            var repoPath = _gitService.CurrentRepositoryPath;
            var status = await _gitService.GetStatusAsync();
            var currentState = await _stateService.GetCurrentStateAsync();
            var changes = status.Changes;
            var branches = await _gitService.GetLocalBranchesAsync();
            
            // UI Update
            Application.Current.Dispatcher.Invoke(() =>
            {
                RepositoryPath = repoPath;
                CurrentBranch = status.Branch;
                CurrentState = currentState;
                
                Changes.Clear();
                foreach (var c in changes) Changes.Add(c);

                // Branch List Update
                Branches.Clear();
                foreach (var b in branches) Branches.Add(b);
                Branches.Add("[作成...]");
                
                _isChangingBranch = true;
                SelectedBranch = status.Branch;
                _isChangingBranch = false;
            });
            Log("状態更新完了: " + StatusMessage, false);
            Application.Current.Dispatcher.Invoke(NotifyCommandsCanExecuteChanged);
        }
        catch (Exception ex)
        {
            Log("更新エラー: " + ex.Message, true);
        }
        finally
        {
            if (showBusy) Application.Current.Dispatcher.Invoke(() => IsBusy = false);
        }
    }

    /// @brief コミットを実行するコマンド
    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task CommitAsync()
    {
        if (string.IsNullOrWhiteSpace(CommitMessage))
        {
            MessageBox.Show("commit message を入力してください", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Check Staged
        bool anyStaged;
        lock(Changes) { anyStaged = Changes.Any(c => c.IsStaged); }
        
        if (!anyStaged)
        {
            var res = MessageBox.Show("ステージされているファイルがありません。\nすべての変更をステージしてコミットしますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                await StageAllInternalAsync();
            }
            else
            {
                return;
            }
        }

        IsBusy = true; // Block UI for commit
        try
        {
            // Wait for any background staging tasks
            await _gitLock.WaitAsync();
            try 
            {
                Log("Commit を実行します...", false);
                var result = await _gitService.CommitAsync(CommitMessage, CommitBody);
                if (result.Success)
                {
                    Log("Commit 完了", false);
                    CommitMessage = "";
                    CommitBody = "";
                    await RefreshInternalAsync(true); // Commit refreshes with Busy state
                }
                else
                {
                    Log("Commit 失敗:\n" + result.StandardError, true);
                }
            }
            finally
            {
                _gitLock.Release();
            }
        }
        catch (Exception ex)
        {
            Log("Commit エラー: " + ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCommit() => IsNotBusy && !IsRebaseMode && CurrentState.HasFlag(RepoState.Dirty);

    /// @brief 内部用: ファイルリストをステージする (ロック確保済みであることを前提としない、内部でロックする)
    /// @param paths ステージ対象ファイル
    /// @return 成功したかどうか
    private async Task StageFilesInternalAsync(List<string> paths)
    {
        if (!paths.Any()) return; 

        // Optimistic Update (UI Thread)
        Application.Current.Dispatcher.Invoke(() => 
        {
             foreach(var p in paths)
             {
                 var entry = Changes.FirstOrDefault(c => c.FilePath == p);
                 if(entry != null) entry.IsStaged = true;
             }
        });
        
        // Background Op
        Interlocked.Increment(ref _pendingOperations);
        try
        {
            await _gitLock.WaitAsync();
            try
            {
                Log($"ステージング実行 ({paths.Count} files)...", false);
                foreach(var path in paths)
                {
                     await _gitService.StageFileAsync(path);
                }
                Log("ステージング完了", false);
            }
            finally
            {
                _gitLock.Release();
            }
        }
        finally
        {
            if (Interlocked.Decrement(ref _pendingOperations) == 0)
            {
                await RefreshInternalAsync(false);
            }
        }
    }

    /// @brief 内部用: 全ファイルをステージする (呼び出し元で待機可能)
    private async Task StageAllInternalAsync()
    {
         List<string> paths;
         lock(Changes) { paths = Changes.Select(c => c.FilePath).ToList(); }
         await StageFilesInternalAsync(paths);
    }

    /// @brief Pushを実行するコマンド
    [RelayCommand(CanExecute = nameof(CanPush))]
    private async Task PushAsync()
    {
        IsBusy = true;
        try
        {
            Log("Push を実行します...", false);
            
            var result = CurrentState.HasFlag(RepoState.NoUpstream) 
                ? await _gitService.PushAsync(CurrentBranch, true)
                : await _gitService.PushAsync(CurrentBranch, false); // Default push

            if (result.Success)
            {
                Log("Push 完了", false);
                await RefreshInternalAsync(true); // Push refreshes with Busy state
            }
            else
            {
                Log("Push 失敗:\n" + result.StandardError, true);
                
                if (result.StandardError.Contains("rejected") || result.StandardError.Contains("failed to push"))
                {
                    MessageBox.Show("Push が拒否されました。\nリモートに新たな変更が含まれています。\n先に Pull (Rebase) を行ってください。", "Push エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            Log("Push エラー: " + ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanPush() => IsNotBusy && !IsRebaseMode && (CurrentState.HasFlag(RepoState.Unpushed) || CurrentState.HasFlag(RepoState.NoUpstream));

    /// @brief Pullを実行するコマンド
    [RelayCommand(CanExecute = nameof(CanPull))]
    private async Task PullAsync()
    {
        // Dirty check logic specified in plan
        if (CurrentState.HasFlag(RepoState.Dirty))
        {
            var res = MessageBox.Show(
                "commit されていない変更があります。\n\n" +
                "・[はい] commit してから続ける (UIでcommitしてください)\n" +
                "・[いいえ] stash して pull する\n" + 
                "・[キャンセル] 中止",
                "pull の前に確認",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Cancel) return;
            if (res == MessageBoxResult.Yes)
            {
                MessageBox.Show("Commit を実行してから再度 Pull してください。", "案内");
                return;
            }
            if (res == MessageBoxResult.No) // "stash"
            {
                IsBusy = true;
                try 
                {
                    Log("Stash を実行しています...", false);
                    var stashRes = await _gitService.StashAsync("auto-stash before pull");
                    if (!stashRes.Success)
                    {
                        Log("Stash 失敗:\n" + stashRes.StandardError, true);
                        IsBusy = false;
                        return;
                    }
                    Log("Stash 完了", false);
                }
                catch (Exception ex)
                {
                    Log("Stash エラー: " + ex.Message, true);
                    IsBusy = false;
                    return;
                }
            }
        }

        IsBusy = true;
        try
        {
            Log("Pull --rebase を実行しています...", false);
            var result = await _gitService.PullRebaseAsync();
            if (result.Success)
            {
                Log("Pull 完了", false);
                await RefreshInternalAsync(true);
            }
            else
            {
                Log("Pull 失敗 (Conflict 発生の可能性):\n" + result.StandardError, true);
                await RefreshInternalAsync(true);
                
                if (CurrentState.HasFlag(RepoState.Rebase))
                {
                     MessageBox.Show("Pull に失敗しました。Rebase 中に Conflict が発生しました。\n解決モードに移行します。", "Conflict 発生", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex) 
        {
            Log("Pull エラー: " + ex.Message, true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanPull() => IsNotBusy && !IsRebaseMode;

    /// @brief PRを作成するコマンド
    [RelayCommand(CanExecute = nameof(CanCreatePR))]
    private async Task CreatePRAsync()
    {
        // 1. Auth Check
        if (CurrentState.HasFlag(RepoState.AuthNg))
        {
             var loginRes = MessageBox.Show("gh の認証が必要です。ログインしますか？\n(ブラウザが開きます)", "認証の確認", MessageBoxButton.YesNo);
             if (loginRes == MessageBoxResult.Yes)
             {
                 IsBusy = true;
                 await _gitHubService.RunAuthLoginAsync();
                 await RefreshAsync();
                 IsBusy = false;
             }
             return;
        }

        // 2. Logic: Upstream/Unpushed -> Push first
        if (CurrentState.HasFlag(RepoState.NoUpstream) || CurrentState.HasFlag(RepoState.Unpushed))
        {
             var pushRes = MessageBox.Show("PR作成の前に Push が必要です。\nPush して続行しますか？", "Push の確認", MessageBoxButton.YesNo);
             if (pushRes == MessageBoxResult.No) return;

             IsBusy = true;
             try
             {
                var pResult = CurrentState.HasFlag(RepoState.NoUpstream) 
                    ? await _gitService.PushAsync(CurrentBranch, true)
                    : await _gitService.PushAsync(CurrentBranch, false);
                
                if (!pResult.Success)
                {
                    Log("Push 失敗のため PR 作成を中止しました。", true);
                    IsBusy = false;
                    return;
                }
             }
             catch
             {
                 IsBusy = false; 
                 return;
             }
        }

        // 3. Create PR
        IsBusy = true;
        try
        {
            var lastCommitMsg = await _gitService.GetLastCommitMessageAsync();
            var lines = lastCommitMsg.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var defaultTitle = lines.FirstOrDefault() ?? CurrentBranch; 
            var defaultBody = lines.Length > 1 ? string.Join(Environment.NewLine, lines.Skip(1)) : "Created by Simple PR Client";

            bool? dialogResult = false;
            string prTitle = defaultTitle;
            string prBody = defaultBody;
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                var prWindow = new PRInputWindow(defaultTitle, defaultBody);
                prWindow.Owner = Application.Current.MainWindow; 
                dialogResult = prWindow.ShowDialog();
                if (dialogResult == true)
                {
                    prTitle = prWindow.PRTitle;
                    prBody = prWindow.PRBody;
                }
            });

            if (dialogResult != true)
            {
                IsBusy = false;
                return;
            }
            
            var baseBranch = await _gitHubService.GetDefaultBranchAsync();
            
            Log("PR を作成しています...", false);
            var result = await _gitHubService.CreatePullRequestAsync(prTitle, prBody, baseBranch, CurrentBranch);
            
            if (result.Success)
            {
                Log("Pull Request 作成完了:\n" + result.StandardOutput, false);
                var url = result.StandardOutput.Trim(); 
                if (url.StartsWith("https://"))
                {
                     var open = MessageBox.Show($"PRを作成しました。\n{url}\n\nGitHubで開きますか？", "成功", MessageBoxButton.YesNo);
                     if (open == MessageBoxResult.Yes)
                     {
                         _toolDetector.OpenFileInDefaultEditor(url); 
                     }
                }
            }
            else
            {
                var error = result.StandardError;
                Log("PR 作成失敗:\n" + error, true);
                
                if (error.Contains("GraphQL: Resource not accessible by personal access token") || 
                    error.Contains("scope"))
                {
                    var res = MessageBox.Show(
                        "GitHubの権限不足によりPRを作成できませんでした。\n" +
                        "これは `repo` スコープが不足している場合に発生します。\n\n" +
                        "今すぐ認証を更新(Refresh)しますか？\n" +
                        "([はい]を押すと黒い画面が開きます)\n\n" +
                        "【操作手順】\n" +
                        "1. 黒い画面で英語の質問が出ます\n" +
                        "   'Authenticate Git with your GitHub credentials? (Y/n)'\n" +
                        "2. キーボードで「Y」と入力し、Enterキーを押してください\n" +
                        "3. ブラウザが開くので、認証(Authorize)ボタンを押してください",
                        "権限エラーと修正", 
                        MessageBoxButton.YesNo, 
                        MessageBoxImage.Information); 
                        
                    if (res == MessageBoxResult.Yes)
                    {
                        IsBusy = true;
                        try 
                        {
                            Log("認証更新を実行中 (黒い画面に従ってください)...", false);
                            var authRes = await _gitHubService.RefreshAuthAsync();
                            if (authRes.Success)
                            {
                                MessageBox.Show("認証更新が完了しました。\n再度 PR 作成を試してください。", "成功");
                            }
                            else
                            {
                                Log("認証更新失敗 (画面が閉じられたかエラー): " + authRes.ExitCode, true);
                            }
                        }
                        finally { IsBusy = false; }
                    }
                }
            }
        }
        catch (Exception ex)
        {
             Log("PR エラー: " + ex.Message, true);
        }
        finally
        {
            IsBusy = false;
            await RefreshInternalAsync(true);
        }
    }

    private bool CanCreatePR() => IsNotBusy && !IsRebaseMode;

    /// @brief Rebaseを中止するコマンド
    [RelayCommand(CanExecute = nameof(IsRebaseMode))]
    private async Task AbortRebaseAsync()
    {
        var res = MessageBox.Show("Rebase を中止(Abort)しますか？\n変更内容は破棄され、Pull前の状態に戻ります。", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res == MessageBoxResult.No) return;

        IsBusy = true;
        try
        {
            Log("Rebase Abort を実行...", false);
            var result = await _gitService.RebaseAbortAsync();
            if (result.Success) Log("Abort 完了", false); else Log("Abort 失敗:\n" + result.StandardError, true);
            await RefreshInternalAsync(true);
        }
        finally { IsBusy = false; }
    }

    /// @brief Rebaseを継続するコマンド
    [RelayCommand(CanExecute = nameof(IsRebaseMode))]
    private async Task ContinueRebaseAsync()
    {
        IsBusy = true;
        try
        {
            Log("Rebase Continue を実行...", false);
            var result = await _gitService.RebaseContinueAsync();
            if (result.Success) Log("Continue 完了", false); else Log("Continue 失敗 (まだConflictがある可能性があります):\n" + result.StandardError, true);
            await RefreshInternalAsync(true);
        }
        finally { IsBusy = false; }
    }
    
    /// @brief Visual Studioを開くコマンド
    [RelayCommand]
    private void OpenVS()
    {
        if (string.IsNullOrEmpty(RepositoryPath)) return; 
        
        bool success = _toolDetector.OpenInVisualStudio(RepositoryPath);
        if (!success)
        {
            // Fallback
            _toolDetector.OpenFileInDefaultEditor(RepositoryPath);
        }
    }

    /// @brief ログをクリップボードにコピー
    [RelayCommand]
    private void CopyLog()
    {
        var allText = string.Join(Environment.NewLine, Logs.Select(x => x.ToString()));
        Clipboard.SetText(allText);
        MessageBox.Show("ログをクリップボードにコピーしました。", "完了");
    }

    /// @brief 全ファイルをステージするコマンド (全選択)
    [RelayCommand]
    private void StageAll()
    {
        // Fire and Forget
        _ = StageAllInternalAsync();
    }

    /// @brief 全ファイルのステージ解除コマンド (全解除)
    [RelayCommand]
    private void UnstageAll()
    {
        // Optimistic Update
        foreach (var c in Changes) c.IsStaged = false;

        Interlocked.Increment(ref _pendingOperations);
        Task.Run(async () => 
        {
            await _gitLock.WaitAsync();
            try
            {
                Log("全ファイルをUnstage(全解除)しています...", false);
                List<string> paths;
                lock(Changes) { paths = Changes.Select(c => c.FilePath).ToList(); }

                foreach(var path in paths)
                {
                     await _gitService.UnstageFileAsync(path);
                }
                Log("全ファイルのUnstage完了", false);
            }
            finally
            {
                _gitLock.Release();
                if (Interlocked.Decrement(ref _pendingOperations) == 0)
                {
                    await RefreshInternalAsync(false);
                }
            }
        });
    }

    /// @brief 個別ファイルをステージ切り替え (バックグラウンド・キュー待ち)
    [RelayCommand]
    private void ToggleStage(FileChangeEntry? entry)
    {
        if (entry == null) return; 
        
        // Optimistic Update (Flip local state)
        // Entry is ObservableObject, so UI updates instantly.
        bool newState = !entry.IsStaged;
        entry.IsStaged = newState; // Flip

        Interlocked.Increment(ref _pendingOperations);
        Task.Run(async () => 
        {
            await _gitLock.WaitAsync();
            try
            {
                ProcessResult result;
                if (!newState) // target is Unstaged
                {
                    Log($"Unstaging {entry.FilePath}...", false);
                    result = await _gitService.UnstageFileAsync(entry.FilePath);
                }
                else
                {
                    Log($"Staging {entry.FilePath}...", false);
                    result = await _gitService.StageFileAsync(entry.FilePath);
                }

                if (!result.Success)
                {
                    Log($"Staging操作失敗: {result.StandardError}", true);
                }
            }
            finally
            {
                _gitLock.Release();
                if (Interlocked.Decrement(ref _pendingOperations) == 0)
                {
                    await RefreshInternalAsync(false);
                }
            }
        });
    }

    private async Task RunGitOperationInBackgroundAsync(Func<Task> action)
    {
        // Acquire lock but DO NOT set IsBusy
        await _gitLock.WaitAsync();
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log("Background Task Error: " + ex.Message, true);
        }
        finally
        {
            _gitLock.Release();
        }
    }

    // Log Expansion
    [ObservableProperty]
    private bool _isLogExpanded;

    private void Log(string message, bool isError)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add(new LogEntry { Message = message, IsError = isError });
            if (isError) IsLogExpanded = true;
             // Scroll to bottom logic usually in View code-behind
        });
    }

    private void NotifyCommandsCanExecuteChanged()
    {
        CommitCommand.NotifyCanExecuteChanged();
        PushCommand.NotifyCanExecuteChanged();
        PullCommand.NotifyCanExecuteChanged();
        CreatePRCommand.NotifyCanExecuteChanged();
        AbortRebaseCommand.NotifyCanExecuteChanged();
        ContinueRebaseCommand.NotifyCanExecuteChanged();
    }
}
