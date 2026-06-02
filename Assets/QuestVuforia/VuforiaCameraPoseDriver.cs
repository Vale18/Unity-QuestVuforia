using Meta.XR;
using UnityEngine;

/// <summary>
/// Drives this GameObject's transform to the PHYSICAL passthrough camera world pose
/// every frame (position + rotation, including the camera-to-head lens offset/tilt).
/// 
///
/// Runs in LateUpdate (after OVR has updated the head/eye poses) so the camera pose
/// for the current frame is current.
/// </summary>
[DefaultExecutionOrder(100)]
public class VuforiaCameraPoseDriver : MonoBehaviour
{
    [Tooltip("The PassthroughCameraAccess that provides the camera image fed to Vuforia. " +
             "Must be the SAME instance/eye that MetaCameraProvider uses.")]
    [SerializeField] private PassthroughCameraAccess cameraAccess;

    [Header("Debug")]
    [SerializeField] private bool logPose = false;

    private int frameCount;

    private void Reset()
    {
        // Convenience: auto-find a PassthroughCameraAccess in the scene if not assigned.
        if (cameraAccess == null)
        {
            cameraAccess = FindFirstObjectByType<PassthroughCameraAccess>();
        }
    }

    private void LateUpdate()
    {
        if (cameraAccess == null || !cameraAccess.IsPlaying)
        {
            return;
        }

        // GetCameraPose() returns the WORLD-space pose of the physical passthrough
        // camera at the current image timestamp, including the lens offset (translation
        // and tilt) relative to the head. SetPositionAndRotation sets the WORLD pose
        // regardless of this object's parent.
        Pose camPose = cameraAccess.GetCameraPose();
        transform.SetPositionAndRotation(camPose.position, camPose.rotation);

        if (logPose && (++frameCount % 30 == 0))
        {
            Debug.Log($"[Quforia] VuforiaCameraPoseDriver -> camPose pos=" +
                      $"({camPose.position.x:F3},{camPose.position.y:F3},{camPose.position.z:F3})");
        }
    }
}
