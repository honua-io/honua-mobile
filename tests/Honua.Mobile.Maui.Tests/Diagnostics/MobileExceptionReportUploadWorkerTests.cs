using System.Globalization;
using System.Net;
using System.Text.Json;
using Honua.Mobile.Maui;
using Honua.Mobile.Maui.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Mobile.Maui.Tests.Diagnostics;

public sealed class MobileExceptionReportUploadWorkerTests
{
    [Fact]
    public void ServerUploadRegistration_WiresQueueReporterUploaderAndWorker()
    {
        var queueDirectory = CreateQueueDirectoryPath();
        try
        {
            using var provider = new ServiceCollection()
                .AddLogging()
                .AddHonuaMobileExceptionReporting(new MobileExceptionReportingOptions
                {
                    Mode = MobileExceptionReportingMode.ServerUpload,
                    QueueDirectory = queueDirectory,
                    UploadEndpoint = new Uri("https://api.honua.test/mobile/exception-reports"),
                })
                .BuildServiceProvider();

            Assert.IsType<LocalMobileExceptionReporter>(provider.GetRequiredService<IMobileExceptionReporter>());
            Assert.IsType<FileMobileExceptionReportQueue>(provider.GetRequiredService<IMobileExceptionReportQueue>());
            Assert.IsType<HttpMobileExceptionReportUploader>(provider.GetRequiredService<IMobileExceptionReportUploader>());
            Assert.IsType<MobileExceptionReportUploadWorker>(provider.GetRequiredService<IMobileExceptionReportUploadWorker>());
        }
        finally
        {
            DeleteDirectory(queueDirectory);
        }
    }

    [Fact]
    public async Task ServerUploadReporter_QueuesSanitizedReportBeforeUpload()
    {
        var queueDirectory = CreateQueueDirectoryPath();
        try
        {
            var options = new MobileExceptionReportingOptions
            {
                Mode = MobileExceptionReportingMode.ServerUpload,
                QueueDirectory = queueDirectory,
                UploadEndpoint = new Uri("https://api.honua.test/mobile/exception-reports"),
            };
            var queue = new FileMobileExceptionReportQueue(options);
            var reporter = new LocalMobileExceptionReporter(queue, options);

            await reporter.ReportAsync(new InvalidOperationException("failed token=raw-token"));

            var queued = Assert.Single(await ReadQueuedReportsAsync(queue));
            Assert.DoesNotContain("raw-token", queued.Report.Message);
        }
        finally
        {
            DeleteDirectory(queueDirectory);
        }
    }

    [Fact]
    public async Task HttpUploader_PostsSanitizedReportJsonToConfiguredEndpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        using var http = new HttpClient(new StubHttpMessageHandler(async (request, _) =>
        {
            capturedRequest = request;
            capturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }));
        var options = new MobileExceptionReportingOptions
        {
            Mode = MobileExceptionReportingMode.ServerUpload,
            UploadEndpoint = new Uri("https://api.honua.test/mobile/exception-reports"),
        };
        var uploader = new HttpMobileExceptionReportUploader(http, options);
        var report = CreateReport(message: "sanitized failure");

        var uploaded = await uploader.UploadAsync(report);

        Assert.True(uploaded);
        Assert.Equal(HttpMethod.Post, capturedRequest?.Method);
        Assert.Equal(options.UploadEndpoint, capturedRequest?.RequestUri);
        Assert.Equal("application/json", capturedRequest?.Content?.Headers.ContentType?.MediaType);
        Assert.NotNull(capturedBody);

