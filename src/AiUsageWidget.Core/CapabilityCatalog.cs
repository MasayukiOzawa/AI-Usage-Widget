using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiUsageWidget.Core;

// Capability discovery only reads provider metadata. It never creates a chat or model turn.
public sealed class CapabilityCatalog
{
    private DateTimeOffset next;
    private ProviderCapabilities? value;
    private string? account;

    public async Task<ProviderCapabilities?> ReadAsync(string accountKey,
        Func<CancellationToken, Task<ProviderCapabilities>> fetch, CancellationToken ct)
    {
        if (account != accountKey) { account = accountKey; value = null; next = default; }
        if (DateTimeOffset.UtcNow < next) return value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            value = await fetch(timeout.Token);
            next = DateTimeOffset.UtcNow.AddHours(1);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            value = value is null ? new([], [], [], "一覧を取得できませんでした。") : value with { Message = "前回取得した一覧" };
            next = DateTimeOffset.UtcNow.AddMinutes(5);
        }
        return value;
    }

    public static IReadOnlyList<CapabilityItem> ParseCodexSkills(JsonElement result)
    {
        var output = new List<CapabilityItem>();
        if (result.Get("data") is not { ValueKind: JsonValueKind.Array } groups) return output;
        foreach (var group in groups.EnumerateArray())
        {
            if (group.Get("skills") is not { ValueKind: JsonValueKind.Array } skills) continue;
            foreach (var skill in skills.EnumerateArray().Where(x => x.Get("enabled")?.ValueKind != JsonValueKind.False))
            {
                var name = skill.Text("name");
                if (!string.IsNullOrWhiteSpace(name)) output.Add(new(name, SourceLabel(skill.Text("path") ?? skill.Text("source")), skill.Text("description")));
            }
        }
        return Unique(output);
    }

    public static IReadOnlyList<CapabilityItem> ParseCodexMcp(JsonElement result)
    {
        var rows = result.Get("data") ?? result.Get("servers") ?? result;
        if (rows.ValueKind != JsonValueKind.Array) return [];
        return Unique(rows.EnumerateArray().Select(x =>
        {
            var name = x.Text("name") ?? x.Text("id") ?? "";
            return new CapabilityItem(name, Status(x.Text("status") ?? x.Text("state")), x.Text("error"));
        }).Where(x => x.Name.Length > 0));
    }

    public static IReadOnlyList<CapabilityItem> CodexPlugins()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "plugins", "cache");
        if (!Directory.Exists(root)) return [];
        var found = new List<(CapabilityItem Item, DateTime Time)>();
        foreach (var file in Directory.EnumerateFiles(root, "plugin.json", SearchOption.AllDirectories))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var name = doc.RootElement.Text("name") ?? Directory.GetParent(Directory.GetParent(file)!.FullName)!.Name;
                var version = doc.RootElement.Text("version") ?? Directory.GetParent(file)!.Parent?.Name;
                found.Add((new(name, string.IsNullOrWhiteSpace(version) ? "検出済み" : version, doc.RootElement.Text("description")), File.GetLastWriteTimeUtc(file)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return found.GroupBy(x => x.Item.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.MaxBy(x => x.Time).Item).OrderBy(x => x.Name).ToArray();
    }

    public static async Task<ProviderCapabilities> ClaudeAsync(CancellationToken ct)
    {
        var executable = ClaudeExecutable();
        var pluginsJson = await RunAsync(executable, ["plugin", "list", "--json"], ct);
        var plugins = ParseClaudePlugins(pluginsJson);
        var mcp = ParseClaudeMcp(await RunAsync(executable, ["mcp", "list"], ct));
        var skills = ReadSkills(Path.Combine(AppPaths.ClaudeHome, "skills")).ToList();
        foreach (var plugin in plugins)
            if (plugin.Description is { Length: > 0 } path) skills.AddRange(ReadSkills(Path.Combine(path, "skills")));
        return new(Unique(skills), plugins.Select(x => x with { Description = null }).ToArray(), mcp);
    }

    private static IReadOnlyList<CapabilityItem> ParseClaudePlugins(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var rows = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.Get("plugins");
            if (rows?.ValueKind != JsonValueKind.Array) return [];
            return Unique(rows.Value.EnumerateArray().Where(x => x.Get("enabled")?.ValueKind != JsonValueKind.False).Select(x =>
            {
                var name = x.Text("name") ?? x.Text("id") ?? "";
                var version = x.Text("version") ?? "有効";
                return new CapabilityItem(name, version, x.Text("installPath") ?? x.Text("path"));
            }).Where(x => x.Name.Length > 0));
        }
        catch (JsonException) { return []; }
    }

    private static IReadOnlyList<CapabilityItem> ParseClaudeMcp(string text)
    {
        if (text.Contains("No MCP servers configured", StringComparison.OrdinalIgnoreCase)) return [];
        return Unique(text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line =>
        {
            var match = Regex.Match(line, "^(?<name>[^:]+):.*?(?<state>Connected|Failed|Needs auth|Pending|Disabled|Stopped)", RegexOptions.IgnoreCase);
            return match.Success ? new CapabilityItem(match.Groups["name"].Value.Trim(), Status(match.Groups["state"].Value)) : null;
        }).OfType<CapabilityItem>());
    }

    private static IEnumerable<CapabilityItem> ReadSkills(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var file in Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories))
        {
            string? name = null, description = null;
            try
            {
                foreach (var line in File.ReadLines(file).Take(40))
                {
                    if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase)) name = line[5..].Trim().Trim('"', '\'');
                    if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase)) description = line[12..].Trim().Trim('"', '\'');
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            name ??= Directory.GetParent(file)?.Name;
            if (!string.IsNullOrWhiteSpace(name)) yield return new(name, "ユーザー", description);
        }
    }

    private static async Task<string> RunAsync(string executable, IReadOnlyList<string> args, CancellationToken ct)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Claude Code を起動できません。");
        var output = process.StandardOutput.ReadToEndAsync(ct); var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout = await output; var stderr = await error;
        if (process.ExitCode != 0) throw new IOException(stderr);
        return stdout;
    }

    private static string ClaudeExecutable()
    {
        var native = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        return File.Exists(native) ? native : "claude.exe";
    }

    private static IReadOnlyList<CapabilityItem> Unique(IEnumerable<CapabilityItem> rows) => rows
        .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).OrderBy(x => x.Name).ToArray();
    public static string SourceLabel(string? value) => value?.ToLowerInvariant() switch
    {
        { } x when x.Contains("plugin") => "プラグイン",
        { } x when x.Contains("project") => "プロジェクト",
        { } x when x.Contains("personal") || x.Contains("user") => "ユーザー",
        { } x when x.Contains("builtin") || x.Contains("system") => "内蔵",
        _ => "ユーザー"
    };
    public static string Status(string? value) => value?.ToLowerInvariant().Replace('_', '-') switch
    {
        "connected" or "ready" => "接続済み", "failed" or "error" => "接続失敗",
        "needs-auth" or "needs auth" => "要ログイン", "pending" or "starting" => "接続中",
        "disabled" => "無効", "stopped" => "停止", _ => "設定済み"
    };
}

