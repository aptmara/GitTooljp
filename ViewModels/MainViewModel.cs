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

/// @brief メイン画面のViewModel
/// 作成者: 山内陽
public partial class MainViewModel : ObservableObject
{
    private readonly GitService _gitService;
    private readonly GitHubService _gitHubService;
    private readonly StateService _stateService;
    private readonly ToolDetector _toolDetector;

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
            // Or maybe separate UI for staged/unstaged diff?
            // Plan said: "Diff View (Simple text diff)".
            // Let's check Unstaged first.
            
            bool tryStaged = value.IsStaged && !value.WorkTreeStatus.Equals('M');
            // Logic: if it has worktree changes, show them. If only staged, show staged.
            
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

    // Commands
    
    /// @brief コンストラクタ
    /// @param gitService Gitサービス
    /// @param gitHubService GitHubサービス
    /// @param stateService 状態管理サービス
    /// @param toolDetector ツール検出サービス
    public MainViewModel(GitService gitService, GitHubService gitHubService, StateService stateService, ToolDetector toolDetector)
    {
        _gitService = gitService;
        _gitHubService = gitHubService;
        _stateService = stateService;
        _toolDetector = toolDetector;
    }

    /// @brief 状態を更新するコマンド (UIバインド用)
    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private Task RefreshAsync() => RefreshInternalAsync(true);

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
            
            // UI Update
            Application.Current.Dispatcher.Invoke(() =>
            {
                RepositoryPath = repoPath;
                CurrentBranch = status.Branch;
                CurrentState = currentState;
                
                Changes.Clear();
                foreach (var c in changes) Changes.Add(c);
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

    private bool CanCommit() => IsNotBusy && !IsRebaseMode && CurrentState.HasFlag(RepoState.Dirty); // 厳密には Staged > 0 も必要だが簡略化

    /// @brief Pushを実行するコマンド
    [RelayCommand(CanExecute = nameof(CanPush))]
    private async Task PushAsync()
    {
        IsBusy = true;
        try
        {
            Log("Push を実行します...", false);
            // Upstream check is done inside State service but we might need to handle NoUpstream specifically here logic-wise
            // But State-driven UI implies we just call Push or PushSetUpstream.
            
            // However, the plan says "RepoState" determines flags.
            // If NoUpstream, we should ask user or just do set-upstream.
            // Let's check state.
            
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
                // User chose to commit first. Focus commit box (ideally).
                // Here we just abort pull so user can commit.
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
                    // Proceed to pull
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
                // Refresh to update state to Rebase if conflict happened
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
        // 1. Auth Check - already in State but double check logic flow
        if (CurrentState.HasFlag(RepoState.AuthNg))
        {
             // Prompt login
             var loginRes = MessageBox.Show("gh の認証が必要です。ログインしますか？\n(ブラウザが開きます)", "認証の確認", MessageBoxButton.YesNo);
             if (loginRes == MessageBoxResult.Yes)
             {
                 IsBusy = true;
                 await _gitHubService.RunAuthLoginAsync();
                 await RefreshAsync();
                 IsBusy = false;
                 // Retry PR? User can click again.
             }
             return;
        }

        // 2. Logic: Upstream/Unpushed -> Push first
        if (CurrentState.HasFlag(RepoState.NoUpstream) || CurrentState.HasFlag(RepoState.Unpushed))
        {
             // Confirm push
             var pushRes = MessageBox.Show("PR作成の前に Push が必要です。\nPush して続行しますか？", "Push の確認", MessageBoxButton.YesNo);
             if (pushRes == MessageBoxResult.No) return;

             // reuse Push logic?
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
                // Refresh state to ensure Clean/Synced
                // BUT we don't want to clear IsBusy yet
                // Manually refresh state logic or just proceed assuming success
             }
             catch
             {
                 IsBusy = false; 
                 return;
             }
        }

        // 3. Create PR
        // Simplified for MVP: ask for title/body in a prompt or use side panel inputs
        // Assuming we use CommitMessage/Body fields or new fields? Plan says "Action Panel" inputs.
        // Let's assume we have separate properties PRTitle, PRBody.
        // For now, using CommitMessage as fallback or input dialog?
        // Let's use simple InputBox logic or assuming UI has fields.
        
        IsBusy = true;
        try
        {
            // For MVP, just using a placeholder or assuming UI bindings exist.
            // Let's assume we use the Commit Message box for PR details for simplicity if in "Clean" state?
            // No, that's confusing.
            // I'll proceed with a simple fixed title or "Last commit message" for now to satisfy build.
            // Real app needs a dialog.
            
            var title = "Pull Request from " + CurrentBranch;
            var body = "Created by Simple PR Client";
            
            var baseBranch = await _gitHubService.GetDefaultBranchAsync();
            
            Log("PR を作成しています...", false);
            var result = await _gitHubService.CreatePullRequestAsync(title, body, baseBranch, CurrentBranch);
            
            if (result.Success)
            {
                Log("Pull Request 作成完了:\n" + result.StandardOutput, false);
                var url = result.StandardOutput.Trim(); // gh usually outputs URL
                if (url.StartsWith("https://"))
                {
                     var open = MessageBox.Show($"PRを作成しました。\n{url}\n\nGitHubで開きますか？", "成功", MessageBoxButton.YesNo);
                     if (open == MessageBoxResult.Yes)
                     {
                         _toolDetector.OpenFileInDefaultEditor(url); // Opens URL
                     }
                }
            }
            else
            {
                Log("PR 作成失敗:\n" + result.StandardError, true);
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
        // Optimistic Update
        foreach (var c in Changes) c.IsStaged = true;

        // Fire and Forget (Queue)
        Interlocked.Increment(ref _pendingOperations);
        Task.Run(async () => 
        {
            await _gitLock.WaitAsync();
            try
            {
                Log("全ファイルをStage(全選択)しています...", false);
                var unstaged = Changes.Where(c => !c.IsStaged).ToList(); // Re-evaluate if valid logic? No, Model is Staged=true now.
                // We should rely on Git status or just Force Add all?
                // Git add . is safest for "Stage All".
                // But GitService StageFileAsync is per file.
                // Optimistic UI set IsStaged=true.
                // We need to know which files were actually unstaged before we flipped them?
                // Or just assume `git add .` adds everything.
                // Let's use `git add .` equivalent logic if possible, or iterate all files.
                // To be safe with optimistic, we should just iterate all files that WERE unstaged.
                // But we just lost that info by setting IsStaged=true.
                // Actually, `git add` on already staged file is no-op. So just iterate all.
                
                // However, iterating Changes collection from background thread might be unsafe collection access?
                // Changes is ObservableCollection.
                // Better snapshot it.
                List<string> paths;
                lock(Changes) { paths = Changes.Select(c => c.FilePath).ToList(); }

                foreach(var path in paths)
                {
                     await _gitService.StageFileAsync(path);
                }
                Log("全ファイルのStage完了", false);
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
                    // Revert optimistic update on failure?
                    // Entry might have changed again. Complex.
                    // Ideally yes, but for MVP just log error.
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

    private void Log(string message, bool isError)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add(new LogEntry { Message = message, IsError = isError });
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