using System.Windows;
using Tech901.IdPhoto.Core.Interfaces;

namespace Tech901.IdPhoto.App.Services;

public sealed class WpfDispatcher : IDispatcher
{
    public void Invoke(Action action)
    {
        if (Application.Current?.Dispatcher.CheckAccess() == true)
            action();
        else
            Application.Current?.Dispatcher.Invoke(action);
    }
}
