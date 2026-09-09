using System.Diagnostics;
using System.Text.Json;

namespace AiUsageWidget.Core;

// Model discovery never sends a user message or starts a model turn.
public sealed class ModelCatalog
{
    private DateTimeOffset next;
    private IReadOnlyList<AvailableModel>? models;
    private string? account;
    public string? Message { get; private set; }
    public async Task<IReadOnlyList<AvailableModel>?> ReadAsync(string accountKey, Func<CancellationToken, Task<IReadOnlyList<AvailableModel>>> fetch, CancellationToken ct)
    {
        if (account != accountKey) { models = null; next = default; account = accountKey; }
        if (DateTimeOffset.UtcNow < next) return models;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            models = await fetch(timeout.Token);
            Message = models.Count == 0 ? "モデル一覧を取得できませんでした。" : null;
            next = DateTimeOffset.UtcNow.AddMinutes(models.Count == 0 ? 5 : 60);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            Message = models is { Count: > 0 } ? "前回取得したモデル一覧" : "モデル一覧を取得できませんでした。";
            next = DateTimeOffset.UtcNow.AddMinutes(5);
        }
        return models;
    }

    public static IReadOnlyList<AvailableModel> Parse(JsonElement rows)
    {
        if (rows.ValueKind != JsonValueKind.Array) return [];
        return rows.EnumerateArray()
            .Where(x => !x.Flag("hidden") && !x.Flag("isHidden") && x.Get("policy")?.Text("state") != "disabled")
            .Select(x => new AvailableModel(x.Text("id") ?? x.Text("value") ?? x.Text("model") ?? "",
                x.Text("displayName") ?? x.Text("name") ?? x.Text("id") ?? x.Text("value") ?? "", x.Text("description")))
            .Where(x => x.Id.Length > 0).DistinctBy(x => x.Id).ToArray();
    }

    public static async Task<IReadOnlyList<AvailableModel>> ClaudeAsync(CancellationToken ct)
    {
        var native = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        var info = new ProcessStartInfo(File.Exists(native) ? native : "claude.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = Path.GetTempPath()
        };
        foreach (var arg in new[] { "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
            "--no-session-persistence", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}" }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Claude Code を起動できません。");
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.StandardInput.WriteLineAsync("{\"type\":\"control_request\",\"request_id\":\"models\",\"request\":{\"subtype\":\"initialize\"}}".AsMemory(), ct);
            await process.StandardInput.FlushAsync(ct);
            while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
            {
                using var doc = JsonDocument.Parse(line);
                var response = doc.RootElement.Get("response");
                if (doc.RootElement.Text("type") != "control_response" || response?.Text("request_id") != "models") continue;
                if (response?.Get("response")?.Get("models") is { } rows) return Parse(rows);
                throw new IOException("Claude Code モデル一覧を取得できません。");
            }
            throw new IOException("Claude Code 接続が終了しました。");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await stderr;
        }
    }
}
