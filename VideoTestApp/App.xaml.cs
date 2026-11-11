using System.Windows;

namespace VideoTestApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow(StartupOptions.Parse(e.Args), MediaLibrary.DefaultFolder);
        window.Closed += (_, _) => Shutdown(window.ExitCode);
        window.Show();
    }
}
