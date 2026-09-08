using System.IO;
using System.Text.Json;
using System.Threading.Channels;

namespace TheIsleOverlay.App;

/// <summary>Opt-in diagnostics must never put disk backpressure on the UI.</summary>
internal sealed class MapDiagnosticWriter : IAsyncDisposable
{
    private readonly TextWriter _writer;
    private readonly Channel<object> _records = Channel.CreateBounded<object>(
        new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    private readonly Task _pump;

    public MapDiagnosticWriter(TextWriter writer)
    {
        _writer = writer;
        _pump = Task.Run(PumpAsync);
    }

    public void Publish(object record) => _records.Writer.TryWrite(record);

    public async ValueTask DisposeAsync()
    {
        _records.Writer.TryComplete();
        await _pump.ConfigureAwait(false);
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var record in _records.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await _writer.WriteLineAsync(JsonSerializer.Serialize(record)).ConfigureAwait(false);
                await _writer.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                         or JsonException or NotSupportedException)
        {
            // Diagnostic files are optional; disable only this writer on I/O failure.
            _records.Writer.TryComplete();
        }
        finally
        {
            try { await _writer.DisposeAsync().ConfigureAwait(false); }
            catch (IOException) { }
        }
    }
}
