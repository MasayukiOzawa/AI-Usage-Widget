using System.Reflection;
using System.Text.Json;
using GitHub.Copilot;

namespace AiUsageWidget.Core;

internal static class CopilotMetadata
{
    // SDK 1.0.13 requires a token in AuthInfoGhCli, but newer runtimes intentionally
    // return credential-free identity metadata. Reuse the same SDK connection and
    // read only copilotUser until the generated SDK response type is corrected.
    public static async Task<JsonElement?> ReadCompatibleAsync(CopilotClient client, CancellationToken ct)
    {
        try
        {
            var account = client.Rpc.Account;
            var rpc = account.GetType().GetField("_rpc", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(account);
            if (rpc == null) return null;
            var invoke = typeof(CopilotClient).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .SingleOrDefault(m => m.Name == "InvokeRpcAsync" && m.IsGenericMethodDefinition && m.GetParameters().Length == 4 && m.GetParameters()[0].ParameterType.IsInstanceOfType(rpc));
            if (invoke?.MakeGenericMethod(typeof(JsonElement)).Invoke(null, [rpc, "account.getCurrentAuth", Array.Empty<object>(), ct]) is not Task<JsonElement> task) return null;
            var response = await task;
            var identity = response.Get("authInfo");
            return identity?.Get("copilotUser") ?? identity?.Get("copilot_user");
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return null; }
    }
}
