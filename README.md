# AI Usage Widget

Codex / GitHub Copilot / Claude の残り使用枠を一覧する、日本語の Windows 常駐ウィジェットです。C# / .NET 10 / WPF で実装しています。

## 起動

配布フォルダー `artifacts\win-x64` 全体を任意の場所に置き、`AiUsageWidget.exe` を起動します。.NET ランタイムの追加インストールは不要です。`bridge` と Copilot ランタイムなど、隣接ファイルも必要です。Claude 連携後にフォルダーを移動する場合は、先に連携を解除して移動後に再登録してください。

- 初期状態は最前面表示・60秒更新・残り20%と10%で通知です。
- モデル専用枠・無制限枠はカードの「その他の枠」から展開できます。数値は残り率、バーは使用率です。
- ヘッダーをドラッグして移動し、「−」で通知領域へ収納します。通知領域のアイコンをクリックすると再表示します。
- 終了はトレイの「終了」。自動起動は設定画面から有効にできます。
- 「履歴」で7日/30日の使用枠推移、Codex の日別トークン履歴、Copilot の使用済みクレジット履歴を表示します。履歴の点は実際の観測値で、欠測やリセットをまたいだ線は補間しません。

## 接続

通常の Windows ユーザーとして実行してください。別ユーザー・サンドボックス内では既存ログインを利用できない場合があります。モデルへの生成リクエストは送信しません。

### Codex

Codex アプリまたは CLI をインストールして `codex login` で ChatGPT アカウントへログインしてください。設定済みパス、PATH、標準のアプリインストール先から実行ファイルを検出します。APIキーの課金額は対象外です。

App Server の `account/read`、`account/rateLimits/read`、`account/usage/read` を呼び出します。枠の種類や期間は取得値を使用します。

### GitHub Copilot

公式 .NET SDK とバージョンを揃えたランタイムを同梱します。既存の Copilot CLI ログインを利用します。未認証時は Copilot CLI の `/login` で個人契約のアカウントへログインしてください。

`Account.GetQuotaAsync` の残り率を使用します。無制限枠は無制限と表示します。課金方式により単位が異なるため、生の枠数を「リクエスト回数」とは表示しません。SDKが現在時刻を補う場合など、確かなリセット日時が得られない枠では「取得不可」と表示します。

### Claude / Claude Code

1. Claude Code でサブスクリプションのアカウントへ `/login` します。
2. ウィジェットの設定から「連携を有効にする」を選びます。
3. Claude Code は `claude` コマンドでログインすると、既存のOAuthログインを使って使用枠を取得します。

Claude Code のローカル資格情報からアクセストークンを読み、使用量エンドポイントへ読み取りリクエストだけを送ります。再起動直後などで認証ファイルが未生成または期限切れの場合は、`claude auth status --json` で既存ログインを確認して資格情報を更新します。会話や生成リクエストは行いません。未ログイン、401/403、または使用枠を返さない契約は「未接続」と表示します。429や通信障害は「更新失敗」とし、共通のバックオフで再試行します。

更新間隔は設定画面でサービスごとに指定できます。CodexとGitHub Copilotは15～3600秒、Claude Codeは使用量APIの頻度制限を避けるため300～3600秒です。

以前のステータスライン連携を使用していた環境では、更新時に元の設定へ戻してください。バックアップは下記のデータフォルダーにあります。

## 保存先

`%LOCALAPPDATA%\AiUsageWidget`

| ファイル | 内容 |
|---|---|
| `settings.json` | 表示・更新・パス設定 |
| `history.db` | 約31日分の観測値、通知の重複抑止記録 |
| `claude/*.json` | セッション別の使用枠スナップショット |
| `claude-integration.json` | 旧ステータスライン連携の復元情報（移行後は未使用） |
| `claude-settings-backup.json` | 登録前の設定の復旧用バックアップ |

会話本文やトークンなどの認証秘密は履歴へ保存しません。Codex のアカウント識別子はローカル履歴に保存します。各サービスのログインは公式ツールが管理します。テスト時は `AI_USAGE_WIDGET_HOME` で保存先を分離できます。

## 開発・検証

