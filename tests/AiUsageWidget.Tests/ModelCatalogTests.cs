using System.Text.Json;
using AiUsageWidget.Core;
using Xunit;

namespace AiUsageWidget.Tests;
public class ModelCatalogTests
{
    [Fact]
    public void ParsesProviderSchemasAndExcludesHiddenOrDisabledModels()
    {
        using var doc = JsonDocument.Parse("""[{"id":"a","displayName":"A"},{"value":"b","displayName":"B","description":"Detail"},{"id":"c","name":"C"},{"id":"hidden","hidden":true},{"id":"disabled","policy":{"state":"disabled"}},{"id":"a"},{}]""");
        var rows = ModelCatalog.Parse(doc.RootElement);
        Assert.Equal(new[] { "a", "b", "c" }, rows.Select(x => x.Id));
        Assert.Equal("Detail", rows[1].Description);
    }
    [Fact]
    public async Task CacheIsReusedButClearedOnAccountChange()
    {
        var catalog = new ModelCatalog();
        var calls = 0;
        Task<IReadOnlyList<AvailableModel>> Fetch(CancellationToken ct) { calls++; return Task.FromResult<IReadOnlyList<AvailableModel>>([new("a", "A", null)]); }
        await catalog.ReadAsync("one", Fetch, default);
        await catalog.ReadAsync("one", Fetch, default);
        Assert.Equal(1, calls);
        var result = await catalog.ReadAsync("two", _ => throw new IOException(), default);
        Assert.Null(result);
        Assert.NotNull(catalog.Message);
    }
    [Fact]
    public async Task CancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ModelCatalog().ReadAsync("one", ct => Task.FromCanceled<IReadOnlyList<AvailableModel>>(ct), cts.Token));
    }
}