public static class CapabilityLocations
{
    public static IReadOnlyList<CapabilityLocation> For(string providerId)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var agents = Path.Combine(profile, ".agents", "skills");
        var locations = providerId switch
        {
            "codex" => Build(
                Join(Path.Combine(CodexHome(), "AGENTS.md"), Path.Combine(CodexHome(), "AGENTS.override.md"),
                    Project("AGENTS.md"), Project("AGENTS.override.md")),
                Join(agents, Path.Combine(CodexHome(), "skills")),
                Path.Combine(CodexHome(), "plugins"),
                $"{CodexHome()}  (config.toml)"),
            "copilot" => Build(
                Join(Path.Combine(profile, ".copilot", "copilot-instructions.md"),
                    Project(".github", "copilot-instructions.md"), Project(".github", "instructions", "*.instructions.md"), Project("AGENTS.md")),
                Join(agents, Path.Combine(profile, ".copilot", "skills")),
                Path.Combine(profile, ".copilot", "installed-plugins"),
                $"{Path.Combine(profile, ".copilot")}  (mcp-config.json)"),
            "claude" => Build(
                Join(Path.Combine(AppPaths.ClaudeHome, "CLAUDE.md"), Project("CLAUDE.md"), Project(".claude", "CLAUDE.md")),
                Join(agents, Path.Combine(AppPaths.ClaudeHome, "skills")),
                Path.Combine(AppPaths.ClaudeHome, "plugins"),
                $"{AppPaths.ClaudeHome}  (.claude.json / .mcp.json)"),
            _ => []
        };
        return locations.Select(x => x with { Value = AbbreviateProfile(x.Value, profile) }).ToArray();
    }

    private static IReadOnlyList<CapabilityLocation> Build(string instructions, string skills, string plugins, string mcp) =>
    [
        new("インストラクション", instructions),
        new("スキル", skills),
        new("プラグイン", plugins),
        new("MCP", mcp)
    ];

    private static string CodexHome() => Environment.GetEnvironmentVariable("CODEX_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    private static string Join(params string[] values) => string.Join(Environment.NewLine, values.Distinct(StringComparer.OrdinalIgnoreCase));
    private static string Project(params string[] parts) => Path.Combine(["<プロジェクト>", .. parts]);
    private static string AbbreviateProfile(string value, string profile) => string.IsNullOrWhiteSpace(profile)
        ? value : value.Replace(profile, "~", StringComparison.OrdinalIgnoreCase);
}
