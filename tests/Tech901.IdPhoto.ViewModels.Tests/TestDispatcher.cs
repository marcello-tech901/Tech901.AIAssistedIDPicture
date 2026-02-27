using Tech901.IdPhoto.Core.Interfaces;

namespace Tech901.IdPhoto.ViewModels.Tests;

public sealed class TestDispatcher : IDispatcher
{
    public void Invoke(Action action)
    {
        action();
    }
}
