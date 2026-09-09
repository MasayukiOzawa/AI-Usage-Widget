using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AiUsageWidget.Core;
namespace AiUsageWidget.App;
public sealed class SettingsWindow : Window
{
    public SettingsWindow(WidgetApp app)
    {
        Style = (Style)Application.Current.Resources[typeof(Window)];
        Title = "AI Usage Widget · 設定"; Width = 470; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new Thickness(22) }; Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(new TextBlock { Text = "表示と接続", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new(0,0,0,15) });
        var top = new CheckBox { Content = "最前面に表示", IsChecked = app.Settings.AlwaysOnTop };
        var notice = new CheckBox { Content = "残り 20%・10% で通知", IsChecked = app.Settings.Notifications };
        var startup = new CheckBox { Content = "Windows ログイン時に起動", IsChecked = app.Settings.AutoStart };
        body.Children.Add(top); body.Children.Add(notice); body.Children.Add(startup);
        Label("Codex 更新間隔（秒・15～3600）"); var codexInterval = new TextBox { Text = app.Settings.CodexRefreshSeconds.ToString() }; body.Children.Add(codexInterval);
        Label("GitHub Copilot 更新間隔（秒・15～3600）"); var copilotInterval = new TextBox { Text = app.Settings.CopilotRefreshSeconds.ToString() }; body.Children.Add(copilotInterval);
        Label("Claude Code 更新間隔（秒・300～3600）"); var claudeInterval = new TextBox { Text = app.Settings.ClaudeRefreshSeconds.ToString() }; body.Children.Add(claudeInterval);
        Label("Codex 実行ファイル（空欄で自動検出）"); var codex = new TextBox { Text = app.Settings.CodexPath ?? "" }; body.Children.Add(codex);
        Label("Copilot 実行ファイル（空欄で同梱ランタイム）"); var copilot = new TextBox { Text = app.Settings.CopilotPath ?? "" }; body.Children.Add(copilot);
        Label("接続案内");
        body.Children.Add(new TextBlock { Text = "Codex: codex login\nCopilot: Copilot CLI の /login\nClaude Code: /login\n既存の公式ツールと同じユーザーで実行してください。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray });
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0,8,0,0) }; body.Children.Add(links);
        foreach (var d in app.Descriptors) { var b = new Button { Content = d.Name, Margin = new(0,0,6,0), FontSize = 10 }; b.Click += (_, _) => WidgetApp.OpenUrl(d.HelpUrl); links.Children.Add(b); }
        var save = new Button { Content = "保存する", Margin = new(0,18,0,0) }; body.Children.Add(save);
        save.Click += (_, _) =>
        {
            if (!int.TryParse(codexInterval.Text, out var codexSeconds) || codexSeconds < 15 || codexSeconds > 3600 ||
                !int.TryParse(copilotInterval.Text, out var copilotSeconds) || copilotSeconds < 15 || copilotSeconds > 3600)
            { MessageBox.Show(this, "CodexとGitHub Copilotの更新間隔は15～3600秒で指定してください。"); return; }
            if (!int.TryParse(claudeInterval.Text, out var claudeSeconds) || claudeSeconds < 300 || claudeSeconds > 3600)
            { MessageBox.Show(this, "Claude Codeの更新間隔は300～3600秒で指定してください。"); return; }
            foreach (var path in new[] { codex.Text, copilot.Text }) if (!string.IsNullOrWhiteSpace(path) && !System.IO.File.Exists(path)) { MessageBox.Show(this, "指定された実行ファイルが見つかりません。"); return; }
            app.Settings.CodexRefreshSeconds = codexSeconds; app.Settings.CopilotRefreshSeconds = copilotSeconds; app.Settings.ClaudeRefreshSeconds = claudeSeconds;
            app.Settings.AlwaysOnTop = top.IsChecked == true; app.Settings.Notifications = notice.IsChecked == true; app.Settings.AutoStart = startup.IsChecked == true;
            app.Settings.CodexPath = codex.Text.Trim(); app.Settings.CopilotPath = copilot.Text.Trim();
            try { app.ApplySettings(); Close(); } catch (Exception e) { MessageBox.Show(this, e.Message, "設定を保存できません"); }
        };
        void Label(string text) => body.Children.Add(new TextBlock { Text = text, Margin = new(0,14,0,5), FontWeight = FontWeights.SemiBold });
    }
}
