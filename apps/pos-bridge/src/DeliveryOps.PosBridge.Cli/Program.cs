using DeliveryOps.PosBridge.Core.Models;
using DeliveryOps.PosBridge.Core.Services;

Dictionary<string, string> values = ParseArguments(args);
string webhookUrl = Value("webhook", "DELIVERYOPS_POS_WEBHOOK_URL");
string secret = Value("secret", "DELIVERYOPS_POS_WEBHOOK_SECRET");
string inbox = Value("inbox", "DELIVERYOPS_POS_INBOX");
bool runOnce = values.ContainsKey("once");
int interval = int.TryParse(values.GetValueOrDefault("interval"), out int parsedInterval) ? parsedInterval : 5;
BridgeRuntimeSettings settings = new(webhookUrl, secret, inbox, interval);
IReadOnlyList<string> errors = settings.Validate();
if (errors.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, errors));
    Console.Error.WriteLine("Usage: --webhook <url> --inbox <directory> [--secret <value>] [--interval 5] [--once]");
    Console.Error.WriteLine("The secret should preferably be supplied through DELIVERYOPS_POS_WEBHOOK_SECRET.");
    return 2;
}

using HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
PosBridgeProcessor processor = new(new FolderOrderQueue(), new WebhookOrderSender(httpClient));
processor.LogReceived += (_, entry) => Console.WriteLine($"{entry.Timestamp:O} [{entry.Level}] {entry.ExternalOrderId} {entry.Message}");
using CancellationTokenSource shutdown = new();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; shutdown.Cancel(); };

do
{
    ProcessingCycleResult result = await processor.ProcessOnceAsync(settings, shutdown.Token);
    Console.WriteLine($"Cycle complete: processed={result.Processed}, retrying={result.Retrying}, rejected={result.Rejected}");
    if (runOnce) return result.Retrying > 0 ? 3 : result.Rejected > 0 ? 2 : 0;
    await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), shutdown.Token);
} while (!shutdown.IsCancellationRequested);
return 0;

string Value(string argument, string environmentVariable) =>
    values.GetValueOrDefault(argument) ?? Environment.GetEnvironmentVariable(environmentVariable) ?? string.Empty;

static Dictionary<string, string> ParseArguments(string[] arguments)
{
    Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
    for (int index = 0; index < arguments.Length; index++)
    {
        string current = arguments[index];
        if (!current.StartsWith("--", StringComparison.Ordinal)) continue;
        string key = current[2..];
        if (index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            result[key] = arguments[++index];
        else result[key] = "true";
    }
    return result;
}
