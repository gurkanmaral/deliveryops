using System.Text.Json;
using DeliveryOps.PosBridge.Core.Models;

namespace DeliveryOps.PosBridge.Core.Services;

/// <summary>Atomic JSON persistence for small local files (write to .tmp, then replace).</summary>
public static class JsonFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return default;
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporaryPath = path + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        File.Move(temporaryPath, path, true);
    }
}

public sealed class MenuStore(string path)
{
    public async Task<MenuCatalog> LoadAsync(CancellationToken cancellationToken = default) =>
        await JsonFileStore.ReadAsync<MenuCatalog>(path, cancellationToken) ?? MenuCatalog.Empty;

    public async Task SaveAsync(MenuCatalog catalog, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> errors = catalog.Validate();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        await JsonFileStore.WriteAsync(path, catalog, cancellationToken);
    }
}

/// <summary>
/// Recent cashier orders, so a payment taken after the order was sent can still be reported. Holds customer
/// contact data, so records older than the retention window are dropped on every save.
/// </summary>
public sealed class CashierOrderJournal(string path, TimeProvider? timeProvider = null, int retentionDays = 2)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<IReadOnlyList<CashierOrderRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        List<CashierOrderRecord> records = await JsonFileStore.ReadAsync<List<CashierOrderRecord>>(path, cancellationToken) ?? [];
        return Prune(records);
    }

    public async Task UpsertAsync(CashierOrderRecord record, CancellationToken cancellationToken = default)
    {
        List<CashierOrderRecord> records = [.. await LoadAsync(cancellationToken)];
        records.RemoveAll(x => x.ExternalOrderId == record.ExternalOrderId);
        records.Insert(0, record);
        await JsonFileStore.WriteAsync(path, Prune(records), cancellationToken);
    }

    private List<CashierOrderRecord> Prune(IEnumerable<CashierOrderRecord> records)
    {
        DateTimeOffset cutoff = _timeProvider.GetUtcNow().AddDays(-Math.Clamp(retentionDays, 1, 30));
        return records.Where(x => x.CreatedAt >= cutoff).Take(500).ToList();
    }
}
