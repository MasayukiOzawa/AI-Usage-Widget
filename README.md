# AI Usage Widget

OpenAI Codex / GitHub Copilot / Claude Code の残り使用枠を一覧する、日本語の Windows 常駐ウィジェットです。C# / .NET 10 / WPF で実装しています。

![AI Usage Widgetのデスクトップ表示](docs/images/ai-usage-widget.png)

## 起動

[GitHub Releases](../../releases) からWindows x64向けZIPをダウンロードして展開し、`AiUsageWidget.exe` を起動します。Release版のアプリ本体は.NET 10ランタイム、SQLite、Copilotランタイムを内包した単一の実行ファイルで、.NETランタイムの追加インストールは不要です。READMEと第三者ライセンス文書もZIPに同梱されます。

- 初期状態は最前面表示・残り20%と10%で通知です。更新間隔はOpenAI CodexとGitHub Copilotが60秒、Claude Codeが300秒です。Windowsログイン時の自動起動は初期状態ではオフです。
- モデル専用枠・無制限枠はカードの「その他の枠」から展開できます。数値は残り率、バーは使用率です。
- タイトルバーまたは上部の「AI USAGE」をドラッグして移動し、「−」で通知領域へ収納します。通知領域のアイコンをクリックすると再表示します。
- 終了はトレイの「終了」。自動起動は設定画面から有効にできます。
- 「履歴」で7日/30日の使用枠推移、Codex の日別トークン履歴、Copilot の使用済みクレジット履歴を表示します。履歴の点は実際の観測値で、欠測やリセットをまたいだ線は補間しません。

## 接続

通常の Windows ユーザーとして実行してください。別ユーザー・サンドボックス内では既存ログインを利用できない場合があります。モデルへの生成リクエストは送信しません。

### Codex

Codex アプリまたは CLI をインストールして `codex login` で ChatGPT アカウントへログインしてください。設定済みパス、PATH、標準のアプリインストール先から実行ファイルを検出します。APIキーの課金額は対象外です。

App Server の `account/read`、`account/rateLimits/read`、`account/usage/read` を呼び出します。枠の種類や期間は取得値を使用します。

### GitHub Copilot

公式 .NET SDK とバージョンを揃えたランタイムを同梱します。既存の Copilot CLI ログインを利用します。未認証時は Copilot CLI の `/login` で使用枠を確認するアカウントへログインしてください。本アプリは個人契約での利用を想定しています。

`Account.GetQuotaAsync` の残り率と使用量・上限を使用します。無制限枠は「無制限」と表示し、実数の使用量・上限は省略します。課金方式のメタデータに応じて「クレジット」「リクエスト」を区別し、判別できない場合は「単位不明」と表示します。GitHub Copilotのリセット時刻は画面に表示しません。

### Claude Code

1. Claude Codeを起動し、`/login` でサブスクリプションのアカウントへログインします。
2. 同じWindowsユーザーでウィジェットを起動します。既存のOAuth資格情報を使って、使用枠を定期取得します。

ウィジェット側の連携登録やステータスラインの設定は不要です。この実装はClaude Codeの資格情報を参照します。

Claude Code のローカル資格情報からアクセストークンを読み、使用量エンドポイントへ読み取りリクエストだけを送ります。ウィジェットは認証ファイルの更新やOAuthトークンの更新を行いません。認証ファイルがない・読み取れない・有効期限が切れた場合は「未接続」と表示するため、`claude auth login` で再ログインしてからウィジェットを更新してください。

#### 認証期限切れ時の挙動

Claude Codeを継続して実際に利用している間は、通常、Claude Code自身がOAuthトークンを更新します。Claude Codeを利用せずウィジェットだけを動かしている場合、保存済みアクセストークンの期限が切れると、使用枠の前回値を残したまま状態が「未接続」になります。Claude Desktopの利用だけでは、Claude Code用の資格情報が更新されるとは限りません。

`claude auth status` はログイン状態を確認するコマンドであり、期限切れトークンの更新や接続復旧を保証しません。「未接続」になった場合は、PowerShellなどで次のコマンドを実行し、ブラウザー認証を完了してください。

```powershell
claude auth login
```

再ログイン後、ウィジェット上部の更新ボタンを押すか、次回の自動更新を待つと「接続済み」に戻ります。リフレッシュトークンが有効な場合は、`claude` を起動して実際に1回リクエストを送ることでもClaude Code自身が資格情報を更新できます。単にCLIを起動して終了するだけでは更新されない場合があります。

会話や生成リクエストは行いません。未認証、401/403、使用枠が返らない場合は「未接続」と表示します。この接続はローカルOAuth資格情報と使用量エンドポイントに依存し、CLIやサービス側の仕様変更で取得できなくなる場合があります。

Claude Codeの5時間枠が未使用の場合、使用量APIはリセット時刻を返さないことがあります。この場合はリセット行を表示せず、利用開始後にAPIから時刻を取得できた時点で表示します。

