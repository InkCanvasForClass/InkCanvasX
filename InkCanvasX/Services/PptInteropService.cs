using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SkiaAnnotate.Services;

public sealed class PptInteropService : IDisposable
{
    private const int RpcECallRejected = unchecked((int)0x80010001);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid rclsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private dynamic? _application;
    private dynamic? _presentation;
    private bool _ownsApplication;
    private bool _ownsPresentation;
    private bool _disposed;

    public bool IsOpened => _presentation is not null;
    public bool IsConnectedToPowerPoint => _application is not null;

    public void Open(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("未找到指定的 PPT 文件。", filePath);
        }

        ClosePresentation();

        try
        {
            if (_application is null)
            {
                _application = CreateApplication();
                _ownsApplication = true;
            }
            _presentation = _application.Presentations.Open(filePath, ReadOnly: false, Untitled: false, WithWindow: false);
            _ownsPresentation = true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            throw new InvalidOperationException("无法打开 PPT。请确认本机已安装 Microsoft Office 并重试。", ex);
        }
    }

    public void ConnectToRunningPowerPoint()
    {
        try
        {
            var appType = Type.GetTypeFromProgID("PowerPoint.Application")
                          ?? throw new InvalidOperationException("未检测到 PowerPoint COM 组件。");
            var clsId = appType.GUID;
            GetActiveObject(ref clsId, IntPtr.Zero, out var activeObject);
            _application = activeObject;
            _ownsApplication = false;

            try
            {
                _presentation = _application.ActivePresentation;
                _ownsPresentation = false;
            }
            catch
            {
                _presentation = null;
            }
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException("未检测到正在运行的 PowerPoint。请先打开 PowerPoint 和演示文稿。", ex);
        }
    }

    public int GetSlideCount()
    {
        EnsurePresentation();
        return (int)_presentation!.Slides.Count;
    }

    public BitmapImage RenderSlideToImage(int slideIndex, int width = 1600, int height = 900)
    {
        EnsurePresentation();
        var presentation = _presentation!;

        if (slideIndex < 1 || slideIndex > presentation.Slides.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(slideIndex), "幻灯片索引超出范围。");
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"SkiaAnnotate_{Guid.NewGuid():N}.png");
        dynamic? slide = null;

        try
        {
            slide = presentation.Slides[slideIndex];
            slide.Export(tempFile, "PNG", width, height);

            var image = new BitmapImage();
            using var stream = File.OpenRead(tempFile);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        finally
        {
            if (slide is not null)
            {
                Marshal.FinalReleaseComObject(slide);
            }

            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    public bool TryRefreshActivePresentation()
    {
        try
        {
            if (_application is null)
            {
                return false;
            }

            _presentation = RetryCom(() => _application.ActivePresentation);
            return _presentation is not null;
        }
        catch
        {
            return false;
        }
    }

    public bool IsSlideShowRunning()
    {
        if (_application is null)
        {
            return false;
        }

        try
        {
            return RetryCom(() => (int)_application.SlideShowWindows.Count) > 0;
        }
        catch
        {
            return false;
        }
    }

    public void EndSlideShow()
    {
        var slideShowWindow = GetSlideShowWindowOrNull();
        if (slideShowWindow is null)
        {
            return;
        }

        try
        {
            slideShowWindow.View.Exit();
        }
        catch
        {
            // 放映可能已结束或窗口已失效，忽略退出异常。
        }
    }

    public int GetCurrentSlideNumberInShow()
    {
        var slideShowWindow = GetSlideShowWindowOrNull();
        if (slideShowWindow is null)
        {
            return 0;
        }

        try
        {
            return RetryCom(() => (int)slideShowWindow.View.CurrentShowPosition);
        }
        catch
        {
            return 0;
        }
    }

    public void NextSlideInShow()
    {
        var slideShowWindow = GetSlideShowWindowOrNull();
        if (slideShowWindow is null)
        {
            return;
        }

        try
        {
            RetryCom(() => { slideShowWindow.View.Next(); return 0; });
        }
        catch
        {
            // ignore
        }
    }

    public void PreviousSlideInShow()
    {
        var slideShowWindow = GetSlideShowWindowOrNull();
        if (slideShowWindow is null)
        {
            return;
        }

        try
        {
            RetryCom(() => { slideShowWindow.View.Previous(); return 0; });
        }
        catch
        {
            // ignore
        }
    }

    public Rect GetSlideShowBounds()
    {
        var slideShowWindow = GetSlideShowWindowOrNull();
        if (slideShowWindow is null)
        {
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        try
        {
            var hwnd = new IntPtr((int)slideShowWindow.HWND);
            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var rect))
            {
                return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }
        }
        catch
        {
            // 回退到 COM 提供的窗口边界。
        }

        return new Rect((double)slideShowWindow.Left, (double)slideShowWindow.Top, (double)slideShowWindow.Width, (double)slideShowWindow.Height);
    }

    private void EnsurePresentation()
    {
        if (_presentation is null)
        {
            throw new InvalidOperationException("请先打开一个 PPT 文件。");
        }
    }

    private void ClosePresentation()
    {
        if (_presentation is null)
        {
            return;
        }

        if (_ownsPresentation)
        {
            _presentation.Close();
        }

        Marshal.FinalReleaseComObject(_presentation);
        _presentation = null;
        _ownsPresentation = false;
    }

    private dynamic? GetSlideShowWindowOrNull()
    {
        if (_application is null)
        {
            return null;
        }

        try
        {
            var count = RetryCom(() => (int)_application.SlideShowWindows.Count);
            return count > 0 ? RetryCom(() => _application.SlideShowWindows[1]) : null;
        }
        catch
        {
            return null;
        }
    }

    private static T RetryCom<T>(Func<T> action, int maxAttempts = 6)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (COMException ex) when (ex.HResult == RpcECallRejected)
            {
                var delayMs = 20 + attempt * 20;
                System.Threading.Thread.Sleep(delayMs);
            }
        }

        return action();
    }

    private static dynamic CreateApplication()
    {
        var appType = Type.GetTypeFromProgID("PowerPoint.Application");
        if (appType is null)
        {
            throw new InvalidOperationException("未检测到 PowerPoint COM 组件。");
        }

        return Activator.CreateInstance(appType)
               ?? throw new InvalidOperationException("无法启动 PowerPoint 应用程序。");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ClosePresentation();

        if (_application is not null)
        {
            if (_ownsApplication)
            {
                _application.Quit();
            }

            Marshal.FinalReleaseComObject(_application);
            _application = null;
        }

        _ownsApplication = false;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
