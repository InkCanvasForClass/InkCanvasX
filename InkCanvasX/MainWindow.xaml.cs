using System.Windows;
using System.Windows.Input;
using SkiaAnnotate.Services;
using SkiaAnnotate.ViewModels;

namespace SkiaAnnotate;

public partial class MainWindow : Window
{
    private readonly AnnotateViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new AnnotateViewModel(new PptInteropService(), new PresentationModeService());
        DataContext = _viewModel;
        KeyDown += OnMainWindowKeyDown;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void OnMainWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Right or Key.PageDown)
        {
            if (_viewModel.NextSlideCommand.CanExecute(null))
            {
                _viewModel.NextSlideCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        if (e.Key is Key.Left or Key.PageUp)
        {
            if (_viewModel.PreviousSlideCommand.CanExecute(null))
            {
                _viewModel.PreviousSlideCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        if (e.Key is Key.F5)
        {
            _viewModel.TogglePresentationModeCommand.Execute(null);
            e.Handled = true;
        }
    }
}
