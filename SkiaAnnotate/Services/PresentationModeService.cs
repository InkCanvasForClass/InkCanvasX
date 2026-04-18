using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SkiaAnnotate.Controls;
using SkiaAnnotate.Windows;

namespace SkiaAnnotate.Services;

public sealed class PresentationModeService
{
    private AnnotationOverlayWindow? _overlayWindow;
    private readonly Dictionary<int, List<SkiaStroke>> _slideInkCache = new();
    private int _activeSlideNumber;
    private bool _isSlideContentInitialized;

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

    public void EnsureOverlayHidden()
    {
        if (_overlayWindow is null)
        {
            return;
        }

        var overlay = _overlayWindow;
        _overlayWindow = null;
        SaveCurrentSlideInkSnapshot();
        _slideInkCache.Clear();
        _activeSlideNumber = 0;
        _isSlideContentInitialized = false;
        overlay.Close();
    }

    public void ExitOverlayAndSlideShow(PptInteropService pptInteropService)
    {
        EnsureOverlayHidden();
        pptInteropService.EndSlideShow();
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
        Keyboard.Focus(overlayWindow);
        _activeSlideNumber = currentSlideNumber;
        _isSlideContentInitialized = false;
        SyncStrokesForSlide(currentSlideNumber);
    }

    private static Rect GetFullscreenCanvasBounds()
    {
        return new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
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
            strokes = new List<SkiaStroke>();
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

        _slideInkCache[_activeSlideNumber] = _overlayWindow.GetCurrentStrokesSnapshot();
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
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(70)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var currentSlideNumber = pptInteropService.GetCurrentSlideNumberInShow();
            SyncStrokesForSlide(currentSlideNumber);
        };
        timer.Start();
    }
}
