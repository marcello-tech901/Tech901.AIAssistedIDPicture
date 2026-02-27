namespace Tech901.IdPhoto.Core.Interfaces;

/// <summary>
/// Detects whether a person is present in front of the camera.
/// Used to gate the Idle → NameCapture transition without requiring Azure.
/// </summary>
public interface IPresenceDetector
{
    bool DetectPresence(byte[] imageData);
}
