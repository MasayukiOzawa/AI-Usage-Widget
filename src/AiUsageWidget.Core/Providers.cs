using System.Net;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text.Json;
using GitHub.Copilot;
namespace AiUsageWidget.Core;
public sealed class CodexProvider(Func<string?> path) : IUsageProvider
{
    private readonly ModelCatalog catalog = new();
    private readonly CapabilityCatalog capabilityCatalog = new();
    public ProviderDescriptor Descriptor { get; } = new("codex", "OpenAI Codex", "#6CE6C0", UpdateMode.Poll, UsageCapabilities.Quota | UsageCapabilities.TokenHistory, "https://learn.chatgpt.com/docs/app-server");
    private LineRpc? rpc;
    private string? activePath;
    public async Task<UsageSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        try
        {
            var executable = Executables.FindCodex(path());
            if (rpc == null || !rpc.IsAlive || executable != activePath)
            {
                if (rpc != null) await rpc.DisposeAsync();
                rpc = new LineRpc(executable); activePath = executable;
                await rpc.CallAsync("initialize", new { clientInfo = new { name = "ai_usage_widget", version = "1.0.0" } }, ct);
                await rpc.NotifyAsync("initialized", ct);
            }
            var account = await rpc.CallAsync("account/read", new { refreshToken = false }, ct);
            var key = account.Get("account")?.Text("email") ?? "default";
            var limits = await rpc.CallAsync("account/rateLimits/read", null, ct);
            IReadOnlyList<TokenDay>? tokens = null; string? message = null;
            try { tokens = ProviderParsers.Tokens(await rpc.CallAsync("account/usage/read", null, ct)); }
            catch (Exception e) when ((e is IOException or OperationCanceledException) && !ct.IsCancellationRequested) { message = "トークン履歴を取得できません。使用枠は更新済みです。"; }
            var models = await catalog.ReadAsync(key, async token =>
            {
                var all = new List<AvailableModel>();
                string? cursor = null;
                var cursors = new HashSet<string>();
                do
                {
                    var page = await rpc.CallAsync("model/list", new { cursor, limit = 100, includeHidden = false }, token);
                    if (page.Get("data") is { } rows) all.AddRange(ModelCatalog.Parse(rows));
                    cursor = page.Text("nextCursor");
                } while (cursor != null && cursors.Add(cursor));
                return all.DistinctBy(x => x.Id).ToArray();
            }, ct);
            var capabilities = await capabilityCatalog.ReadAsync(key, async token =>
            {
                var skills = CapabilityCatalog.ParseCodexSkills(await rpc.CallAsync("skills/list",
                    new { cwds = new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }, forceReload = false }, token));
                IReadOnlyList<CapabilityItem> mcp;
                try { mcp = CapabilityCatalog.ParseCodexMcp(await rpc.CallAsync("mcpServerStatus/list", new { cursor = (string?)null, limit = 100, detail = "toolsAndAuthOnly" }, token)); }
                catch (Exception) when (!token.IsCancellationRequested) { mcp = []; }
                return new ProviderCapabilities(skills, CapabilityCatalog.CodexPlugins(), mcp);
            }, ct);
            return new("codex", key, DateTimeOffset.UtcNow, "Codex App Server", UsageStatus.Ready, ProviderParsers.Codex(limits), message, tokens)
            { Plan = account.Get("account")?.Text("planType"), Models = models, ModelsMessage = catalog.Message, Capabilities = capabilities };
        }
        catch { if (rpc != null) { await rpc.DisposeAsync(); rpc = null; } throw; }
    }
    public async ValueTask DisposeAsync() { if (rpc != null) { await rpc.DisposeAsync(); rpc = null; } }
}
public sealed class CopilotProvider(Func<string?> path) : IUsageProvider
{
    private readonly ModelCatalog catalog = new();
    private readonly CapabilityCatalog capabilityCatalog = new();
    public ProviderDescriptor Descriptor { get; } = new("copilot", "GitHub Copilot", "#A6A0FF", UpdateMode.Poll, UsageCapabilities.Quota, "https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli");
    private CopilotClient? client;
    private string? activePath;
    public async Task<UsageSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        try
        {
            var configured = path();
            if (client == null || configured != activePath)
            {
                if (client != null) await client.DisposeAsync();
                client = new CopilotClient(new CopilotClientOptions { Connection = RuntimeConnection.ForStdio(string.IsNullOrWhiteSpace(configured) ? null : configured) }); activePath = configured;
                await client.StartAsync().WaitAsync(ct);
            }
            var auth = await client.GetAuthStatusAsync(ct);
            var accountJson = JsonSerializer.SerializeToElement(auth, Json.Options);
            var quota = await client.Rpc.Account.GetQuotaAsync(cancellationToken: ct);
            JsonElement? user = null;
            try
            {
                var current = await client.Rpc.Account.GetCurrentAuthAsync(ct);
                if (current.AuthInfo is { } identity)
                {
                    var metadata = JsonSerializer.SerializeToElement(identity, identity.GetType(), Json.Options);
                    user = metadata.Get("copilotUser") ?? metadata.Get("copilot_user");
                }
            }
            catch (JsonException) when (!ct.IsCancellationRequested) { user = await CopilotMetadata.ReadCompatibleAsync(client, ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { /* Keep quota when optional metadata fails. */ }
            var now = DateTimeOffset.UtcNow;
            var models = await catalog.ReadAsync(accountJson.Text("login") ?? "default", async token =>
                ModelCatalog.Parse(JsonSerializer.SerializeToElement(await client.ListModelsAsync(token), Json.Options)), ct);
            var capabilities = await capabilityCatalog.ReadAsync(accountJson.Text("login") ?? "default", async token =>
            {
                var skillResult = await client.Rpc.Skills.DiscoverAsync([Environment.CurrentDirectory], [], false, token);
                var skills = skillResult.Skills.Where(x => x.Enabled).Select(x => new CapabilityItem(x.Name,
                    CapabilityCatalog.SourceLabel(x.Source.ToString()), x.Description)).OrderBy(x => x.Name).ToArray();
                var pluginResult = await client.Rpc.Plugins.ListAsync(token);
                var plugins = pluginResult.Plugins.Where(x => x.Enabled).Select(x => new CapabilityItem(x.Name,
                    string.IsNullOrWhiteSpace(x.Version) ? "有効" : x.Version,
                    string.IsNullOrWhiteSpace(x.Marketplace) ? null : x.Marketplace)).OrderBy(x => x.Name).ToArray();
                var mcpResult = await client.Rpc.Mcp.Config.ListAsync(token);
                var mcp = mcpResult.Servers.Where(x => x.Value.Get("disabled")?.ValueKind != JsonValueKind.True)
                    .Select(x => new CapabilityItem(x.Key, "設定済み")).OrderBy(x => x.Name).ToArray();
                return new ProviderCapabilities(skills, plugins, mcp);
            }, ct);
            return new("copilot", accountJson.Text("login") ?? "default", now, "GitHub Copilot SDK", UsageStatus.Ready, ProviderParsers.Copilot(JsonSerializer.SerializeToElement(quota, Json.Options), now, user))
            { Plan = user?.Text("access_type_sku") ?? user?.Text("accessTypeSku") ?? user?.Text("copilot_plan") ?? user?.Text("copilotPlan"), Models = models, ModelsMessage = catalog.Message, Capabilities = capabilities };
        }
        catch { if (client != null) { await client.DisposeAsync(); client = null; } throw; }
    }
    public async ValueTask DisposeAsync() { if (client != null) { await client.DisposeAsync(); client = null; } }
}
public sealed class ClaudeProvider : IUsageProvider
{
    private readonly ModelCatalog catalog = new();
    private readonly CapabilityCatalog capabilityCatalog = new();
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly string credentialsPath;
    private readonly Func<CancellationToken, Task> refreshAuthentication;
    public ClaudeProvider(string root, HttpClient? httpClient = null, string? credentialsPath = null,
        Func<CancellationToken, Task>? refreshAuthentication = null)
    {
        _ = root;
        ownsHttp = httpClient == null;
        http = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        this.credentialsPath = credentialsPath ?? Path.Combine(AppPaths.ClaudeHome, ".credentials.json");
        this.refreshAuthentication = refreshAuthentication ?? RefreshClaudeAuthenticationAsync;
    }
    public ProviderDescriptor Descriptor { get; } = new("claude", "Claude Code", "#EAAF8C", UpdateMode.Poll, UsageCapabilities.Quota, "https://code.claude.com/docs/en/authentication") { MinimumRefreshSeconds = 300 };
    public async Task<UsageSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        var oauth = await ReadAuthenticationAsync(ct);
        var token = oauth.Text("accessToken")!;

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException($"Claude Code auth/plan unavailable (HTTP {(int)response.StatusCode}).");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Claude Code usage HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var usage = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var windows = ProviderParsers.ClaudeUsage(usage.RootElement);
        var now = DateTimeOffset.UtcNow;
        var plan = oauth.Text("subscriptionType");
        var accountKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
        var models = await catalog.ReadAsync(accountKey, ModelCatalog.ClaudeAsync, ct);
        var capabilities = await capabilityCatalog.ReadAsync(accountKey, CapabilityCatalog.ClaudeAsync, ct);
        return windows.Count == 0
            ? new("claude", "default", now, "Claude Code Usage", UsageStatus.AuthenticationRequired, []) { Plan = plan, Capabilities = capabilities }
            : new("claude", "default", now, "Claude Code Usage", UsageStatus.Ready, windows) { Plan = plan, Models = models, ModelsMessage = catalog.Message, Capabilities = capabilities };
    }
    public ValueTask DisposeAsync() { if (ownsHttp) http.Dispose(); return ValueTask.CompletedTask; }

