namespace Tech901.IdPhoto.Core.Interfaces;

public interface IDispatcher
{
    void Invoke(Action action);
}
