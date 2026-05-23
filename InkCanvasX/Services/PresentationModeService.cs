using System.Runtime.InteropServices;
using Jalium.UI;
using Jalium.UI.Controls.Ink;
using Jalium.UI.Threading;
using SkiaAnnotate.Windows;

namespace SkiaAnnotate.Services;

public sealed class PresentationModeService
{
    private AnnotationOverlayWindow? _overlayWindow;
    private readonly Dictionary<int, StrokeCollection> _slideInkCache = new();
    private readonly DispatcherTimer _slideSyncRetryTimer;
    private PptInteropService? _pendingSyncService;
    private int _syncRetryCount;
    private int _activeSlideNumber;
    private bool _isSlideContentInitialized;

    public PresentationModeService()
    {
        _slideSyncRetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(45)
        };
        _slideSyncRetryTimer.Tick += OnSlideSyncRetryTick;
    }

    public bool IsPresentationMode => _overlayWindow is not null;

    public void EnsureOverlayVisible(PptInteropService pptInteropService)
    {
        var bounds = GetFullscreenCanvasBounds();
        var currentSlideNumber = pptInteropService.GetCurrentSlideNumberInShow();

        if (_overlayWindow is not null)
        {
            _overlayWindow.Left = bounds.Left;
            _overlayWindow.Top = bounds.Top;
            _overlayWindow.Width = bounds.Width;
            _overlayWindow.Height = bounds.Height;
            SyncStrokesForSlide(currentSlideNumber);
            return;
        }

        Enter(pptInteropService, bounds, currentSlideNumber);
    }

    public void EnsureOverlayHidden(bool clearInkCache = false)
    {
        if (_overlayWindow is null)
        {
            if (clearInkCache)
            {
                ResetSession();
            }
            return;
        }

        var overlay = _overlayWindow;
        _overlayWindow = null;
        _slideSyncRetryTimer.Stop();
        _pendingSyncService = null;
        _syncRetryCount = 0;
        SaveCurrentSlideInkSnapshot();
        if (clearInkCache)
        {
            ResetSession();
        }
        overlay.Close();
    }

    public void ExitOverlayAndSlideShow(PptInteropService pptInteropService)
    {
        EnsureOverlayHidden(clearInkCache: true);
        pptInteropService.EndSlideShow();
    }

    public void ResetSession()
    {
        _slideInkCache.Clear();
        _activeSlideNumber = 0;
        _isSlideContentInitialized = false;
    }

    private void Enter(PptInteropService pptInteropService, Rect bounds, int currentSlideNumber)
    {
        var overlayWindow = new AnnotationOverlayWindow
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height
        };

        overlayWindow.NextSlideRequested += () => HandleNextSlideRequested(pptInteropService);
        overlayWindow.PreviousSlideRequested += () => HandlePreviousSlideRequested(pptInteropService);
        overlayWindow.ExitRequested += () => ExitOverlayAndSlideShow(pptInteropService);
        overlayWindow.Closed += (_, _) =>
        {
            _overlayWindow = null;
        };

        _overlayWindow = overlayWindow;
        overlayWindow.Show();
        overlayWindow.Activate();
        _activeSlideNumber = currentSlideNumber;
        _isSlideContentInitialized = false;
        SyncStrokesForSlide(currentSlideNumber);
    }

    private static Rect GetFullscreenCanvasBounds()
    {
        return GetSystemMetricsBounds();
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static Rect GetSystemMetricsBounds()
    {
        const int smXVirtualScreen = 76;
        const int smYVirtualScreen = 77;
        const int smCxVirtualScreen = 78;
        const int smCyVirtualScreen = 79;

        return new Rect(
            GetSystemMetrics(smXVirtualScreen),
            GetSystemMetrics(smYVirtualScreen),
            GetSystemMetrics(smCxVirtualScreen),
            GetSystemMetrics(smCyVirtualScreen));
    }

    private void SyncStrokesForSlide(int currentSlideNumber)
    {
        if (_overlayWindow is null || currentSlideNumber <= 0)
        {
            return;
        }

        if (_activeSlideNumber != currentSlideNumber)
        {
            SaveCurrentSlideInkSnapshot();
            _activeSlideNumber = currentSlideNumber;
            _isSlideContentInitialized = false;
        }

        if (_isSlideContentInitialized)
        {
            return;
        }

        if (!_slideInkCache.TryGetValue(currentSlideNumber, out var strokes))
        {
            strokes = new StrokeCollection();
            _slideInkCache[currentSlideNumber] = strokes;
        }

        _overlayWindow.SetCurrentStrokes(strokes);

        _isSlideContentInitialized = true;
    }

    private void SaveCurrentSlideInkSnapshot()
    {
        if (_overlayWindow is null || _activeSlideNumber <= 0)
        {
            return;
        }

        _slideInkCache[_activeSlideNumber] = _overlayWindow.GetCurrentStrokesSnapshot().Clone();
    }

    private void HandleNextSlideRequested(PptInteropService pptInteropService)
    {
        SaveCurrentSlideInkSnapshot();
        pptInteropService.NextSlideInShow();
        ScheduleSlideSync(pptInteropService);
    }

    private void HandlePreviousSlideRequested(PptInteropService pptInteropService)
    {
        SaveCurrentSlideInkSnapshot();
        pptInteropService.PreviousSlideInShow();
        ScheduleSlideSync(pptInteropService);
    }

    private void ScheduleSlideSync(PptInteropService pptInteropService)
    {
        var currentSlideNumber = pptInteropService.GetCurrentSlideNumberInShow();
        SyncStrokesForSlide(currentSlideNumber);

        _pendingSyncService = pptInteropService;
        _syncRetryCount = 0;
        _slideSyncRetryTimer.Start();
    }

    private void OnSlideSyncRetryTick(object? sender, EventArgs e)
    {
        if (_pendingSyncService is null || _overlayWindow is null)
        {
            _slideSyncRetryTimer.Stop();
            return;
        }

        var slideNumber = _pendingSyncService.GetCurrentSlideNumberInShow();
        SyncStrokesForSlide(slideNumber);

        _syncRetryCount++;
        if (_syncRetryCount >= 5)
        {
            _slideSyncRetryTimer.Stop();
            _pendingSyncService = null;
        }
    }
}
