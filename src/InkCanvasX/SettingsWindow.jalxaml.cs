using System.Reflection;
using FluentJalium.Controls;
using FluentJalium.Controls.Themes;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace InkCanvasX;

public partial class SettingsWindow : Window
{
    private static readonly Brush WindowChromeBrush = Brush("WindowBackground", Color.FromRgb(0xF3, 0xF3, 0xF3));
    private static readonly Brush ContentBrush = Brush("NavigationViewContentBackground", Color.FromRgb(0xF3, 0xF3, 0xF3));
    private static readonly Brush PaneBrush = Brush("NavigationViewPaneBackground", Color.FromRgb(0xFA, 0xFA, 0xFA));
    private static readonly Brush CardBrush = Brush("LayerFillColorDefaultBrush", Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly Brush CardBorderBrush = Brush("ControlBorder", Color.FromArgb(0x1F, 0, 0, 0));
    private static readonly Brush PrimaryTextBrush = Brush("TextPrimary", Color.FromRgb(0x24, 0x24, 0x24));
    private static readonly Brush SecondaryTextBrush = Brush("TextSecondary", Color.FromRgb(0x5C, 0x5C, 0x5C));

    private FWNavigationView? _navigationView;
    private FWNavigationViewItem? _appearanceItem;
    private FWNavigationViewItem? _inkItem;
    private FWNavigationViewItem? _interactionItem;
    private FWNavigationViewItem? _aboutItem;

    private FWStackPanel? _appearancePanel;
    private FWStackPanel? _inkPanel;
    private FWStackPanel? _interactionPanel;
    private FWStackPanel? _aboutPanel;

    private FWSlider? _penWidthSlider;
    private FWSlider? _minPointDistanceSlider;
    private FWTextBlock? _penWidthValueText;
    private FWTextBlock? _minPointDistanceValueText;
    private FWComboBox? _smoothingLevelComboBox;
    private FWTextBlock? _aboutVersionText;

    private FWToggleSwitch? _accentContrastSwitch;
    private FWToggleSwitch? _followSystemThemeSwitch;
    private FWToggleSwitch? _keepToolbarOnTopSwitch;
    private FWToggleSwitch? _realtimeSamplingSwitch;
    private FWToggleSwitch? _pressureSwitch;
    private FWToggleSwitch? _tiltSwitch;

    private bool _accentContrast;
    private bool _followSystemTheme;
    private bool _keepToolbarOnTop = true;
    private bool _realtimeSampling = InkRuntimeOptions.Current.EnableRealtimeSampling;
    private bool _pressureMapping = InkRuntimeOptions.Current.EnablePressure;
    private bool _tiltMapping = InkRuntimeOptions.Current.EnableTilt;

    public SettingsWindow()
    {
        InitializeComponent();

        AllowsTransparency = false;
        SystemBackdrop = WindowBackdropType.None;
        Background = WindowChromeBrush;
        Content = BuildSettingsShell();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;

        WirePenSlider();
        WireInkRuntimeControls();
        WireSwitches();
        ApplyVersionText();

        if (_navigationView is not null && _appearanceItem is not null)
        {
            _navigationView.SelectedItem = _appearanceItem;
            NavigateTo(SettingsNavPage.Appearance);
        }
    }

    private UIElement BuildSettingsShell()
    {
        _appearancePanel = CreateAppearancePanel();
        _inkPanel = CreateInkPanel();
        _interactionPanel = CreateInteractionPanel();
        _aboutPanel = CreateAboutPanel();

        _appearanceItem = CreateNavigationItem("外观", Symbol.Palette, SettingsNavPage.Appearance);
        _inkItem = CreateNavigationItem("墨迹", Symbol.InkingTool, SettingsNavPage.Ink);
        _interactionItem = CreateNavigationItem("窗口与交互", Symbol.TouchPointer, SettingsNavPage.Interaction);
        _aboutItem = CreateNavigationItem("关于", Symbol.Info, SettingsNavPage.About);

        _navigationView = new FWNavigationView
        {
            Background = WindowChromeBrush,
            PaneBackground = PaneBrush,
            ContentBackground = ContentBrush,
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsPaneOpen = true,
            OpenPaneLength = 260,
            CompactPaneLength = 48,
            PaneTitle = "Inkcanvas X",
            PaneHeader = CreatePaneHeader(),
            Content = CreateContentHost(),
        };
        _navigationView.SelectionChanged += OnNavigationSelectionChanged;
        _navigationView.MenuItems.Add(_appearanceItem);
        _navigationView.MenuItems.Add(_inkItem);
        _navigationView.MenuItems.Add(_interactionItem);
        _navigationView.FooterMenuItems.Add(_aboutItem);
        _navigationView.UpdateMenuItems();

        return _navigationView;
    }

    private static FWNavigationViewItem CreateNavigationItem(string text, Symbol symbol, SettingsNavPage page)
    {
        return new FWNavigationViewItem
        {
            Content = text,
            Icon = new SymbolIcon
            {
                Symbol = symbol,
                Width = 18,
                Height = 18,
                Foreground = PrimaryTextBrush,
            },
            Tag = page,
        };
    }

    private static UIElement CreatePaneHeader()
    {
        return new FWStackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            Margin = new Thickness(0, 8, 0, 10),
            Children =
            {
                new FWTextBlock
                {
                    Text = "Inkcanvas X",
                    FontSize = 24,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = PrimaryTextBrush,
                },
                new FWTextBlock
                {
                    Text = "设置",
                    FontSize = 12,
                    Foreground = SecondaryTextBrush,
                },
            },
        };
    }