更新間隔は設定画面でサービスごとに指定できます。CodexとGitHub Copilotは15～3600秒、Claude Codeは使用量APIの頻度制限を避けるため300～3600秒です。

各サービスを独立して更新します。通信失敗時は前回値があれば保持し、通常は60秒→120秒→300秒の間隔で再試行します。HTTP 429では300秒待ち、前回値がある場合は「前回の値」と表示します。上部の「↻」またはトレイの「更新」で使用枠を手動更新できますが、モデル・スキルなどの一覧キャッシュは解除しません。

以前のステータスライン連携を使用していた環境では、ウィジェットを終了してから次のコマンドで解除できます。バックアップは下記のデータフォルダーにあります。

```powershell
.\artifacts\win-x64\AiUsageWidget.exe --uninstall-claude
```

## 保存先

`%LOCALAPPDATA%\AiUsageWidget`

| ファイル | 内容 |
|---|---|
| `settings.json` | 表示・更新・パス設定 |
| `history.db` | 使用枠・トークン履歴、通知の重複抑止記録。新しい観測の保存時に31日より古い観測値を削除 |
| `claude/*.json` | 旧ステータスライン連携で収集したセッション別の使用枠 |
| `claude-integration.json` | 旧ステータスライン連携の復元情報（移行後は未使用） |
| `claude-settings-backup.json` | 旧ステータスライン連携の登録前設定のバックアップ |

会話本文やトークンなどの認証秘密は履歴へ保存しません。Codex のアカウント識別子はローカル履歴に保存します。各サービスのログインは公式ツールが管理します。テスト時は `AI_USAGE_WIDGET_HOME` で保存先を分離できます。

履歴にはCopilotのアカウント識別子、モデル・スキル・プラグイン・MCPの取得結果も含まれます。旧連携の設定バックアップには元の設定全文が含まれます。保存データや診断JSONを共有する際は内容を確認してください。

## 開発・検証

Windows x64 と .NET 10 SDK を使用します。依存関係の復元と、初回ビルドでの公式 Copilot ランタイム取得にはインターネット接続が必要です。

以下はリポジトリのルートで実行します。配布フォルダーへ上書きする前に、トレイの「終了」で実行中のウィジェットを終了してください。

`publish.ps1` の既定構成は `Release` です。既存の出力先を消去せず上書きするため、公開用の配布物を作る際は、必要なファイルを確認して既存の `artifacts\win-x64` を退避し、空の出力先へ発行してください。構成を指定する場合は `./publish.ps1 -Configuration Debug` のように実行します。

```powershell
dotnet restore AiUsageWidget.slnx
dotnet build AiUsageWidget.slnx
dotnet test AiUsageWidget.slnx
.\publish.ps1
.\artifacts\win-x64\AiUsageWidget.exe
```

```powershell
# 実接続診断。使用枠・プラン・モデル・拡張情報・エラーなどを保存します。
Start-Process -FilePath .\artifacts\win-x64\AiUsageWidget.exe -ArgumentList '--probe artifacts\usage-probe.json' -Wait
# サンプル使用枠でデモ表示
.\artifacts\win-x64\AiUsageWidget.exe --demo
# 自身のWPF画面をPNGに描画して終了
Start-Process -FilePath .\artifacts\win-x64\AiUsageWidget.exe -ArgumentList '--demo --capture artifacts\widget.png' -Wait
```

デモと画面キャプチャは通常起動版を終了してから実行してください。同一Windowsユーザーの通常ウィジェットは多重起動できません。`--demo` はサンプル使用枠を表示し、通常の定期取得は開始しませんが、モデル・拡張一覧のサンプルはありません。画面には実データと同じサービス名が表示されます。データを分離する場合は、専用のPowerShellで起動前に `$env:AI_USAGE_WIDGET_HOME = Join-Path $PWD 'artifacts\demo-data'` を設定してください。

`--probe` は各サービスを順番に取得してJSONを書き出し終了します。失敗したサービスのエラーもJSONに入るため、終了したことだけでは接続成功を判断できません。`--capture` はウィジェット自身のWPF画面を描画するもので、デスクトップ全体の撮影ではありません。

## GitHub Releases

`v` で始まるタグをGitHubへpushすると、GitHub Actionsがテストを実行し、Windows x64向けのReleaseを作成します。

実行可能プログラムと配布フォルダーはGitリポジトリへコミットしません。ローカル生成先の `artifacts`、GitHub Actions内の `publish` と `package` は `.gitignore` の対象です。配布はGitHub Releasesに添付されるZIPだけを使用します。

```powershell
git tag v1.0.0
git push origin v1.0.0
```

Releaseの `AiUsageWidget-v1.0.0-win-x64.zip` には、.NET 10ランタイム、SQLite、Copilotランタイムを内包した自己完結型の `AiUsageWidget.exe` と、README・第三者ライセンス文書が入ります。アプリ本体は単一の実行ファイルで、別途.NETランタイムをインストールする必要はありません。ネイティブ依存ファイルは実行時に.NETの一時展開先へ展開されます。

