using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace AiUsageWidget.Core;
public sealed record ClaudeBackup(JsonNode? OriginalStatusLine, string InstalledCommand);
public static class ClaudeIntegration
{
    public static void Collect(string root, string input, DateTimeOffset receivedAt)
    {
        using var doc = JsonDocument.Parse(input); var value = doc.RootElement;
        var session = value.Text("session_id"); if (string.IsNullOrEmpty(session)) return;
        var windows = ProviderParsers.Claude(value);
        var snapshot = new UsageSnapshot("claude", "default", receivedAt, "Claude Code · 利用時更新", windows.Count == 0 ? UsageStatus.Waiting : UsageStatus.Ready, windows,
            windows.Count == 0 ? "使用枠の受信待ち · ログイン・Claude Code のバージョンを確認してください。" : null);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session)));
        AppPaths.WriteAtomic(Path.Combine(root, "claude", name + ".json"), JsonSerializer.Serialize(snapshot, Json.Options));
    }
    public static bool IsInstalled(string root) => File.Exists(Path.Combine(root, "claude-integration.json"));
    public static void Install(string root, string claudeHome, string bridgePath)
    {
        if (!File.Exists(bridgePath)) throw new FileNotFoundException("配布フォルダーの ClaudeBridge が見つかりません。publish.ps1 を実行してください。", bridgePath);
        var path = Path.Combine(claudeHome, "settings.json");
        var settings = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : new JsonObject();
        if (IsInstalled(root)) throw new InvalidOperationException("Claude Code 連携は登録済みです。変更する場合は先に解除してください。");
        var command = $"\"{bridgePath.Replace('\\', '/')}\" --root \"{root.Replace('\\', '/')}\"";
        var backup = new ClaudeBackup(settings["statusLine"]?.DeepClone(), command);
        // Keep the full original settings separately for manual recovery; uninstall only touches our field.
        if (File.Exists(path)) AppPaths.WriteAtomic(Path.Combine(root, "claude-settings-backup.json"), File.ReadAllText(path));
        AppPaths.WriteAtomic(Path.Combine(root, "claude-integration.json"), JsonSerializer.Serialize(backup, Json.Options));
        settings["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = command, ["padding"] = backup.OriginalStatusLine?["padding"]?.DeepClone() ?? JsonValue.Create(0) };
        try { AppPaths.WriteAtomic(path, settings.ToJsonString(Json.Options)); }
        catch { File.Delete(Path.Combine(root, "claude-integration.json")); throw; }
    }
    public static void Uninstall(string root, string claudeHome)
    {
        var backupPath = Path.Combine(root, "claude-integration.json");
        if (!File.Exists(backupPath)) return;
        var backup = JsonSerializer.Deserialize<ClaudeBackup>(File.ReadAllText(backupPath), Json.Options)!;
        var path = Path.Combine(claudeHome, "settings.json"); var settings = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (settings["statusLine"]?["command"]?.GetValue<string>() != backup.InstalledCommand) throw new InvalidOperationException("連携後にステータスラインが変更されています。バックアップを確認して手動で解除してください。");
        if (backup.OriginalStatusLine == null) settings.Remove("statusLine"); else settings["statusLine"] = backup.OriginalStatusLine.DeepClone();
        AppPaths.WriteAtomic(path, settings.ToJsonString(Json.Options)); File.Delete(backupPath);
    }
}
