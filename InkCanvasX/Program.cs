using Jalium.UI.Controls;

namespace SkiaAnnotate;

internal static class Program
{
    [System.STAThread]
    private static int Main(string[] args)
    {
        var app = new App
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        return app.Run(args);
    }
}
