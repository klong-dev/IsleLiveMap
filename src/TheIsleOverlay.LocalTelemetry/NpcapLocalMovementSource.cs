using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading.Channels;
using PacketDotNet;
using SharpPcap;

namespace TheIsleOverlay.LocalTelemetry;

public sealed class NpcapLocalMovementSource : ILocalMovementSource
{
    public const string DefaultGameProcessName = "TheIsleClient-Win64-Shipping";

    private static readonly TimeSpan ProcessPollInterval = TimeSpan.FromSeconds(1);

    private readonly string _processName;
    private readonly WindowsUdpPortOwnerResolver _portResolver;
    private readonly LocalMovementTracker _tracker;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private int _watchStarted;
    private int _disposed;

    public NpcapLocalMovementSource(
        string processName = DefaultGameProcessName,
        WindowsUdpPortOwnerResolver? portResolver = null,
        LocalMovementTracker? tracker = null)
    {
        _processName = processName;
        _portResolver = portResolver ?? new WindowsUdpPortOwnerResolver();
        _tracker = tracker ?? new LocalMovementTracker();
    }

    public async IAsyncEnumerable<LocalMovementObservation> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _watchStarted, 1) != 0)
        {
            throw new InvalidOperationException("A local movement source can only be watched once.");
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        var channel = Channel.CreateBounded<LocalMovementObservation>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
        var captureTask = RunAsync(channel.Writer, linkedCancellation.Token);

        await foreach (var observation in channel.Reader
                           .ReadAllAsync(linkedCancellation.Token)
                           .ConfigureAwait(false))
        {
            yield return observation;
        }

        await captureTask.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _disposeCancellation.Cancel();
        _disposeCancellation.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task RunAsync(
        ChannelWriter<LocalMovementObservation> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var processId = FindGameProcessId();
                var ports = processId is null
                    ? new HashSet<int>()
                    : _portResolver.GetOwnedPorts(processId.Value);
                if (processId is null || ports.Count == 0)
                {
                    await Task.Delay(ProcessPollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _tracker.Reset();
                await CaptureUntilEndpointChangesAsync(
                        processId.Value,
                        ports,
                        writer,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
            return;
        }

        writer.TryComplete();
    }

    private async Task CaptureUntilEndpointChangesAsync(
        int processId,
        IReadOnlySet<int> ports,
        ChannelWriter<LocalMovementObservation> writer,
        CancellationToken cancellationToken)
    {
        var devices = OpenCaptureDevices(ports, writer);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(ProcessPollInterval, cancellationToken).ConfigureAwait(false);
                if (FindGameProcessId() != processId
                    || !_portResolver.GetOwnedPorts(processId).SetEquals(ports))
                {
                    return;
                }
            }
        }
        finally
        {
            foreach (var capture in devices)
            {
                capture.Device.OnPacketArrival -= capture.Handler;
                try
                {
                    capture.Device.StopCapture();
                }
                catch
                {
                }

                try
                {
                    capture.Device.Close();
                }
                catch
                {
                }
            }
        }
    }

    private IReadOnlyList<OpenedCapture> OpenCaptureDevices(
        IReadOnlySet<int> ports,
        ChannelWriter<LocalMovementObservation> writer)
    {
        CaptureDeviceList devices;
        try
        {
            devices = CaptureDeviceList.Instance;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or TypeInitializationException
                or PcapException)
        {
            throw new LocalPacketCaptureUnavailableException(
                "Npcap is not installed or its packet-capture driver is unavailable.",
                exception);
        }

        var candidates = SelectActiveDevices(devices).ToArray();
        var opened = new List<OpenedCapture>();
        var filter = $"udp and ({string.Join(" or ", ports.Select(port => $"src port {port}"))})";
        foreach (var device in candidates)
        {
            PacketArrivalEventHandler handler = (_, packetCapture) =>
                HandlePacket(packetCapture, ports, writer);
            try
            {
                device.Open(DeviceModes.None, read_timeout: 250);
                device.Filter = filter;
                device.OnPacketArrival += handler;
                device.StartCapture();
                opened.Add(new OpenedCapture(device, handler));
            }
            catch
            {
                device.OnPacketArrival -= handler;
                try
                {
                    device.Close();
                }
                catch
                {
                }
            }
        }

        if (opened.Count == 0)
        {
            throw new LocalPacketCaptureUnavailableException(
                "No active network adapter could be opened through Npcap.");
        }

        return opened;
    }

    private void HandlePacket(
        PacketCapture packetCapture,
        IReadOnlySet<int> ports,
        ChannelWriter<LocalMovementObservation> writer)
    {
        try
        {
            var rawPacket = packetCapture.GetPacket();
            var packet = Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);
            var udp = packet.Extract<UdpPacket>();
            if (udp is null || !ports.Contains(udp.SourcePort))
            {
                return;
            }

            var payload = udp.PayloadData;
            if (payload is null || payload.Length == 0)
            {
                return;
            }

            var observedAt = DateTimeOffset.UtcNow;
            lock (_tracker)
            {
                if (_tracker.TryTrack(payload, observedAt, out var movement))
                {
                    writer.TryWrite(new LocalMovementObservation(observedAt, movement));
                }
            }
        }
        catch
        {
            // Malformed or unrelated UDP traffic must not stop local tracking.
        }
    }

    private int? FindGameProcessId()
    {
        var processes = Process.GetProcessesByName(_processName);
        try
        {
            return processes.Length == 0 ? null : processes[0].Id;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static IEnumerable<ILiveDevice> SelectActiveDevices(CaptureDeviceList devices)
    {
        var activeDescriptions = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network =>
                network.OperationalStatus == OperationalStatus.Up
                && network.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && network.GetIPProperties().GatewayAddresses.Count > 0)
            .Select(network => network.Description)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var active = devices
            .Where(device =>
                !string.IsNullOrWhiteSpace(device.Description)
                && activeDescriptions.Contains(device.Description))
            .ToArray();
        return active.Length > 0 ? active : devices;
    }

    private sealed record OpenedCapture(
        ILiveDevice Device,
        PacketArrivalEventHandler Handler);
}
