using AiUsageWidget.Core;
using Xunit;

namespace AiUsageWidget.Tests;

public sealed class SystemUsageSettingsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "AiUsageWidgetTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingOrLegacySettingsDefaultToTenSeconds()
    {
        Assert.Equal(10, new WidgetSettings().SystemUsageRefreshSeconds);
        Assert.Equal(10, WidgetSettings.Load(root).SystemUsageRefreshSeconds);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), """{"refreshSeconds":90}""");
        var loaded = WidgetSettings.Load(root);
        Assert.Equal(10, loaded.SystemUsageRefreshSeconds);
        Assert.Equal(90, loaded.CodexRefreshSeconds);
    }

    [Theory]
    [InlineData(int.MinValue, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(17, 17)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]
    [InlineData(int.MaxValue, 60)]
    public void IntervalIsNormalizedWhenAssignedOrLoaded(int input, int expected)
    {
        Assert.Equal(expected, new WidgetSettings { SystemUsageRefreshSeconds = input }.SystemUsageRefreshSeconds);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), $"{{\"systemUsageRefreshSeconds\":{input}}}");
        Assert.Equal(expected, WidgetSettings.Load(root).SystemUsageRefreshSeconds);
    }

    [Fact]
    public void CustomIntervalSurvivesSaveAndReload()
    {
        new WidgetSettings { SystemUsageRefreshSeconds = 23, CodexRefreshSeconds = 120 }.Save(root);
        var loaded = WidgetSettings.Load(root);
        Assert.Equal(23, loaded.SystemUsageRefreshSeconds);
        Assert.Equal(120, loaded.CodexRefreshSeconds);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
