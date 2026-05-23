using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace LanStartWrite.Inkcanvas;

public partial class PenSecondaryMenuWindow : Window
{
    private static readonly Brush TransparentBrush =
        new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

    private static readonly Color[] PaletteColors =
    [
        Color.FromRgb(0x20, 0x20, 0x20), // 1
        Color.FromRgb(0xD1, 0x34, 0x38), // 2
        Color.FromRgb(0xF2, 0x6B, 0x1F), // 3
        Color.FromRgb(0xF2, 0xC8, 0x11), // 4
        Color.FromRgb(0x10, 0x7C, 0x10), // 5
        Color.FromRgb(0x00, 0xB7, 0xC3), // 6
        Color.FromRgb(0x00, 0x78, 0xD4), // 7
        Color.FromRgb(0x87, 0x64, 0xB8), // 8
        Color.FromRgb(0x73, 0x73, 0x73), // 9
    ];

    private static readonly Color SelectionRingColor = Color.FromRgb(0x00, 0x78, 0xD4);

    private readonly Border[] _colorRings;
    private bool _penKindSync;
    private int _selectedPaletteIndex;

    private Slider PenThickness => (Slider)PenThicknessSlider!;
    private TextBlock PenThicknessText => (TextBlock)PenThicknessValueText!;

    public Color SelectedColor { get; private set; }
    public double SelectedThickness { get; private set; } = 4;
    public PenKind SelectedKind { get; private set; } = PenKind.Pen;

    public event Action<Color>? PenColorChanged;
    public event Action<double>? PenThicknessChanged;
    public event Action<PenKind>? PenKindChanged;

    public PenSecondaryMenuWindow()
    {
        SelectedColor = PaletteColors[0];

        AllowsTransparency = true;
        ShowActivated = false;
        InitializeComponent();
        SystemBackdrop = WindowBackdropType.None;
        Background = TransparentBrush;
        Opacity = 1;

        _colorRings =
        [
            (Border)PenColorRing1!,
            (Border)PenColorRing2!,
            (Border)PenColorRing3!,
            (Border)PenColorRing4!,
            (Border)PenColorRing5!,
            (Border)PenColorRing6!,
            (Border)PenColorRing7!,
            (Border)PenColorRing8!,
            (Border)PenColorRing9!,
        ];

        _selectedPaletteIndex = 0;
        UpdateColorSelectionVisuals();
        PenThickness.Value = SelectedThickness;
        UpdateThicknessText();

        WireColorRings();
        WirePenKindRadios();
    }

    public void SetCurrentState(Color color, double thickness, PenKind kind)
    {
        SelectedColor = color;
        SelectedThickness = Math.Max(1, thickness);
        SelectedKind = kind;
        PenThickness.Value = SelectedThickness;
        UpdateThicknessText();

        _selectedPaletteIndex = FindBestPaletteIndex(color);
        UpdateColorSelectionVisuals();

        _penKindSync = true;
        try
        {
            PenKindPenRadio!.IsChecked = kind == PenKind.Pen;
            PenKindHighlighterRadio!.IsChecked = kind == PenKind.Highlighter;
            PenKindLaserRadio!.IsChecked = kind == PenKind.Laser;
        }
        finally
        {
            _penKindSync = false;
        }
    }

    private static int FindBestPaletteIndex(Color c)
    {
        var best = 0;
        var bestDist = double.MaxValue;
        for (var i = 0; i < PaletteColors.Length; i++)
        {
            var p = PaletteColors[i];
            var dr = c.R - p.R;
            var dg = c.G - p.G;
            var db = c.B - p.B;
            var d = (dr * dr) + (dg * dg) + (db * db);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        return best;
    }

    private void WireColorRings()
    {
        for (var i = 0; i < _colorRings.Length; i++)
        {
            var ring = _colorRings[i];
            var index = i;
            ring.PreviewPointerDown += (_, e) =>
            {
                e.Handled = true;
                SelectPaletteIndex(index);
            };
        }
    }

    private void WirePenKindRadios()
    {
        PenKindPenRadio!.Checked += OnAnyPenKindRadioChecked;
        PenKindHighlighterRadio!.Checked += OnAnyPenKindRadioChecked;
        PenKindLaserRadio!.Checked += OnAnyPenKindRadioChecked;
    }

    private void OnAnyPenKindRadioChecked(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (_penKindSync)
            return;

        if (sender is not RadioButton rb || rb.IsChecked != true)
            return;

        var kind = ReferenceEquals(rb, PenKindHighlighterRadio)
            ? PenKind.Highlighter
            : ReferenceEquals(rb, PenKindLaserRadio)
                ? PenKind.Laser
                : PenKind.Pen;

        if (SelectedKind == kind)
            return;

        SelectedKind = kind;
        PenKindChanged?.Invoke(kind);
    }

    private void SelectPaletteIndex(int index)
    {
        if (index < 0 || index >= PaletteColors.Length)
            return;

        _selectedPaletteIndex = index;
        var c = PaletteColors[index];
        UpdateColorSelectionVisuals();
        SelectedColor = c;
        PenColorChanged?.Invoke(c);
    }

    private void UpdateColorSelectionVisuals()
    {
        for (var i = 0; i < _colorRings.Length; i++)
        {
            var ring = _colorRings[i];
            if (i == _selectedPaletteIndex)
            {
                ring.BorderBrush = new SolidColorBrush(SelectionRingColor);
                ring.BorderThickness = new Thickness(2);
            }
            else
            {
                ring.BorderBrush = new SolidColorBrush(Colors.Transparent);
                ring.BorderThickness = new Thickness(2);
            }
        }
    }

    private void UpdateThicknessText()
    {
        PenThicknessText.Text = $"粗细: {(int)Math.Round(SelectedThickness)} px";
    }

    private void PenThicknessSlider_OnValueChanged(object sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        SelectedThickness = Math.Round(PenThickness.Value);
        UpdateThicknessText();
        PenThicknessChanged?.Invoke(SelectedThickness);
    }
}
