using Peak.Can.Basic;

namespace PcanCanMonitor;

public sealed class PcanMonitorService : IAsyncDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private PcanChannel _activeChannel;
    private int _activeBitrate;
    private string _activeChannelName = "—";
    private bool _initialized;

    public event EventHandler<CanFrame>? FrameReceived;
    public event EventHandler<MonitorStatusEventArgs>? StatusChanged;

    public bool IsRunning => _worker is { IsCompleted: false };
    public string ActiveChannelName => _activeChannelName;
    public int ActiveBitrate => _activeBitrate;

    public Task StartAsync()
    {
        lock (_sync)
        {
            if (IsRunning)
            {
                AppLog.Info("Capture start requested while the monitor is already running.");
                return Task.CompletedTask;
            }
            AppLog.Info("Starting PCAN capture worker.");
            _cancellation = new CancellationTokenSource();
            _worker = Task.Run(() => RunAsync(_cancellation.Token));
            return Task.CompletedTask;
        }
    }

    public async Task StopAsync()
    {
        Task? worker;
        lock (_sync)
        {
            AppLog.Info("Stopping PCAN capture worker.");
            _cancellation?.Cancel();
            worker = _worker;
        }
        if (worker is not null)
        {
            try { await worker.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task RunAsync(CancellationToken cancellation)
    {
        try
        {
            AppLog.Info("Beginning PCAN-USB discovery and passive bitrate detection.");
            var result = await DetectChannelAsync(cancellation).ConfigureAwait(false);
            if (!result.Found)
            {
                AppLog.Warn($"PCAN discovery did not open a channel: {result.Message}");
                PublishStatus(result.Mode, result.Message, null);
                return;
            }

            _activeChannel = result.Channel;
            _activeChannelName = result.ChannelName;
            _activeBitrate = result.Bitrate;
            _initialized = true;
            AppLog.Info($"PCAN channel opened: {result.ChannelName}; bitrate={result.Bitrate}; message={result.Message}");
            PublishStatus(MonitorMode.Live, result.Message, new PcanChannelInfo(result.ChannelName, "PEAK-System PCAN-USB", true));

            while (!cancellation.IsCancellationRequested)
            {
                var status = Api.Read(_activeChannel, out var message);
                if (status == PcanStatus.OK)
                {
                    if ((message.MsgType & (MessageType.Status | MessageType.Error)) == 0)
                    {
                        var isExtended = (message.MsgType & MessageType.Extended) == MessageType.Extended;
                        var isRemote = (message.MsgType & MessageType.RemoteRequest) == MessageType.RemoteRequest;
                        var length = Math.Min((int)message.Length, 8);
                        var messageData = message.Data is null ? [] : (byte[])message.Data;
                        var data = messageData.Take(length).ToArray();
                        FrameReceived?.Invoke(this, new CanFrame(DateTime.UtcNow, _activeChannelName, _activeBitrate,
                            message.ID, isExtended, isRemote, data));
                    }
                }
                else if ((status & PcanStatus.ReceiveQueueEmpty) != PcanStatus.ReceiveQueueEmpty)
                {
                    AppLog.Error($"PCAN read returned status {status}: {PcanBasicPackageAdapter.GetErrorTextSafe(status)}");
                    PublishStatus(MonitorMode.Error, PcanBasicPackageAdapter.GetErrorTextSafe(status), null);
                    break;
                }
                else
                {
                    await Task.Delay(12, cancellation).ConfigureAwait(false);
                }
            }
        }
        catch (DllNotFoundException)
        {
            AppLog.Warn("PCANBasic.dll was not found.");
            PublishStatus(MonitorMode.DriverUnavailable, "PCANBasic.dll was not found. Connect the PEAK driver to enable live capture.", null);
        }
        catch (EntryPointNotFoundException)
        {
            AppLog.Error("The installed PCANBasic.dll does not expose the expected entry points.");
            PublishStatus(MonitorMode.DriverUnavailable, "The installed PCANBasic.dll is incompatible with this monitor.", null);
        }
        catch (TypeInitializationException exception)
        {
            var message = exception.InnerException?.Message ?? exception.Message;
            AppLog.Error("The managed PCAN-Basic API could not initialize the native driver.", exception);
            PublishStatus(MonitorMode.DriverUnavailable, message, null);
        }
        catch (PcanBasicException exception)
        {
            AppLog.Error("PCAN-Basic reported a driver/API exception.", exception);
            PublishStatus(MonitorMode.DriverUnavailable, exception.Message, null);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            AppLog.Info("PCAN capture worker canceled.");
        }
        catch (Exception exception)
        {
            AppLog.Error("Unexpected PCAN capture worker exception.", exception);
            PublishStatus(MonitorMode.Error, exception.Message, null);
        }
        finally
        {
            ReleaseChannel();
        }
    }

    private async Task<DetectionResult> DetectChannelAsync(CancellationToken cancellation)
    {
        var connected = new List<PcanChannel>();
        foreach (var channel in PcanBasicPackageAdapter.UsbChannels)
        {
            cancellation.ThrowIfCancellationRequested();
            var first = Api.Initialize(channel, Bitrate.Pcan500);
            if (first != PcanStatus.OK)
            {
                AppLog.Info($"PCAN-USB channel {channel} unavailable: {first}");
                continue;
            }
            AppLog.Info($"PCAN-USB channel {channel} responded to discovery.");
            connected.Add(channel);
            Api.Uninitialize(channel);
        }

        if (connected.Count == 0)
            return new DetectionResult(false, default, 0, "", MonitorMode.Waiting, "No PCAN-USB interface detected. Connect hardware or import a capture file.");

        foreach (var channel in connected)
        {
            foreach (var (rate, bitrate) in PcanBasicPackageAdapter.CommonBitrates)
            {
                cancellation.ThrowIfCancellationRequested();
                var status = Api.Initialize(channel, bitrate);
                if (status != PcanStatus.OK)
                {
                    AppLog.Info($"Bitrate probe skipped {rate} on channel {channel}: {status}");
                    continue;
                }

                var gotFrame = await ObserveForFrameAsync(channel, cancellation).ConfigureAwait(false);
                if (gotFrame)
                {
                    AppLog.Info($"Bitrate probe observed traffic at {rate} on channel {channel}.");
                    return new DetectionResult(true, channel, rate, FormatChannelName(channel), MonitorMode.Live,
                        $"Listening on {FormatChannelName(channel)} · {FormatRate(rate)}");
                }

                AppLog.Info($"Bitrate probe observed no traffic at {rate} on channel {channel}.");
                Api.Uninitialize(channel);
            }

            var fallbackStatus = Api.Initialize(channel, Bitrate.Pcan500);
            if (fallbackStatus == PcanStatus.OK)
            {
                AppLog.Warn($"No bitrate probe saw traffic; using 500000 on channel {channel} while waiting for confirmation.");
                return new DetectionResult(true, channel, 500_000, FormatChannelName(channel), MonitorMode.Live,
                    $"{FormatChannelName(channel)} found · waiting for traffic to confirm bitrate");
            }
        }

        return new DetectionResult(false, default, 0, "", MonitorMode.Error, "PCAN hardware was found, but no usable channel could be opened.");
    }

    private static async Task<bool> ObserveForFrameAsync(PcanChannel channel, CancellationToken cancellation)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(220);
        while (DateTime.UtcNow < deadline)
        {
            cancellation.ThrowIfCancellationRequested();
            var status = Api.Read(channel, out var message);
            if (status == PcanStatus.OK && (message.MsgType & (MessageType.Status | MessageType.Error)) == 0)
                return true;
            await Task.Delay(14, cancellation).ConfigureAwait(false);
        }
        return false;
    }

    private void ReleaseChannel()
    {
        if (_initialized)
        {
            try { Api.Uninitialize(_activeChannel); }
            catch { }
            _initialized = false;
        }
    }

    private void PublishStatus(MonitorMode mode, string message, PcanChannelInfo? channel)
        => StatusChanged?.Invoke(this, new MonitorStatusEventArgs(mode, message, channel));

    private static string FormatRate(int rate) => rate >= 1_000_000 ? $"{rate / 1_000_000d:0.#} Mbit/s" : $"{rate / 1_000d:0.#} kbit/s";

    private static string FormatChannelName(PcanChannel channel) => $"PCAN-USB {channel.ToString()[3..]}";

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cancellation?.Dispose();
    }

    private sealed record DetectionResult(bool Found, PcanChannel Channel, int Bitrate, string ChannelName, MonitorMode Mode, string Message);
}

public sealed class MonitorStatusEventArgs(MonitorMode mode, string message, PcanChannelInfo? channel) : EventArgs
{
    public MonitorMode Mode { get; } = mode;
    public string Message { get; } = message;
    public PcanChannelInfo? Channel { get; } = channel;
}
