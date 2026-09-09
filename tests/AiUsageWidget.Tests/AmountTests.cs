using System.Text.Json;
using AiUsageWidget.Core;
using Xunit;

namespace AiUsageWidget.Tests;

public class AmountTests
{
    [Theory]
    [InlineData("true", "クレジット")]
    [InlineData("false", "リクエスト")]
    [InlineData("null", "単位不明")]
    public void CopilotPreservesAmountsAndBillingUnits(string billing, string unit)
    {
        using var quota = JsonDocument.Parse("""{"quotaSnapshots":{"premium_interactions":{"usedRequests":180.25,"entitlementRequests":20000,"remainingPercentage":99.1}}}""");
        using var user = JsonDocument.Parse("{\"token_based_billing\":" + billing + "}");
        var window = Assert.Single(ProviderParsers.Copilot(quota.RootElement, DateTimeOffset.UtcNow, user.RootElement));
        Assert.Equal(180.25, window.UsedAmount);
        Assert.Equal(20000, window.LimitAmount);
        Assert.Equal(99.1, window.RemainingPercent);
        Assert.Equal(unit, window.Unit);
    }

    [Fact]
    public void MissingAmountsAreNotDerivedFromPercentage()
    {
        using var quota = JsonDocument.Parse("""{"quotaSnapshots":{"unknown":{"remainingPercentage":90}}}""");
        var window = Assert.Single(ProviderParsers.Copilot(quota.RootElement, DateTimeOffset.UtcNow));
        Assert.Null(window.UsedAmount); Assert.Null(window.LimitAmount);
    }

    [Fact]
    public void UnlimitedAndInvalidAmountsAreNotNumericLimits()
    {
        using var quota = JsonDocument.Parse("""{"quotaSnapshots":{"chat":{"entitlementRequests":-1,"usedRequests":-1}}}""");
        var window = Assert.Single(ProviderParsers.Copilot(quota.RootElement, DateTimeOffset.UtcNow));
        Assert.True(window.Unlimited); Assert.Null(window.LimitAmount); Assert.Null(window.UsedAmount);
    }
}
