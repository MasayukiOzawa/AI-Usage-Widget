using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace AiUsageWidget.Core;
public sealed class LineRpc : IAsyncDisposable
{
    private readonly Process process;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task reader;
    private readonly Task stderr;
    private long sequence;
    public event Action<JsonElement>? Notification;
    public bool IsAlive => !process.HasExited;
    public LineRpc(string executable)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        info.ArgumentList.Add("app-server"); info.ArgumentList.Add("--stdio");
        process = Process.Start(info) ?? throw new IOException("Codex の起動に失敗しました。");
        reader = ReadAsync();
        stderr = Task.Run(async () => { while (await process.StandardError.ReadLineAsync(lifetime.Token) is not null) { } });
    }
    public async Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref sequence);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); pending[id] = tcs;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try { await SendAsync(new { id, method, @params = parameters }, timeout.Token); return await tcs.Task.WaitAsync(timeout.Token); }
        finally { pending.TryRemove(id, out _); }
    }
    public Task NotifyAsync(string method, CancellationToken ct) => SendAsync(new { method, @params = new { } }, ct);
    private async Task SendAsync(object message, CancellationToken ct)
    {
        await writeLock.WaitAsync(ct);
        try { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), ct); await process.StandardInput.FlushAsync(ct); }
        finally { writeLock.Release(); }
    }
    private async Task ReadAsync()
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync(lifetime.Token) is { } line)
            {
                JsonDocument doc; try { doc = JsonDocument.Parse(line); } catch (JsonException) { continue; }
                using (doc)
                {
                    var root = doc.RootElement;
                    if (root.Get("id") is { ValueKind: JsonValueKind.Number } id && id.TryGetInt64(out var n) && pending.TryGetValue(n, out var tcs))
                    {
                        if (root.Get("error") is { } error) tcs.TrySetException(new IOException(error.Text("message") ?? "Codex 通信エラー"));
                        else tcs.TrySetResult(root.Get("result")?.Clone() ?? default);
                    }
                    else if (root.Text("method") != null) Notification?.Invoke(root.Clone());
                }
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally { foreach (var item in pending.Values) item.TrySetException(new IOException("Codex 接続が終了しました。")); }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        try { if (!process.HasExited) { process.StandardInput.Close(); using var timeout = new CancellationTokenSource(2000); try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { process.Kill(true); } } } catch (InvalidOperationException) { }
        try { await Task.WhenAll(reader, stderr); } catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { }
        process.Dispose(); lifetime.Dispose(); writeLock.Dispose();
    }
}