    private UIElement CreateContentHost()
    {
        var host = new FWGrid
        {
            Background = ContentBrush,
        };
        host.Children.Add(new FWScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new FWGrid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 1040,
                Margin = new Thickness(32, 24, 32, 32),
                Children =
                {
                    _appearancePanel!,
                    _inkPanel!,
                    _interactionPanel!,
                    _aboutPanel!,
                },
            },
        });
        return host;
    }

    private FWStackPanel CreateAppearancePanel()
    {
        _accentContrastSwitch = CreateSwitch("高对比度强调色", "用于增强选中态和开关可见性。", _accentContrast);
        _followSystemThemeSwitch = CreateSwitch("跟随系统亮暗模式（预留）", "当前仍由应用固定浅色主题，后续可接入动态切换。", _followSystemTheme);
        _followSystemThemeSwitch.IsEnabled = false;
        _tiltSwitch = CreateSwitch("倾斜映射（实验）", "记录并尝试利用笔倾斜参数，未支持设备将自动忽略。", _tiltMapping);

        return CreateSection(
            "外观",
            "外观偏好与主题策略。",
            CreateSettingsCard(
                _accentContrastSwitch,
                _followSystemThemeSwitch,
                _tiltSwitch));
    }

    private FWStackPanel CreateInkPanel()
    {
        _penWidthSlider = new FWSlider
        {
            Minimum = 1,
            Maximum = 24,
            Value = 4,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
        };
        _penWidthValueText = CreateSecondaryText("4 px");

        var penWidthRow = CreateSettingRow(
            "默认笔划粗细",
            "影响新建墨迹时的初始笔宽。",
            new FWStackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                Children =
                {
                    _penWidthSlider,
                    _penWidthValueText,
                },
            },
            new FWBorder
            {
                Width = 56,
                Height = 56,
                Margin = new Thickness(16, 2, 0, 0),
                CornerRadius = new CornerRadius(10),
                BorderBrush = CardBorderBrush,
                BorderThickness = new Thickness(1),
                Child = new SymbolIcon
                {
                    Symbol = Symbol.InkingTool,
                    Width = 24,
                    Height = 24,
                    Foreground = PrimaryTextBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });

        _realtimeSamplingSwitch = CreateSwitch("实时采样通道（RTS）", "消费 intermediate points，优先降低高速书写断笔。", _realtimeSampling);

        _minPointDistanceSlider = new FWSlider
        {
            Minimum = 0.4,
            Maximum = 2.5,
            Value = InkRuntimeOptions.Current.MinPointDistance,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true,
        };
        _minPointDistanceValueText = CreateSecondaryText("0.75 px");
        var minPointRow = CreateSettingRow(
            "最小点距（MinPointDistance）",
            "更低值保留更多采样点，适合高速书写。",
            new FWStackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 8,
                Children =
                {
                    _minPointDistanceSlider,
                    _minPointDistanceValueText,
                },
            });

        _smoothingLevelComboBox = new FWComboBox
        {
            Width = 132,
            SelectedIndex = 1,
        };
        _smoothingLevelComboBox.Items.Add(new FWComboBoxItem { Content = "低延迟" });
        _smoothingLevelComboBox.Items.Add(new FWComboBoxItem { Content = "平衡" });
        _smoothingLevelComboBox.Items.Add(new FWComboBoxItem { Content = "高平滑" });
        var smoothingRow = CreateSettingRow("平滑等级", "低延迟 / 平衡 / 高平滑（会影响插值强度）。", _smoothingLevelComboBox);

        _pressureSwitch = CreateSwitch("压力映射", "开启后根据笔压控制笔迹宽度/力度（设备支持时）。", _pressureMapping);

        return CreateSection(
            "墨迹",
            "笔迹样式与书写参数。",
            CreateSettingsCard(
                penWidthRow,
                _realtimeSamplingSwitch,
                minPointRow,
                smoothingRow,
                _pressureSwitch));
    }

    private FWStackPanel CreateInteractionPanel()
    {
        _keepToolbarOnTopSwitch = CreateSwitch("打开画布后保持批注栏在最前", "与当前批注栏置顶逻辑一致。", _keepToolbarOnTop);
        return CreateSection(
            "窗口与交互",
            "窗口行为和交互偏好。",
            CreateSettingsCard(_keepToolbarOnTopSwitch));
    }

    private FWStackPanel CreateAboutPanel()
    {
        _aboutVersionText = CreateSecondaryText("版本 1.0.0");
        return CreateSection(
            "关于",
            "应用信息与版本。",
            CreateSettingsCard(new FWStackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 6,
                Margin = new Thickness(20, 16, 20, 16),
                Children =
                {
                    new FWTextBlock
                    {
                        Text = "Inkcanvas X",
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = PrimaryTextBrush,
                    },
                    _aboutVersionText,
                    CreateSecondaryText("基于 FluentJalium、Jalium.UI 与 InkCanvas。"),
                },
            }));
    }

    private static FWStackPanel CreateSection(string title, string description, UIElement card)
    {
        return new FWStackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 0,
            Visibility = Visibility.Collapsed,
            Children =
            {
                new FWTextBlock
                {
                    Text = title,
                    FontSize = 30,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = PrimaryTextBrush,
                },
                new FWTextBlock
                {
                    Text = description,
                    Margin = new Thickness(0, 8, 0, 0),
                    FontSize = 13,
                    Foreground = SecondaryTextBrush,
                },
                card,
            },
        };
    }

    private static FWBorder CreateSettingsCard(params UIElement[] rows)
    {
        var panel = new FWStackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 0,
        };

        for (var i = 0; i < rows.Length; i++)
        {
            panel.Children.Add(rows[i]);
            if (i < rows.Length - 1)
            {
                panel.Children.Add(new FWBorder
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromArgb(0x12, 0, 0, 0)),
                });
            }
        }

        return new FWBorder
        {
            Margin = new Thickness(0, 18, 0, 0),
            CornerRadius = new CornerRadius(12),
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            Background = CardBrush,
            Child = panel,
        };
    }

    private static FWGrid CreateSettingRow(string title, string description, UIElement content, UIElement? trailing = null)
    {
        var grid = new FWGrid
        {
            Margin = new Thickness(20, 16, 20, 16),
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new FWStackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            Children =
            {
                new FWTextBlock
                {
                    Text = title,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = PrimaryTextBrush,
                },
                new FWTextBlock
                {
                    Text = description,
                    FontSize = 12,
                    Foreground = SecondaryTextBrush,
                    TextWrapping = TextWrapping.Wrap,
                },
                content,
            },
        };
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        if (trailing is not null)
        {
            Grid.SetColumn(trailing, 1);
            grid.Children.Add(trailing);
        }

        return grid;
    }

    private static FWToggleSwitch CreateSwitch(string title, string description, bool isOn)
    {
        return new FWToggleSwitch
        {
            Header = title,
            Description = description,
            IsOn = isOn,
            Margin = new Thickness(20, 10, 20, 10),
        };
    }

    private static FWTextBlock CreateSecondaryText(string text)
    {
        return new FWTextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = SecondaryTextBrush,
        };
    }

    private void WirePenSlider()
    {
        if (_penWidthSlider is null)
            return;

        _penWidthSlider.ValueChanged += (_, _) => UpdatePenWidthLabel();
        UpdatePenWidthLabel();
    }

    private void WireInkRuntimeControls()
    {
        var runtime = InkRuntimeOptions.Current;
        _realtimeSampling = runtime.EnableRealtimeSampling;
        _pressureMapping = runtime.EnablePressure;
        _tiltMapping = runtime.EnableTilt;

        if (_realtimeSamplingSwitch is not null)
            _realtimeSamplingSwitch.IsOn = _realtimeSampling;
        if (_pressureSwitch is not null)
            _pressureSwitch.IsOn = _pressureMapping;
        if (_tiltSwitch is not null)
            _tiltSwitch.IsOn = _tiltMapping;

        if (_minPointDistanceSlider is not null)
        {
            _minPointDistanceSlider.Value = runtime.MinPointDistance;
            _minPointDistanceSlider.ValueChanged += (_, _) =>
            {
                InkRuntimeOptions.SetMinPointDistance(_minPointDistanceSlider.Value);
                UpdateMinPointDistanceLabel();
            };
            UpdateMinPointDistanceLabel();
        }

        if (_smoothingLevelComboBox is not null)
        {
            _smoothingLevelComboBox.SelectedIndex = runtime.SmoothingLevel switch
            {
                InkSmoothingLevel.Low => 0,
                InkSmoothingLevel.High => 2,
                _ => 1,
            };
            _smoothingLevelComboBox.SelectionChanged += (_, _) =>
            {
                var level = _smoothingLevelComboBox.SelectedIndex switch
                {
                    0 => InkSmoothingLevel.Low,
                    2 => InkSmoothingLevel.High,
                    _ => InkSmoothingLevel.Balanced,
                };
                InkRuntimeOptions.SetSmoothingLevel(level);
            };
        }
    }

    private void WireSwitches()
    {
        if (_accentContrastSwitch is not null)
        {
            _accentContrastSwitch.Toggled += (_, _) => _accentContrast = _accentContrastSwitch.IsOn;
        }

        if (_followSystemThemeSwitch is not null)
        {
            _followSystemThemeSwitch.Toggled += (_, _) => _followSystemTheme = _followSystemThemeSwitch.IsOn;
        }

        if (_keepToolbarOnTopSwitch is not null)
        {
            _keepToolbarOnTopSwitch.Toggled += (_, _) => _keepToolbarOnTop = _keepToolbarOnTopSwitch.IsOn;
        }

        if (_realtimeSamplingSwitch is not null)
        {
            _realtimeSamplingSwitch.Toggled += (_, _) =>
            {
                _realtimeSampling = _realtimeSamplingSwitch.IsOn;
                InkRuntimeOptions.SetRealtimeSampling(_realtimeSampling);
            };
        }

        if (_pressureSwitch is not null)
        {
            _pressureSwitch.Toggled += (_, _) =>
            {
                _pressureMapping = _pressureSwitch.IsOn;
                InkRuntimeOptions.SetEnablePressure(_pressureMapping);
            };
        }

        if (_tiltSwitch is not null)
        {
            _tiltSwitch.Toggled += (_, _) =>
            {
                _tiltMapping = _tiltSwitch.IsOn;
                InkRuntimeOptions.SetEnableTilt(_tiltMapping);
            };
        }
    }

    private void UpdatePenWidthLabel()
    {
        if (_penWidthSlider is null || _penWidthValueText is null)
            return;

        var v = (int)Math.Round(_penWidthSlider.Value);
        _penWidthValueText.Text = $"{v} px";
    }

    private void UpdateMinPointDistanceLabel()
    {
        if (_minPointDistanceSlider is null || _minPointDistanceValueText is null)
            return;

        _minPointDistanceValueText.Text = $"{_minPointDistanceSlider.Value:F2} px";
    }

    private void ApplyVersionText()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        if (v is not null && _aboutVersionText is not null)
            _aboutVersionText.Text = $"版本 {v.Major}.{v.Minor}.{v.Build}";
    }

    private void OnNavigationSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        _ = sender;
        if (e.SelectedItem?.Tag is SettingsNavPage page)
            NavigateTo(page);
    }

    private void NavigateTo(SettingsNavPage page)
    {
        if (_appearancePanel is not null)
            _appearancePanel.Visibility = page == SettingsNavPage.Appearance ? Visibility.Visible : Visibility.Collapsed;
        if (_inkPanel is not null)
            _inkPanel.Visibility = page == SettingsNavPage.Ink ? Visibility.Visible : Visibility.Collapsed;
        if (_interactionPanel is not null)
            _interactionPanel.Visibility = page == SettingsNavPage.Interaction ? Visibility.Visible : Visibility.Collapsed;
        if (_aboutPanel is not null)
            _aboutPanel.Visibility = page == SettingsNavPage.About ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Brush Brush(string resourceKey, Color fallback)
    {
        if (Application.Current?.Resources.TryGetValue(resourceKey, out var value) == true && value is Brush brush)
            return brush;

        return new SolidColorBrush(fallback);
    }
}