        using var document = JsonDocument.Parse(capturedBody);
        Assert.Equal(report.Id, document.RootElement.GetProperty("id").GetString());
        Assert.Equal("sanitized failure", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task HttpUploader_AppliesRequestCustomizersBeforeSending()
    {
        HttpRequestMessage? capturedRequest = null;
        using var http = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        }));
        var options = new MobileExceptionReportingOptions
        {
            Mode = MobileExceptionReportingMode.ServerUpload,
            UploadEndpoint = new Uri("https://api.honua.test/mobile/exception-reports"),
        };
        var uploader = new HttpMobileExceptionReportUploader(
            http,
            options,
            [new HeaderRequestCustomizer("X-Honua-Test", "customized")]);

        var uploaded = await uploader.UploadAsync(CreateReport());

        Assert.True(uploaded);
        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest.Headers.TryGetValues("X-Honua-Test", out var values));
        Assert.Equal("customized", Assert.Single(values));
    }

    [Fact]
    public async Task HttpUploader_WithoutEndpointDoesNotSendRequest()
    {
        var sent = false;
        using var http = new HttpClient(new StubHttpMessageHandler((_, _) =>
        {
            sent = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        var uploader = new HttpMobileExceptionReportUploader(
            http,
            new MobileExceptionReportingOptions
            {
                Mode = MobileExceptionReportingMode.ServerUpload,
            });

        var uploaded = await uploader.UploadAsync(CreateReport());

        Assert.False(uploaded);
        Assert.False(sent);
    }

    [Fact]
    public async Task FlushPendingAsync_RetainsReportOnFailureAndDeletesAfterBackoffAndSuccess()
    {
        var timeProvider = new MutableTimeProvider(DateTimeOffset.Parse("2026-05-05T00:00:00Z", CultureInfo.InvariantCulture));
        var queue = new InMemoryExceptionReportQueue();
        var report = CreateReport();
        await queue.EnqueueAsync(report);
        var uploader = new SequenceUploader([false, true]);
        var worker = new MobileExceptionReportUploadWorker(
            queue,
            uploader,
            new MobileExceptionReportingOptions
            {
                Mode = MobileExceptionReportingMode.ServerUpload,
                UploadEndpoint = new Uri("https://api.honua.test/mobile/exception-reports"),
                UploadInitialBackoff = TimeSpan.FromMinutes(1),
                UploadMaxBackoff = TimeSpan.FromMinutes(5),
            },
            timeProvider);

        await worker.FlushPendingAsync();
        Assert.Single(queue.Reports);
        Assert.Equal(1, uploader.Attempts);

        await worker.FlushPendingAsync();
        Assert.Single(queue.Reports);
        Assert.Equal(1, uploader.Attempts);

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        await worker.FlushPendingAsync();

        Assert.Empty(queue.Reports);
        Assert.Equal(2, uploader.Attempts);
    }

    [Fact]
    public async Task FlushPendingAsync_SerializesConcurrentFlushes()
    {
        var queue = new InMemoryExceptionReportQueue();
        await queue.EnqueueAsync(CreateReport());
        var uploadStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpload = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uploader = new BlockingUploader(uploadStarted, releaseUpload);
        var worker = new MobileExceptionReportUploadWorker(
            queue,
            uploader,
            new MobileExceptionReportingOptions
            {
                Mode = MobileExceptionReportingMode.ServerUpload,
                UploadEndpoint = new Uri("https://api.honua.test/mobile/exception-reports"),
            });

        var firstFlush = worker.FlushPendingAsync();
        await uploadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondFlush = worker.FlushPendingAsync();

        releaseUpload.SetResult(null);
        await Task.WhenAll(firstFlush, secondFlush).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(queue.Reports);
        Assert.Equal(1, uploader.Attempts);
    }

    private static MobileExceptionReport CreateReport(string? message = "boom")
    {
        return new MobileExceptionReport
        {
            Id = Guid.NewGuid().ToString("N"),
            Fingerprint = "fingerprint",
            OccurredAtUtc = DateTimeOffset.Parse("2026-05-05T00:00:00Z", CultureInfo.InvariantCulture),
            Source = "unit-test",
            ExceptionType = typeof(InvalidOperationException).FullName!,
            Message = message,
        };
    }

    private static async Task<List<QueuedMobileExceptionReport>> ReadQueuedReportsAsync(IMobileExceptionReportQueue queue)
    {
        var reports = new List<QueuedMobileExceptionReport>();
        await foreach (var queued in queue.ReadPendingAsync())
        {
            reports.Add(queued);
        }

        return reports;
    }

    private static string CreateQueueDirectoryPath()
    {
        return Path.Combine(Path.GetTempPath(), $"honua-mobile-upload-tests-{Guid.NewGuid():N}");
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _handler(request, cancellationToken);
        }
    }

    private sealed class HeaderRequestCustomizer : IMobileExceptionReportUploadRequestCustomizer
    {
        private readonly string _name;
        private readonly string _value;

        public HeaderRequestCustomizer(string name, string value)
        {
            _name = name;
            _value = value;
        }

        public void Customize(HttpRequestMessage request, MobileExceptionReport report)
        {
            request.Headers.TryAddWithoutValidation(_name, _value);
        }
    }

    private sealed class SequenceUploader : IMobileExceptionReportUploader
    {
        private readonly Queue<bool> _results;

        public SequenceUploader(IEnumerable<bool> results)
        {
            _results = new Queue<bool>(results);
        }

        public int Attempts { get; private set; }

        public Task<bool> UploadAsync(MobileExceptionReport report, CancellationToken cancellationToken = default)
        {
            Attempts++;
            return Task.FromResult(_results.Count > 0 && _results.Dequeue());
        }
    }

    private sealed class BlockingUploader : IMobileExceptionReportUploader
    {
        private readonly TaskCompletionSource<object?> _started;
        private readonly TaskCompletionSource<object?> _release;
        private int _attempts;

        public BlockingUploader(
            TaskCompletionSource<object?> started,
            TaskCompletionSource<object?> release)
        {
            _started = started;
            _release = release;
        }

        public int Attempts => Volatile.Read(ref _attempts);

        public async Task<bool> UploadAsync(MobileExceptionReport report, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _attempts);
            _started.TrySetResult(null);
            await _release.Task.WaitAsync(cancellationToken);
            return true;
        }
    }

    private sealed class InMemoryExceptionReportQueue : IMobileExceptionReportQueue
    {
        public List<QueuedMobileExceptionReport> Reports { get; } = [];

        public Task EnqueueAsync(MobileExceptionReport report, CancellationToken cancellationToken = default)
        {
            Reports.Add(new QueuedMobileExceptionReport(report.Id, report));
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<QueuedMobileExceptionReport> ReadPendingAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var report in Reports.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return report;
                await Task.Yield();
            }
        }

        public Task DeleteAsync(QueuedMobileExceptionReport report, CancellationToken cancellationToken = default)
        {
            Reports.RemoveAll(item => item.QueueId == report.QueueId);
            return Task.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public MutableTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan value)
        {
            _now += value;
        }
    }
}
