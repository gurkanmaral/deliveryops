using System.Text.Json;
using DeliveryOps.PosBridge.Core.Models;

namespace DeliveryOps.PosBridge.Wpf.Services;

public sealed class BridgeSettingsStore(ISecretProtector secretProtector)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeliveryOps", "PosBridge", "settings.json");

    public async Task<BridgeRuntimeSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        string defaultInbox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DeliveryOps", "PosInbox");
        if (!File.Exists(_settingsPath)) return new BridgeRuntimeSettings(string.Empty, string.Empty, defaultInbox, 5);
        await using FileStream stream = File.OpenRead(_settingsPath);
        PersistedSettings? settings = await JsonSerializer.DeserializeAsync<PersistedSettings>(stream, JsonOptions, cancellationToken);
        if (settings is null) return new BridgeRuntimeSettings(string.Empty, string.Empty, defaultInbox, 5);
        string secret;
        try { secret = secretProtector.Unprotect(settings.ProtectedSecret); }
        catch (CryptographicException) { secret = string.Empty; }
        return new BridgeRuntimeSettings(settings.WebhookUrl, secret, settings.InboxDirectory, settings.PollIntervalSeconds);
    }

    public async Task SaveAsync(BridgeRuntimeSettings settings, CancellationToken cancellationToken = default)
    {
        string directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(settings.InboxDirectory);
        PersistedSettings persisted = new(settings.WebhookUrl, secretProtector.Protect(settings.Secret),
            settings.InboxDirectory, settings.PollIntervalSeconds);
        string temporaryPath = _settingsPath + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, persisted, JsonOptions, cancellationToken);
        File.Move(temporaryPath, _settingsPath, true);
    }

    private sealed record PersistedSettings(string WebhookUrl, string ProtectedSecret, string InboxDirectory, int PollIntervalSeconds);
}