既存タグのReleaseを作成し直す場合は、GitHub Actionsの `Release` から `Run workflow` を選び、対象のタグ名を入力します。同名のZIPがある場合は置き換えられます。

## 詳細表示とウィンドウ操作

スナップ配置: Windows標準のタイトルバーと最大化ボタンを使用します。Windows 11では最大化ボタンにマウスを重ねるか、ウィジェットを選択して Win + Z を押すと、スナップレイアウトを選択できます。Windowsの設定でスナップウィンドウが有効である必要があります。タイトルバーの最小化ボタンはタスクバーへ最小化し、アプリ内の「−」とタイトルバーの閉じるボタンは通知領域へ収納します。

各サービスのカードで「詳細」→「使用可能なモデル」を開くと、CLI が返すモデル名・識別子を表で、説明をツールチップで確認できます。Codex は `model/list`（ページング対応）、Copilot は SDK の `ListModelsAsync`、Claude Code は stream-json の初期化応答を使用します。Auto / Default などの選択肢も含みます。Web・IDE のモデル一覧とは異なる場合があります。

一覧は取得成功後1時間キャッシュし、失敗時は5分後に再試行します。モデル取得の失敗で使用枠の取得は停止しません。Claude Code は初期化要求のみを送り、ユーザーメッセージや生成要求は送信せず、取得後に起動した子プロセスを終了します。Claude CLI の互換性変更で取得できなくなった場合は一覧内に状態を表示します。

各サービスの「詳細」内にある「スキル」「プラグイン」「MCP」を開くと、取得した項目を表で確認できます。Codex は App Server とローカルのプラグインキャッシュ、Copilot は公式 SDK、Claude Code はユーザースキルと CLI の一覧コマンドを使用します。Codexのプラグイン一覧はキャッシュからの検出結果であり、有効状態の確認ではありません。MCPの「設定済み」は接続成功を意味しません。Claude Code の内蔵スラッシュコマンドはスキル数に含めません。これらの一覧も1時間キャッシュし、取得失敗で使用枠の更新を止めません。

「詳細」には、サービスごとのインストラクションファイルのパスと、スキル・プラグイン・MCP設定を参照するディレクトリも表示します。ユーザープロファイル部分は `~`、プロジェクト固有ファイルは `<プロジェクト>` 起点で表示します。`CODEX_HOME` / `CLAUDE_CONFIG_DIR` がユーザープロファイル外を指す場合は実際の絶対パスを表示します。

「詳細」は初期状態で折りたたまれています。パス欄は参照場所の案内であり、実在確認や、そのプロジェクトでの読み込み確認ではありません。サービスの公式アイコンは同梱・表示せず、ウィンドウとトレイにはアプリ自身のアイコンを使用します。

「Windows ログイン時に起動」を保存すると、現在の実行ファイルをユーザーのRunレジストリへ登録します。ログイン後はウィジェットを表示して取得を開始します。配置フォルダーを移動する場合は、いったん自動起動を無効化して保存し、移動先で再設定してください。

## 検証記録と開発上の制約

[VALIDATION.md](VALIDATION.md) は2026年9月8日時点の検証記録です。記載されたテスト件数や旧ステータスライン連携の確認状況は、現在の実装の最新検証結果を示すものではありません。現在のテストは上記の `dotnet test` で実行してください。旧収集プログラムの検証は発行後に `.\scripts\verify-bridge.ps1` で実行できます。

`IUsageProvider` と `ProviderDescriptor` を実装し、アプリのプロバイダー登録へ追加します。UI・通知・保存処理は共通の `UsageSnapshot` と `QuotaWindow` を扱います。`TokenHistory` は対応サービスのみ提供します。現在はサービスごとに1アカウントです。

外部DLLの動的プラグイン読み込み、複数アカウント切替、API料金集計、使用枠のリセット操作、Windows以外のOSは対象外です。履歴の残り率・トークン数をサービス間で合算しません。

## 仕様参照

- [Codex App Server](https://learn.chatgpt.com/docs/app-server)
- [Copilot SDK 使用量と使用枠](https://github.com/github/copilot-sdk/blob/main/docs/features/usage-and-billing.md)
- [Claude Code 認証](https://code.claude.com/docs/en/authentication)

表示: 各カードは残り％と使用率バーを表示します。プラン / SKUは画面に表示しません。使用量 / 上限は実数を取得できる枠だけに表示し、両方とも不明な枠や無制限枠では省略します。Codexの使用量 / 上限は表示せず、日別トークン数は「履歴」で確認できます。Copilotの単位は課金方式のメタデータに従い、判別不能なら単位不明とします。
SDK 1.0.13 の認証型とランタイムの不一致に限り、CopilotMetadata は同じ SDK 接続の読み取り RPC を互換取得します。SDK 更新時にはこの互換処理を再検証してください。

## 第三者ライセンス

同梱するライブラリとSQLiteのライセンス情報は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。この文書と各ライセンス本文はビルド・発行先にも自動的に含まれます。

## ライセンス

AI Usage Widgetは [MIT License](LICENSE) で公開しています。
