using System.Net;
using System.Text;
using DeliveryOps.PosBridge.Core.Abstractions;
using DeliveryOps.PosBridge.Core.Models;
using DeliveryOps.PosBridge.Core.Services;

namespace DeliveryOps.PosBridge.Core.Tests;

public sealed class PosBridgeProcessorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"deliveryops-pos-{Guid.NewGuid():N}");

    [Fact]
    public async Task Valid_order_is_sent_and_archived()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "order.json"),
            """{"externalOrderId":"POS-1","customerName":"Ada","customerPhone":"555","deliveryAddress":"Istanbul","totalAmount":120.50}""");
        PosBridgeProcessor processor = new(new FolderOrderQueue(), new StubSender(OrderSendResult.Sent(Guid.NewGuid(), false)));

        ProcessingCycleResult result = await processor.ProcessOnceAsync(Settings(), CancellationToken.None);

        Assert.Equal(1, result.Processed);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.json"));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_directory, "processed"), "*.json"));
    }

    [Fact]
    public async Task Invalid_order_is_moved_to_failed_with_reason()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "broken.json"), "{not-json}");
        PosBridgeProcessor processor = new(new FolderOrderQueue(), new StubSender(OrderSendResult.Sent(Guid.NewGuid(), false)));

        ProcessingCycleResult result = await processor.ProcessOnceAsync(Settings(), CancellationToken.None);

        Assert.Equal(1, result.Rejected);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_directory, "failed"), "*.error.txt"));
    }

    [Fact]
    public async Task Server_error_is_retryable_and_keeps_file_in_queue()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "order.json");
        await File.WriteAllTextAsync(path,
            """{"externalOrderId":"POS-2","customerName":"Ada","customerPhone":"555","deliveryAddress":"Istanbul","totalAmount":100}""");
        HttpClient client = new(new StubHttpHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("temporary", Encoding.UTF8, "text/plain")
        }));
        PosBridgeProcessor processor = new(new FolderOrderQueue(), new WebhookOrderSender(client));

        ProcessingCycleResult result = await processor.ProcessOnceAsync(Settings(), CancellationToken.None);

        Assert.Equal(1, result.Retrying);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Expired_processed_and_failed_archives_are_deleted()
    {
        string processed = Path.Combine(_directory, "processed");
        string failed = Path.Combine(_directory, "failed");
        Directory.CreateDirectory(processed);
        Directory.CreateDirectory(failed);
        string oldProcessed = Path.Combine(processed, "old.json");
        string recentProcessed = Path.Combine(processed, "recent.json");
        string oldFailed = Path.Combine(failed, "old.json.error.txt");
        await File.WriteAllTextAsync(oldProcessed, "{}");
        await File.WriteAllTextAsync(recentProcessed, "{}");
        await File.WriteAllTextAsync(oldFailed, "invalid");
        DateTimeOffset now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(oldProcessed, now.AddDays(-8).UtcDateTime);
        File.SetLastWriteTimeUtc(recentProcessed, now.AddDays(-6).UtcDateTime);
        File.SetLastWriteTimeUtc(oldFailed, now.AddDays(-31).UtcDateTime);
        FolderOrderQueue queue = new(new FixedTimeProvider(now));

        await queue.ReadPendingAsync(_directory, 25, CancellationToken.None);

        Assert.False(File.Exists(oldProcessed));
        Assert.True(File.Exists(recentProcessed));
        Assert.False(File.Exists(oldFailed));
    }

    [Fact]
    public async Task Oversized_order_is_rejected_without_deserialization()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "oversized.json"), new string('x', 1_048_577));
        PosBridgeProcessor processor = new(new FolderOrderQueue(), new StubSender(OrderSendResult.Sent(Guid.NewGuid(), false)));

        ProcessingCycleResult result = await processor.ProcessOnceAsync(Settings(), CancellationToken.None);

        Assert.Equal(1, result.Rejected);
        string error = await File.ReadAllTextAsync(Assert.Single(Directory.EnumerateFiles(Path.Combine(_directory, "failed"), "*.error.txt")));
        Assert.Contains("1 MB", error);
    }

    private BridgeRuntimeSettings Settings() => new("http://localhost:5400/webhook", "secret", _directory, 1);
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class StubSender(OrderSendResult result) : IOrderSender
    {
        public Task<OrderSendResult> SendAsync(BridgeRuntimeSettings settings, PosOrder order, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class StubHttpHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
