using System;
using AiUsageWidget.App;
using AiUsageWidget.Core;
using Xunit;

namespace AiUsageWidget.App.Tests;

public sealed class SkillFilterTests
{
    private static ProviderViewModel Create()
    {
        var vm = new ProviderViewModel(new("codex", "Codex", "#6CE6C0", UpdateMode.Poll, UsageCapabilities.Quota, ""));
        vm.Apply(new("codex", "test", DateTimeOffset.UtcNow, "test", UsageStatus.Ready, [])
        {
            Capabilities = new([new("azure-ai", "ユーザー"), new("azure-plugin", "プラグイン提供"), new("other", "システム")], [], [])
        });
        return vm;
    }

    [Fact]
    public void SearchAndSourceCombineAndReset()
    {
        var vm = Create();
        Assert.Equal(3, vm.FilteredSkills.Count);
        vm.SkillSearch = " AZURE ";
        Assert.Equal(2, vm.FilteredSkills.Count);
        vm.PluginSkills = true;
        Assert.Equal("azure-plugin", Assert.Single(vm.FilteredSkills).Name);
        Assert.Equal("スキル (1 / 3)", vm.SkillsLabel);
        vm.SkillSearch = "missing";
        Assert.Empty(vm.FilteredSkills);
        vm.SkillSearch = "";
        vm.AllSkills = true;
        Assert.Equal(3, vm.FilteredSkills.Count);
    }

    [Fact]
    public void ResetStatusAppearsAfterSnapshotUpdateWithoutAffectingFilters()
    {
        var vm = Create();
        Assert.Equal(System.Windows.Visibility.Collapsed, vm.ResetStatusVisibility);
        vm.SkillSearch = "azure";
        vm.Apply(vm.Snapshot! with { ResetStatus = new(2, [], [new("codexRateLimits", "available", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1))]) });
        Assert.Equal(System.Windows.Visibility.Visible, vm.ResetStatusVisibility);
        Assert.Equal("利用可能 2回", vm.ResetStatus!.Availability);
        Assert.Equal("完全リセット", Assert.Single(vm.ResetStatus.Credits).Title);
        Assert.Equal(2, vm.FilteredSkills.Count);
        vm.Apply(vm.Snapshot! with { ResetStatus = null });
        Assert.Equal(System.Windows.Visibility.Collapsed, vm.ResetStatusVisibility);
    }

    [Fact]
    public void ModelSearchMatchesNameOrIdAndSurvivesRefresh()
    {
        var vm = Create();
        var snapshot = vm.Snapshot! with { Models = [new("model-123", "Alpha", null), new("other", "Beta", null)] };
        vm.Apply(snapshot);
        vm.ModelSearch = " ALPHA ";
        Assert.Single(vm.FilteredModels);
        vm.ModelSearch = "123";
        Assert.Equal("Alpha", Assert.Single(vm.FilteredModels).Name);
        vm.Apply(snapshot);
        Assert.Equal("使用可能なモデル (1 / 2)", vm.ModelsLabel);
        vm.ModelSearch = "missing";
        Assert.Empty(vm.FilteredModels);
        vm.ModelSearch = "";
        Assert.Equal(2, vm.FilteredModels.Count);
    }

    [Fact]
    public void PluginAndMcpSearchAreIndependentAndSurviveRefresh()
    {
        var vm = Create();
        var snapshot = vm.Snapshot! with { Capabilities = new([], [new("Alpha", "有効"), new("Beta", "有効")], [new("Alpha-server", "接続済み"), new("Other", "接続済み")]) };
        vm.Apply(snapshot);
        vm.PluginSearch = " ALPHA ";
        Assert.Single(vm.FilteredPlugins);
        Assert.Equal(2, vm.FilteredMcpServers.Count);
        vm.McpSearch = "server";
        Assert.Single(vm.FilteredMcpServers);
        vm.Apply(snapshot);
        Assert.Equal("プラグイン (1 / 2)", vm.PluginsLabel);
        Assert.Equal("MCP (1 / 2)", vm.McpLabel);
        vm.PluginSearch = "missing";
        Assert.Empty(vm.FilteredPlugins);
        vm.PluginSearch = "";
        vm.McpSearch = "";
        Assert.Equal(2, vm.FilteredPlugins.Count);
        Assert.Equal(2, vm.FilteredMcpServers.Count);
    }

    [Fact]
    public void ConditionsSurviveRefreshAndAreIndependent()
    {
        var vm = Create();
        vm.UserSkills = true;
        vm.SkillSearch = "azure";
        vm.Apply(vm.Snapshot!);
        Assert.Single(vm.FilteredSkills);
        Assert.True(vm.UserSkills);
        Assert.Equal(3, Create().FilteredSkills.Count);
    }
}
