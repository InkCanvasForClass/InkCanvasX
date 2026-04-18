using System.Windows;
using SkiaAnnotate.Services;
using SkiaAnnotate.ViewModels;

namespace SkiaAnnotate;

public partial class App : Application
{
    private AnnotateViewModel? _backgroundViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _backgroundViewModel = new AnnotateViewModel(new PptInteropService(), new PresentationModeService());
        _backgroundViewModel.StartSilentLinkMode();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _backgroundViewModel?.Dispose();
        base.OnExit(e);
    }
}

