using System.Windows;
using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.App;

public partial class App : Application
{
    private int _donatePromptShown;
    private int _guidePromptShown;
    private readonly object _localTelemetryGate = new();
    private PrewarmedLocalMovementSource? _warmLocalTelemetry;

    public App()
    {
        Team = new TeamCoordinator();
    }

    public TeamCoordinator Team { get; }

    public static App CurrentApp => (App)Current;

    public static TeamCoordinator CurrentTeam => ((App)Current).Team;

    public bool TryMarkDonatePromptShown() =>
        Interlocked.Exchange(ref _donatePromptShown, 1) == 0;

    public bool TryMarkGuidePromptShown() =>
        Interlocked.Exchange(ref _guidePromptShown, 1) == 0;

    public void EnsureLocalTelemetryWarmup()
    {
        lock (_localTelemetryGate)
        {
            if (_warmLocalTelemetry is not null)
            {
                return;
            }

            _warmLocalTelemetry = new PrewarmedLocalMovementSource();
            _warmLocalTelemetry.Start();
        }
    }

    public ILocalMovementSource TakeLocalTelemetrySource()
    {
        lock (_localTelemetryGate)
        {
            if (_warmLocalTelemetry is null)
            {
                _warmLocalTelemetry = new PrewarmedLocalMovementSource();
                _warmLocalTelemetry.Start();
            }

            var source = _warmLocalTelemetry;
            _warmLocalTelemetry = null;
            return source;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        PrewarmedLocalMovementSource? warmSource;
        lock (_localTelemetryGate)
        {
            warmSource = _warmLocalTelemetry;
            _warmLocalTelemetry = null;
        }
        warmSource?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Team.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
