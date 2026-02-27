using System.Windows;
using System.Windows.Input;
using Tech901.IdPhoto.ViewModels;

namespace Tech901.IdPhoto.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+Shift+A opens admin
        if (e.Key == Key.A
            && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (DataContext is KioskFlowViewModel vm && vm.ToggleAdminCommand.CanExecute(null))
                vm.ToggleAdminCommand.Execute(null);
        }

        // Escape exits fullscreen
        if (e.Key == Key.Escape)
        {
            if (DataContext is KioskFlowViewModel kioskVm && kioskVm.IsPinDialogVisible)
            {
                kioskVm.CancelPinCommand.Execute(null);
                return;
            }

            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.SingleBorderWindow;
            }
        }
    }

    private void PinBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Dispatcher.InvokeAsync(() =>
            {
                PinBox.Clear();
                Keyboard.Focus(PinBox);
            }, System.Windows.Threading.DispatcherPriority.Render);
        }
    }

    private void PinBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitPin();
        }
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        SubmitPin();
    }

    private void SubmitPin()
    {
        if (DataContext is KioskFlowViewModel vm)
        {
            vm.PinEntry = PinBox.Password;
            vm.SubmitPinCommand.Execute(null);
            PinBox.Password = string.Empty;
        }
    }
}
