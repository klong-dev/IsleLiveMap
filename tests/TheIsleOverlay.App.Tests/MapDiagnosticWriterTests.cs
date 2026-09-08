using System.IO;
using System.Text;

namespace TheIsleOverlay.App.Tests;

public sealed class MapDiagnosticWriterTests
{
    [Fact]
    public async Task SlowDisk_DoesNotBlockProducerAndRetainsNewestRecord()
    {
        var disk = new GatedWriter();
        await using var writer = new MapDiagnosticWriter(disk);
        writer.Publish(new { Sequence = 0 });
        await disk.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            for (var sequence = 1; sequence <= 1_000; sequence++)
                writer.Publish(new { Sequence = sequence });
        }
        finally { disk.Release.TrySetResult(); }
        await writer.DisposeAsync();
        Assert.Equal(3, disk.Records.Count);
        Assert.Contains("\"Sequence\":1000", disk.Records[^1]);
    }

    [Fact]
    public async Task DiskFailure_DisablesDiagnosticsWithoutEscapingToOverlay()
    {
        await using var writer = new MapDiagnosticWriter(new FailedWriter());
        writer.Publish(new { Sequence = 1 });
        await writer.DisposeAsync();
        writer.Publish(new { Sequence = 2 });
    }

    private sealed class GatedWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Records { get; } = [];
        public override async Task WriteLineAsync(string? value)
        {
            Started.TrySetResult();
            await Release.Task;
            Records.Add(value!);
        }
    }

    private sealed class FailedWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override Task WriteLineAsync(string? value) => throw new IOException("Simulated full disk.");
    }
}
