using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.Core.Interfaces;

public interface IAudioDeviceEnumerator
{
    IReadOnlyList<AudioDeviceInfo> GetInputDevices();
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();
}
