using System.Text.Json;
namespace AiUsageWidget.Core;
public static class AppPaths
{
    public static string Root => Environment.GetEnvironmentVariable("AI_USAGE_WIDGET_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiUsageWidget");
    public static string ClaudeHome => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    public static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, content); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
public sealed class WidgetSettings
{
    public int CodexRefreshSeconds { get; set; } = 60;
    public int CopilotRefreshSeconds { get; set; } = 60;
    public int ClaudeRefreshSeconds { get; set; } = 300;
    public bool AlwaysOnTop { get; set; }
    public bool Notifications { get; set; } = true;
    public bool AutoStart { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
    public string? CodexPath { get; set; }
    public string? CopilotPath { get; set; }
    public int GetRefreshSeconds(string providerId) => providerId switch
    {
        "codex" => CodexRefreshSeconds,
        "copilot" => CopilotRefreshSeconds,
        "claude" => ClaudeRefreshSeconds,
        _ => 60
    };
    public static WidgetSettings Load(string root)
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(root, "settings.json"));
            using var document = JsonDocument.Parse(text);
            var settings = JsonSerializer.Deserialize<WidgetSettings>(text, Json.Options) ?? new();
            if (!document.RootElement.TryGetProperty("refreshSeconds", out var legacy) || !legacy.TryGetInt32(out var seconds)) return settings;
            if (!document.RootElement.TryGetProperty("codexRefreshSeconds", out _)) settings.CodexRefreshSeconds = Math.Clamp(seconds, 15, 3600);
            if (!document.RootElement.TryGetProperty("copilotRefreshSeconds", out _)) settings.CopilotRefreshSeconds = Math.Clamp(seconds, 15, 3600);
            if (!document.RootElement.TryGetProperty("claudeRefreshSeconds", out _)) settings.ClaudeRefreshSeconds = Math.Clamp(seconds, 300, 3600);
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException) { return new(); }
    }
    public void Save(string root) => AppPaths.WriteAtomic(Path.Combine(root, "settings.json"), JsonSerializer.Serialize(this, Json.Options));
}
public static class Executables
{
    public static string FindCodex(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : throw new FileNotFoundException("設定した Codex が見つかりません。");
        var path = FindOnPath("codex.exe"); if (path != null) return path;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        return Directory.Exists(root) ? Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? throw new FileNotFoundException("Codex の実行ファイルを指定してください。") : throw new FileNotFoundException("Codex が見つかりません。");
    }
    public static string? FindOnPath(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(x => Path.Combine(x.Trim('"'), name)).FirstOrDefault(File.Exists);
}
