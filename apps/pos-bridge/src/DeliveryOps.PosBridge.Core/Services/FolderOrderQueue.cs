using System.Text.Json;
using DeliveryOps.PosBridge.Core.Abstractions;
using DeliveryOps.PosBridge.Core.Models;

namespace DeliveryOps.PosBridge.Core.Services;

public sealed class FolderOrderQueue(
    TimeProvider? timeProvider = null,
    int processedRetentionDays = 7,
    int failedRetentionDays = 30) : IOrderQueue
{
    private const long MaximumOrderFileBytes = 1_048_576;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<QueuedOrder>> ReadPendingAsync(string inboxDirectory, int take, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(inboxDirectory);
        PurgeExpiredArchiveFiles(inboxDirectory, "processed", processedRetentionDays, cancellationToken);
        PurgeExpiredArchiveFiles(inboxDirectory, "failed", failedRetentionDays, cancellationToken);
        string[] paths = Directory.EnumerateFiles(inboxDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(File.GetCreationTimeUtc).Take(Math.Clamp(take, 1, 100)).ToArray();
        List<QueuedOrder> orders = [];
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (new FileInfo(path).Length > MaximumOrderFileBytes)
                {
                    orders.Add(new QueuedOrder(path, null, "Order document exceeds the 1 MB limit."));
                    continue;
                }
                await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.None, 4096, true);
                PosOrder? order = await JsonSerializer.DeserializeAsync<PosOrder>(stream, JsonOptions, cancellationToken);
                orders.Add(order is null
                    ? new QueuedOrder(path, null, "Order document is empty.")
                    : new QueuedOrder(path, order, null));
            }
            catch (IOException)
            {
                // The POS may still be writing the file; leave it for the next cycle.
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                orders.Add(new QueuedOrder(path, null, exception.Message));
            }
        }
        return orders;
    }

    public Task CompleteAsync(QueuedOrder order, CancellationToken cancellationToken) =>
        MoveAsync(order.FilePath, "processed", null, cancellationToken);

    public Task RejectAsync(QueuedOrder order, string reason, CancellationToken cancellationToken) =>
        MoveAsync(order.FilePath, "failed", reason, cancellationToken);

    private async Task MoveAsync(string sourcePath, string folderName, string? reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string targetDirectory = Path.Combine(Path.GetDirectoryName(sourcePath)!, folderName);
        Directory.CreateDirectory(targetDirectory);
        string targetName = $"{Path.GetFileNameWithoutExtension(sourcePath)}-{_timeProvider.GetUtcNow():yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}";
        string targetPath = Path.Combine(targetDirectory, targetName);
        File.Move(sourcePath, targetPath);
        if (!string.IsNullOrWhiteSpace(reason))
            await File.WriteAllTextAsync(targetPath + ".error.txt", reason, cancellationToken);
    }

    private void PurgeExpiredArchiveFiles(
        string inboxDirectory,
        string folderName,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        string archiveDirectory = Path.Combine(inboxDirectory, folderName);
        if (!Directory.Exists(archiveDirectory)) return;

        DateTime cutoffUtc = _timeProvider.GetUtcNow().UtcDateTime
            .AddDays(-Math.Clamp(retentionDays, 1, 3650));
        foreach (string path in Directory.EnumerateFiles(archiveDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoffUtc) File.Delete(path);
            }
            catch (IOException)
            {
                // A scanner or operator may have the archive open; retry on a later cycle.
            }
            catch (UnauthorizedAccessException)
            {
                // Retention must not interrupt delivery processing when a file is protected.
            }
        }
    }
}
