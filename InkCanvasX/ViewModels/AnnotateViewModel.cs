using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Jalium.UI.Threading;
using SkiaAnnotate.Commands;
using SkiaAnnotate.Services;

namespace SkiaAnnotate.ViewModels;

public sealed class AnnotateViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly PptInteropService _pptInteropService;
    private readonly PresentationModeService _presentationModeService;
    private readonly DispatcherTimer _linkMonitorTimer;
    private bool _isLinkModeEnabled;
    private bool _wasSlideShowRunning;
    private bool _wasConnected;
    private string? _lastStatusMessage;
    private int _slideCount;
    private int _currentSlideIndex;
    private string _statusMessage = "请先启动 PowerPoint 并打开演示文稿。";

    public AnnotateViewModel(PptInteropService pptInteropService, PresentationModeService presentationModeService)
    {
        _pptInteropService = pptInteropService;
        _presentationModeService = presentationModeService;

        _linkMonitorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _linkMonitorTimer.Tick += (_, _) => MonitorSlideShow();

        ConnectPowerPointCommand = new RelayCommand(ConnectPowerPoint);
        NextSlideCommand = new RelayCommand(NextSlide, () => CanNavigateSlide(1));
        PreviousSlideCommand = new RelayCommand(PreviousSlide, () => CanNavigateSlide(-1));
        TogglePresentationModeCommand = new RelayCommand(TogglePresentationMode);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand ConnectPowerPointCommand { get; }
    public RelayCommand NextSlideCommand { get; }
    public RelayCommand PreviousSlideCommand { get; }
    public RelayCommand TogglePresentationModeCommand { get; }

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

    public void Dispose()
    {
        AppLogger.Info("释放 ViewModel 资源。");
        _linkMonitorTimer.Stop();
        _pptInteropService.Dispose();
    }

    private void ConnectPowerPoint()
    {
        try
        {
            _pptInteropService.ConnectToRunningPowerPoint();
            RefreshPresentationInfo();
            StatusMessage = "已连接 PowerPoint。放映开始时会自动显示批注浮动栏。";
            AppLogger.Info($"已连接 PowerPoint。SlideCount={SlideCount}");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            AppLogger.Error("连接 PowerPoint 失败。", ex);
        }
    }

    private void TogglePresentationMode()
    {
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

    private void NextSlide()
    {
        _pptInteropService.NextSlideInShow();
    }

    private void PreviousSlide()
    {
        _pptInteropService.PreviousSlideInShow();
    }

    private bool CanNavigateSlide(int delta)
    {
        if (SlideCount <= 0)
        {
            return false;
        }

        var target = CurrentSlideIndex + delta;
        return target >= 1 && target <= SlideCount;
    }

    private void RefreshPresentationInfo()
    {
        _pptInteropService.TryRefreshActivePresentation();
        SlideCount = _pptInteropService.IsOpened ? _pptInteropService.GetSlideCount() : 0;
        CurrentSlideIndex = _pptInteropService.GetCurrentSlideNumberInShow();
        if (CurrentSlideIndex <= 0 && SlideCount > 0)
        {
            CurrentSlideIndex = 1;
        }

        RaiseNavigationCanExecuteChanged();
    }

    private void RaiseNavigationCanExecuteChanged()
    {
        NextSlideCommand.RaiseCanExecuteChanged();
        PreviousSlideCommand.RaiseCanExecuteChanged();
    }

    private void MonitorSlideShow()
    {
        if (!IsLinkModeEnabled)
        {
            return;
        }

        try
        {
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
            RefreshPresentationInfo();

            var isSlideShowRunning = _pptInteropService.IsSlideShowRunning();
            if (isSlideShowRunning)
            {
                _presentationModeService.EnsureOverlayVisible(_pptInteropService);
                CurrentSlideIndex = _pptInteropService.GetCurrentSlideNumberInShow();
                SetStatusMessage("PPT 放映中：批注浮动栏已显示。");
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
        catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80010001))
        {
            AppLogger.Warn($"PowerPoint 忙，跳过一次轮询：{ex.Message}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("联动轮询异常，已降级跳过本次。", ex);
        }
    }

    private void TryConnectPowerPointSilently()
    {
        try
        {
            _pptInteropService.ConnectToRunningPowerPoint();
            RefreshPresentationInfo();
            AppLogger.Info("静默重连 PowerPoint 成功。");
        }
        catch (Exception ex)
        {
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
