namespace Tech901.IdPhoto.Core.Interfaces;

/// <summary>
/// Provides UI-thread marshaling so ViewModels can update observable properties
/// from background threads without taking a direct dependency on WPF's Dispatcher.
/// </summary>
/// <remarks>
/// In production, <c>WpfDispatcher</c> delegates to <c>Application.Current.Dispatcher</c>.
/// In unit tests, <c>TestDispatcher</c> executes the action inline, removing the
/// need for a running message loop.
/// </remarks>
public interface IDispatcher
{
    /// <summary>
    /// Executes the specified action on the UI thread.
    /// </summary>
    /// <param name="action">The action to invoke on the UI thread.</param>
    void Invoke(Action action);
}
