# Simple PR Client (GitTooljp)

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![License](https://img.shields.io/badge/License-MIT-blue.svg)
![Status](https://img.shields.io/badge/Status-Development-orange)

> **「Gitを隠さないが、事故らせない」**
> 初学者がCLIを使わずに、安全にPull Request作成まで到達できるGitクライアント。

## 📖 概要

**Simple PR Client** は、「Gitの操作が怖い」「コマンドラインが難しい」と感じる学生や初心者のために設計されたWindows用Gitクライアントです。

高機能すぎる既存のGUIツールとは異なり、**「コミットして、Pushして、PRを作る」** という一連の流れ（Happy Path）を迷わず安全に行えることに特化しています。

## ✨ 主な特徴

| 機能 | 説明 |
| :--- | :--- |
| 🛡️ **安全第一** | `git pull --rebase` を強制し、作業ツリーが汚れている状態での操作をブロック。初心者が陥りやすい事故を防ぎます。 |
| 👁️ **リッチなDiff表示** | 変更内容を色分けされたHTML形式で見やすく表示。何が変わったかが一目でわかります。 |
| 🚀 **GitHubネイティブ** | GitHub CLI (`gh`) を内部で呼び出し、ブラウザを開かずにPull Requestを作成可能。 |
| 🛠️ **開発ツール連携** | コンフリクト発生時はVisual Studioと連携し、スムーズな解決を支援します。 |

## 🔄 ワークフロー

```mermaid
graph LR
    A[作業 (Work)] -->|Save| B[変更確認 (Check)]
    B -->|Commit| C[コミット (Commit)]
    C -->|Push| D[プッシュ (Push)]
    D -->|Create PR| E[Pull Request作成]
    style A fill:#f9f,stroke:#333,stroke-width:2px
    style E fill:#bbf,stroke:#333,stroke-width:2px
```

## 📦 必要要件

このツールを使用するには以下の環境が必要です：

- **OS**: Windows 10 / 11 (64bit)
- **Runtime**: [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Git**: [Git for Windows](https://git-scm.com/download/win)
- **GitHub CLI**: [GitHub CLI (gh)](https://cli.github.com/)

## 🚀 始め方

1. **リポジトリをクローン**
   ```bash
   git clone https://github.com/yamak/GitTooljp.git
   ```
2. **ビルド**
   Visual Studio 2022以降でソリューションファイル `SimplePRClient.csproj` を開き、ビルドしてください。
3. **実行**
   生成された `SimplePRClient.exe` を実行します。

## 🎮 使い方

<details>
<summary><strong>1. リポジトリの選択</strong></summary>

起動時に `.git` フォルダを含むディレクトリを選択するか、自動検出されたリポジトリを開きます。
</details>

<details>
<summary><strong>2. 変更の確認とコミット</strong></summary>

左側のパネルで変更されたファイルを選択し、中央のDiffビューで内容を確認します。
コミットメッセージを入力し、「コミット」ボタンを押します。
</details>

<details>
<summary><strong>3. PushとPull Request</strong></summary>

コミットが完了したら「Push」を実行。
その後、アクションパネルから「Pull Request作成」をクリックするだけで、GitHub上にPRが作成されます。
</details>

<details>
<summary><strong>4. コンフリクト解決</strong></summary>

Pull時に競合（コンフリクト）が発生した場合、自動的に「復旧モード」になります。
Visual Studioを起動して解決し、「Rebase Continue」を押すだけで作業を再開できます。
</details>

## 🛠️ 技術スタック

- **言語**: C#
- **フレームワーク**: WPF (.NET 8.0)
- **ライブラリ**: CommunityToolkit.Mvvm
- **外部依存**: git, gh, vswhere

## 🗺️ ロードマップ

- [x] リポジトリ検出・選択
- [x] 変更状態の表示 (Staged/Unstaged)
- [x] コミット作成
- [x] Push / Pull (Rebase)
- [x] HTMLベースのリッチDiff表示
- [ ] Pull Request 作成 (GitHub CLI連携)
- [ ] Visual Studio連携によるコンフリクト解決支援

---

[MIT License](LICENSE)
