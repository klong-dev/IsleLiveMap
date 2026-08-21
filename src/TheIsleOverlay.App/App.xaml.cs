using System.Windows;

namespace TheIsleOverlay.App;

public partial class App : Application
{
    public App()
    {
        Team = new TeamCoordinator();
    }

    public TeamCoordinator Team { get; }

    public static TeamCoordinator CurrentTeam => ((App)Current).Team;

    protected override void OnExit(ExitEventArgs e)
    {
        Team.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
