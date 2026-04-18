using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SkiaAnnotate.Commands;
using SkiaAnnotate.Services;
using System.Windows.Threading;

namespace SkiaAnnotate.ViewModels;

public sealed class AnnotateViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly PptInteropService _pptInteropService;
    private readonly PresentationModeService _presentationModeService;
    private readonly Dictionary<int, StrokeCollection> _pageStrokes = new();
    private readonly DispatcherTimer _linkMonitorTimer;
    private bool _isLinkModeEnabled;
    private bool _wasSlideShowRunning;
    private bool _wasConnected;
    private string? _lastStatusMessage;

    private int _slideCount;
    private int _currentSlideIndex;
    private BitmapImage? _currentSlideImage;
    private string _statusMessage = "请先启动 PowerPoint 并打开演示文稿。";
    private InkCanvasEditingMode _editingMode = InkCanvasEditingMode.Ink;
    private DrawingAttributes _drawingAttributes = CreateDefaultDrawingAttributes();

    public AnnotateViewModel(PptInteropService pptInteropService, PresentationModeService presentationModeService)
    {
        _pptInteropService = pptInteropService;
        _presentationModeService = presentationModeService;

        _linkMonitorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _linkMonitorTimer.Tick += (_, _) => MonitorSlideShow();

        OpenPptCommand = new RelayCommand(OpenPptFile);
        ConnectPowerPointCommand = new RelayCommand(ConnectPowerPoint);
        NextSlideCommand = new RelayCommand(NextSlide, () => CanNavigateSlide(1));
        PreviousSlideCommand = new RelayCommand(PreviousSlide, () => CanNavigateSlide(-1));
        SetPenModeCommand = new RelayCommand(() => EditingMode = InkCanvasEditingMode.Ink);
        SetEraserModeCommand = new RelayCommand(() => EditingMode = InkCanvasEditingMode.EraseByStroke);
        ClearCurrentPageCommand = new RelayCommand(ClearCurrentPage);
        TogglePresentationModeCommand = new RelayCommand<Window>(TogglePresentationMode);
        UseBlackPenCommand = new RelayCommand(() => SetPenColor(Colors.Black));
        UseRedPenCommand = new RelayCommand(() => SetPenColor(Colors.Red));
        UseBluePenCommand = new RelayCommand(() => SetPenColor(Colors.DeepSkyBlue));
        IncreasePenThicknessCommand = new RelayCommand(() => ChangeThickness(2));
        DecreasePenThicknessCommand = new RelayCommand(() => ChangeThickness(-2));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand OpenPptCommand { get; }
    public RelayCommand ConnectPowerPointCommand { get; }
    public RelayCommand NextSlideCommand { get; }
    public RelayCommand PreviousSlideCommand { get; }
    public RelayCommand SetPenModeCommand { get; }
    public RelayCommand SetEraserModeCommand { get; }
    public RelayCommand ClearCurrentPageCommand { get; }
    public RelayCommand<Window> TogglePresentationModeCommand { get; }
    public RelayCommand UseBlackPenCommand { get; }
    public RelayCommand UseRedPenCommand { get; }
    public RelayCommand UseBluePenCommand { get; }
    public RelayCommand IncreasePenThicknessCommand { get; }
    public RelayCommand DecreasePenThicknessCommand { get; }

    public BitmapImage? CurrentSlideImage
    {
        get => _currentSlideImage;
        private set
        {
            _currentSlideImage = value;
            OnPropertyChanged();
        }
    }

    public StrokeCollection CurrentStrokes { get; private set; } = new();

    public InkCanvasEditingMode EditingMode
    {
        get => _editingMode;
        private set
        {
            _editingMode = value;
            OnPropertyChanged();
        }
    }

    public DrawingAttributes DrawingAttributes
    {
        get => _drawingAttributes;
        private set
        {
            _drawingAttributes = value;
            OnPropertyChanged();
        }
    }

    public int SlideCount
    {
        get => _slideCount;
        private set
        {
            _slideCount = value;
            OnPropertyChanged();
        }
    }

    public int CurrentSlideIndex
    {
        get => _currentSlideIndex;
        private set
        {
            _currentSlideIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SlideDisplayText));
        }
    }

    public string SlideDisplayText => SlideCount > 0 ? $"{CurrentSlideIndex}/{SlideCount}" : "0/0";
    public bool IsLinkModeEnabled
    {
        get => _isLinkModeEnabled;
        private set
        {
            _isLinkModeEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LinkButtonText));
        }
    }

    public string LinkButtonText => IsLinkModeEnabled ? "停止联动监听" : "开始联动监听";

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public void StartSilentLinkMode()
    {
        if (IsLinkModeEnabled)
        {
            return;
        }

        IsLinkModeEnabled = true;
        _linkMonitorTimer.Start();
        StatusMessage = "后台联动已启动，等待 PowerPoint 放映。";
        AppLogger.Info("后台联动监听已开启。");
    }

    public void UpdateCurrentStrokes(StrokeCollection strokes)
    {
        if (SlideCount <= 0)
        {
            return;
        }

        CurrentStrokes = strokes.Clone();
        _pageStrokes[CurrentSlideIndex] = CurrentStrokes.Clone();
    }

    public void Dispose()
    {
        AppLogger.Info("释放 ViewModel 资源。");
        _pptInteropService.Dispose();
    }

    private void ConnectPowerPoint()
    {
        try
        {
            _pptInteropService.ConnectToRunningPowerPoint();
            _pptInteropService.TryRefreshActivePresentation();
            SlideCount = _pptInteropService.IsOpened ? _pptInteropService.GetSlideCount() : 0;
            CurrentSlideIndex = SlideCount > 0 ? 1 : 0;
            StatusMessage = "已连接 PowerPoint。放映开始时会自动显示批注画布。";
            AppLogger.Info($"已连接 PowerPoint。SlideCount={SlideCount}");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            AppLogger.Error("连接 PowerPoint 失败。", ex);
            MessageBox.Show(ex.Message, "连接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }


    private static DrawingAttributes CreateDefaultDrawingAttributes() =>
        new()
        {
            Color = Colors.Black,
            Width = 4,
            Height = 4,
            FitToCurve = true,
            IgnorePressure = false
        };

    private void OpenPptFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PowerPoint 文件 (*.pptx;*.ppt)|*.pptx;*.ppt|所有文件 (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _pptInteropService.Open(dialog.FileName);
            SlideCount = _pptInteropService.GetSlideCount();
            _pageStrokes.Clear();
            CurrentSlideIndex = SlideCount > 0 ? 1 : 0;
            CurrentSlideImage = null;
            StatusMessage = $"已加载：{dialog.FileName}";
            AppLogger.Info($"打开演示文稿成功：{dialog.FileName}，SlideCount={SlideCount}");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            AppLogger.Error($"打开演示文稿失败：{dialog.FileName}", ex);
            MessageBox.Show(ex.Message, "打开PPT失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RaiseNavigationCanExecuteChanged();
    }

    private void NextSlide() => GoToSlide(CurrentSlideIndex + 1);

    private void PreviousSlide() => GoToSlide(CurrentSlideIndex - 1);

    private bool CanNavigateSlide(int delta)
    {
        if (SlideCount <= 0)
        {
            return false;
        }

        var target = CurrentSlideIndex + delta;
        return target >= 1 && target <= SlideCount;
    }

    private void GoToSlide(int slideIndex)
    {
        if (SlideCount <= 0 || slideIndex < 1 || slideIndex > SlideCount)
        {
            return;
        }

        PersistCurrentStrokes();

        CurrentSlideImage = _pptInteropService.RenderSlideToImage(slideIndex);
        CurrentSlideIndex = slideIndex;
        CurrentStrokes = _pageStrokes.TryGetValue(slideIndex, out var strokes) ? strokes.Clone() : new StrokeCollection();
        OnPropertyChanged(nameof(CurrentStrokes));

        RaiseNavigationCanExecuteChanged();
    }

    private void PersistCurrentStrokes()
    {
        if (CurrentSlideIndex > 0)
        {
            _pageStrokes[CurrentSlideIndex] = CurrentStrokes.Clone();
        }
    }

    private void ClearCurrentPage()
    {
        if (SlideCount <= 0)
        {
            return;
        }

        CurrentStrokes = new StrokeCollection();
        _pageStrokes[CurrentSlideIndex] = new StrokeCollection();
        OnPropertyChanged(nameof(CurrentStrokes));
    }

    private void TogglePresentationMode(Window? ownerWindow)
    {
        if (!_pptInteropService.IsConnectedToPowerPoint)
        {
            StatusMessage = "请先点击“连接 PowerPoint”。";
            return;
        }

        if (ownerWindow is null)
        {
            StatusMessage = "未找到主窗口，无法进入联动模式。";
            return;
        }

        if (!IsLinkModeEnabled)
        {
            IsLinkModeEnabled = true;
            _linkMonitorTimer.Start();
            StatusMessage = "已开启联动监听。请在 PowerPoint 中开始放映。";
            AppLogger.Info("手动开启联动监听。");
            return;
        }

        IsLinkModeEnabled = false;
        _linkMonitorTimer.Stop();
        _presentationModeService.EnsureOverlayHidden(clearInkCache: true);
        SetStatusMessage("已停止联动监听。");
        AppLogger.Info("手动停止联动监听。");
    }

    private void SetPenColor(Color color)
    {
        var attributes = DrawingAttributes.Clone();
        attributes.Color = color;
        DrawingAttributes = attributes;
        EditingMode = InkCanvasEditingMode.Ink;
    }

    private void ChangeThickness(double delta)
    {
        var attributes = DrawingAttributes.Clone();
        var nextThickness = Math.Clamp(attributes.Width + delta, 1, 20);
        attributes.Width = nextThickness;
        attributes.Height = nextThickness;
        DrawingAttributes = attributes;
    }

    private void RaiseNavigationCanExecuteChanged()
    {
        NextSlideCommand.RaiseCanExecuteChanged();
        PreviousSlideCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void MonitorSlideShow()
    {
        if (!IsLinkModeEnabled)
        {
            return;
        }

        if (!_pptInteropService.IsConnectedToPowerPoint)
        {
            if (_wasConnected)
            {
                AppLogger.Warn("PowerPoint 连接已断开。");
                _presentationModeService.ResetSession();
            }

            _wasConnected = false;
            _wasSlideShowRunning = false;
            TryConnectPowerPointSilently();
            _presentationModeService.EnsureOverlayHidden(clearInkCache: false);
            SetStatusMessage("等待连接 PowerPoint。");
            return;
        }

        _wasConnected = true;
        _pptInteropService.TryRefreshActivePresentation();

        if (_pptInteropService.IsOpened)
        {
            SlideCount = _pptInteropService.GetSlideCount();
            if (CurrentSlideIndex <= 0)
            {
                CurrentSlideIndex = 1;
            }
        }

        var isSlideShowRunning = _pptInteropService.IsSlideShowRunning();
        if (isSlideShowRunning)
        {
            _presentationModeService.EnsureOverlayVisible(_pptInteropService);
            SetStatusMessage("PPT 放映中：批注栏已显示。");
            if (!_wasSlideShowRunning)
            {
                AppLogger.Info("检测到放映启动，显示批注覆盖层。");
            }
        }
        else
        {
            _presentationModeService.EnsureOverlayHidden(clearInkCache: false);
            SetStatusMessage("已连接 PowerPoint，等待放映开始。");
            if (_wasSlideShowRunning)
            {
                AppLogger.Info("检测到放映结束，隐藏批注覆盖层。");
            }
        }

        _wasSlideShowRunning = isSlideShowRunning;
    }

    private void TryConnectPowerPointSilently()
    {
        try
        {
            _pptInteropService.ConnectToRunningPowerPoint();
            _pptInteropService.TryRefreshActivePresentation();
            AppLogger.Info("静默重连 PowerPoint 成功。");
        }
        catch (Exception ex)
        {
            // 后台模式下静默重试，不打断用户的 PowerPoint 操作。
            AppLogger.Warn($"静默重连 PowerPoint 失败：{ex.Message}");
        }
    }

    private void SetStatusMessage(string message)
    {
        if (_lastStatusMessage == message)
        {
            return;
        }

        _lastStatusMessage = message;
        StatusMessage = message;
    }
}
