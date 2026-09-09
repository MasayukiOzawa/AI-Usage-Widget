using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AiUsageWidget.Core;

Console.InputEncoding = Encoding.UTF8; Console.OutputEncoding = new UTF8Encoding(false);
var rootIndex = Array.IndexOf(args, "--root");
var root = rootIndex >= 0 && rootIndex + 1 < args.Length ? args[rootIndex + 1] : AppPaths.Root;
var input = await Console.In.ReadToEndAsync();
try { ClaudeIntegration.Collect(root, input, DateTimeOffset.UtcNow); } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
try
{
    var backupPath = Path.Combine(root, "claude-integration.json");
    if (!File.Exists(backupPath)) return;
    var backup = JsonSerializer.Deserialize<ClaudeBackup>(await File.ReadAllTextAsync(backupPath), Json.Options);
    var command = backup?.OriginalStatusLine?["command"]?.GetValue<string>();
    if (string.IsNullOrWhiteSpace(command)) return;
    // Claude executes statusLine through a shell. Preserve stdin and stdout of the original command.
    var gitBash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
    var shell = File.Exists(gitBash) ? gitBash : "cmd.exe";
    var start = new ProcessStartInfo(shell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
    if (shell == "cmd.exe") { start.ArgumentList.Add("/d"); start.ArgumentList.Add("/s"); start.ArgumentList.Add("/c"); } else start.ArgumentList.Add("-c");
    start.ArgumentList.Add(command);
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    await process.StandardInput.WriteAsync(input); process.StandardInput.Close();
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    try { await process.WaitForExitAsync(deadline.Token); } catch (OperationCanceledException) { process.Kill(true); }
    Console.Write(await output); await stderr;
}
catch (Exception e) when (e is IOException or JsonException or System.ComponentModel.Win32Exception or InvalidOperationException) { }
