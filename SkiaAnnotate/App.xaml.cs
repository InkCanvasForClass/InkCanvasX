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
        RegisterGlobalExceptionHandlers();
        OleMessageFilter.Register();
        AppLogger.Info("应用启动。");
        _backgroundViewModel = new AnnotateViewModel(new PptInteropService(), new PresentationModeService());
        _backgroundViewModel.StartSilentLinkMode();
        AppLogger.Info($"后台联动已启动，日志目录：{AppLogger.CurrentLogDirectory}");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _backgroundViewModel?.Dispose();
        OleMessageFilter.Revoke();
        AppLogger.Info("应用退出。");
        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Error("UI 线程未处理异常。", args.Exception);
            // Prevent crash for recoverable errors (COM busy etc.)
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                AppLogger.Error("应用域未处理异常。", ex);
            }
            else
            {
                AppLogger.Error("应用域未处理异常（非 Exception 对象）。");
            }
        };
    }
}