Windows x64 と .NET 10 SDK を使用します。依存関係の復元と、初回ビルドでの公式 Copilot ランタイム取得にはインターネット接続が必要です。

```powershell
dotnet restore AiUsageWidget.slnx
dotnet build AiUsageWidget.slnx
dotnet test AiUsageWidget.slnx
.\publish.ps1
.\artifacts\win-x64\AiUsageWidget.exe
```

```powershell
# 読み取り専用の実接続診断。結果には使用率が含まれます。
.\artifacts\win-x64\AiUsageWidget.exe --probe D:\temp\usage-probe.json
# デモ表示（サンプルであることを画面に表示）
.\artifacts\win-x64\AiUsageWidget.exe --demo
# 自身のWPF画面をPNGに描画して終了
.\artifacts\win-x64\AiUsageWidget.exe --demo --capture D:\temp\widget.png
```

## 拡張

スナップ配置: Windows標準のタイトルバーと最大化ボタンを使用します。Windows 11では最大化ボタンにマウスを重ねるか、ウィジェットを選択して Win + Z を押すと、スナップレイアウトを選択できます。Windowsの設定でスナップウィンドウが有効である必要があります。右端専用領域の確保機能は廃止しました。タイトルバーの最小化はタスクバーへ、アプリ内の「−」と閉じるボタンはトレイへ収納します。

各サービスのカードで「使用可能なモデル」を開くと、CLI が返すモデル名・識別子・説明を確認できます。Codex は `model/list`（ページング対応）、Copilot は SDK の `ListModelsAsync`、Claude Code は stream-json の初期化応答を使用します。Auto / Default などの選択肢も含みます。Web・IDE のモデル一覧とは異なる場合があります。

一覧は取得成功後1時間キャッシュし、失敗時は5分後に再試行します。モデル取得の失敗で使用枠の取得は停止しません。Claude Code は初期化要求のみを送り、ユーザーメッセージや生成要求は送信せず、取得後に起動した子プロセスを終了します。Claude CLI の互換性変更で取得できなくなった場合は一覧内に状態を表示します。

各サービスのカードで「スキル」「プラグイン」「MCP」を開くと、有効または設定済みの項目を表で確認できます。Codex は App Server とローカルのプラグイン情報、Copilot は公式 SDK、Claude Code はユーザースキルと CLI の一覧コマンドを使用します。Claude Code の内蔵スラッシュコマンドはスキル数に含めません。これらの一覧も1時間キャッシュし、取得失敗で使用枠の更新を止めません。

「詳細」には、サービスごとのインストラクションファイルのパスと、スキル・プラグイン・MCP設定を参照するディレクトリも表示します。ユーザープロファイル部分は `~`、プロジェクト固有ファイルは `<プロジェクト>` 起点で表示します。`CODEX_HOME` / `CLAUDE_CONFIG_DIR` がユーザープロファイル外を指す場合は実際の絶対パスを表示します。

実施済みの検証と未実施項目は [VALIDATION.md](VALIDATION.md) を参照してください。

`IUsageProvider` と `ProviderDescriptor` を実装し、アプリのプロバイダー登録へ追加します。UI・通知・保存処理は共通の `UsageSnapshot` と `QuotaWindow` を扱います。`TokenHistory` は対応サービスのみ提供します。現在はサービスごとに1アカウントです。

## 仕様参照

- [Codex App Server](https://learn.chatgpt.com/docs/app-server)
- [Copilot SDK 使用量と使用枠](https://github.com/github/copilot-sdk/blob/main/docs/features/usage-and-billing.md)
- [Claude Code 認証](https://code.claude.com/docs/en/authentication)

表示: 各カードにプラン / SKU、残り％、使用量 / 上限を併記します。Codex は account/read の planType、Copilot は access_type_sku、Claude Code はローカル認証情報の subscriptionType を表示します。取得できない実数は推計せず取得不可とします。Copilot の単位は token_based_billing に従い、判別不能なら単位不明とします。
SDK 1.0.13 の認証型とランタイムの不一致に限り、CopilotMetadata は同じ SDK 接続の読み取り RPC を互換取得します。SDK 更新時にはこの互換処理を再検証してください。
