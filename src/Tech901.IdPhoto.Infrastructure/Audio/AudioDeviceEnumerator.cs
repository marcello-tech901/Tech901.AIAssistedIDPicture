using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Core.Models;

namespace Tech901.IdPhoto.Infrastructure.Audio;

public sealed class AudioDeviceEnumerator : IAudioDeviceEnumerator
{
    private readonly ILogger<AudioDeviceEnumerator> _logger;

    public AudioDeviceEnumerator(ILogger<AudioDeviceEnumerator> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var devices = new List<AudioDeviceInfo>();

            foreach (var device in endpoints)
            {
                devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
                device.Dispose();
            }

            return devices;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate audio input devices");
            return [];
        }
    }

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            var devices = new List<AudioDeviceInfo>();

            foreach (var device in endpoints)
            {
                devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
                device.Dispose();
            }

            return devices;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate audio output devices");
            return [];
        }
    }
}
