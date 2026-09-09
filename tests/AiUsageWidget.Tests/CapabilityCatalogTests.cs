using System.Text.Json;
using AiUsageWidget.Core;
using Xunit;

namespace AiUsageWidget.Tests;

public sealed class CapabilityCatalogTests
{
    [Fact]
    public void CodexSkillsIncludeOnlyEnabledUniqueEntries()
    {
        using var doc = JsonDocument.Parse("""{"data":[{"cwd":"C:/","skills":[{"name":"review","description":"Review code","enabled":true,"path":"C:/.codex/plugins/review/SKILL.md"},{"name":"disabled","enabled":false},{"name":"review","enabled":true}]}]}""");
        var skill = Assert.Single(CapabilityCatalog.ParseCodexSkills(doc.RootElement));
        Assert.Equal("review", skill.Name);
        Assert.Equal("プラグイン", skill.Detail);
        Assert.Equal("Review code", skill.Description);
    }

    [Fact]
    public void CodexMcpPreservesConnectionStateAndError()
    {
        using var doc = JsonDocument.Parse("""{"data":[{"name":"github","status":"connected"},{"name":"db","status":"needs_auth"},{"name":"broken","status":"failed","error":"offline"}]}""");
        var rows = CapabilityCatalog.ParseCodexMcp(doc.RootElement);
        Assert.Equal(new[] { "接続失敗", "要ログイン", "接続済み" }, rows.Select(x => x.Detail));
        Assert.Equal("offline", rows[0].Description);
    }

    [Theory]
    [InlineData("pending", "接続中")]
    [InlineData("disabled", "無効")]
    [InlineData(null, "設定済み")]
    public void UnknownAndKnownStatusesHaveSafeLabels(string? source, string expected) =>
        Assert.Equal(expected, CapabilityCatalog.Status(source));

    [Theory]
    [InlineData("codex", "AGENTS.md")]
    [InlineData("copilot", "copilot-instructions.md")]
    [InlineData("claude", "CLAUDE.md")]
    public void ProviderLocationsExposeInstructionAndThreeConfigurationLocations(string provider, string instruction)
    {
        var rows = CapabilityLocations.For(provider);
        Assert.Equal(new[] { "インストラクション", "スキル", "プラグイン", "MCP" }, rows.Select(x => x.Label));
        Assert.Contains(instruction, rows[0].Value);
        Assert.All(rows, row => Assert.StartsWith("~", row.Value));
        Assert.All(rows, row => Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), row.Value, StringComparison.OrdinalIgnoreCase));
    }
}
