using System.Windows;

namespace TheIsleOverlay.App;

public partial class App : Application
{
    private int _donatePromptShown;
    private int _guidePromptShown;

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

    protected override void OnExit(ExitEventArgs e)
    {
        Team.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