    private async Task<JsonElement> ReadAuthenticationAsync(CancellationToken ct)
    {
        var oauth = await TryReadAuthenticationAsync(ct);
        if (IsUsable(oauth)) return oauth!.Value;
        try { await refreshAuthentication(ct); }
        catch (Exception e) when (!ct.IsCancellationRequested) { throw new UnauthorizedAccessException("Claude Code login is required.", e); }
        oauth = await TryReadAuthenticationAsync(ct);
        return IsUsable(oauth) ? oauth!.Value : throw new UnauthorizedAccessException("Claude Code login is required.");
    }

    private async Task<JsonElement?> TryReadAuthenticationAsync(CancellationToken ct)
    {
        try
        {
            using var credentials = JsonDocument.Parse(await File.ReadAllTextAsync(credentialsPath, ct));
            return (credentials.RootElement.Get("claudeAiOauth") ?? credentials.RootElement).Clone();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static bool IsUsable(JsonElement? oauth)
    {
        if (oauth is not { } value || string.IsNullOrWhiteSpace(value.Text("accessToken"))) return false;
        if (value.Get("expiresAt") is not { ValueKind: JsonValueKind.Number } expiry || !expiry.TryGetInt64(out var milliseconds)) return true;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) > DateTimeOffset.UtcNow.AddMinutes(1); }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static async Task RefreshClaudeAuthenticationAsync(CancellationToken ct)
    {
        var native = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        var info = new ProcessStartInfo(File.Exists(native) ? native : "claude.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var arg in new[] { "auth", "status", "--json" }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Claude Code を起動できません。");
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var output = await outputTask; var error = await errorTask;
        if (process.ExitCode != 0) throw new UnauthorizedAccessException(error);
        using var status = JsonDocument.Parse(output);
        if (!status.RootElement.Flag("loggedIn")) throw new UnauthorizedAccessException("Claude Code login is required.");
    }
}
