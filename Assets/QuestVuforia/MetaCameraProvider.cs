using System;
using System.Collections;
using Meta.XR;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Android;

/// <summary>
/// Provides camera frames and device poses to Vuforia Driver Framework.
/// Uses Meta Quest's PassthroughCameraAccess for frame capture.
/// </summary>
[DefaultExecutionOrder(-50)]
public class MetaCameraProvider : MonoBehaviour
{
    [Header("Camera Access")]
    [SerializeField] private PassthroughCameraAccess cameraAccess;

    [Header("Settings")]
    [SerializeField] private bool autoStart = true;
    [SerializeField] private bool flipImageVertically = true;
    [SerializeField] private bool useCameraRotation = true;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = false;
    [SerializeField] private bool showFrameStats = false;
    [SerializeField] private bool showPoseDebug = false;
    [SerializeField] private float statsInterval = 1.0f;

    private byte[] imageDataRGB;
    private bool isRunning = false;
    private int frameCount = 0;
    private int width, height;
    private float[] cachedIntrinsics;

    // Frame stats
    private float lastStatsTime;
    private int framesProcessed;

    private void Start()
    {
        if (cameraAccess == null)
        {
            Debug.LogError("[Quforia] PassthroughCameraAccess not assigned!");
            return;
        }

        // Request camera permission
        if (!Permission.HasUserAuthorizedPermission("horizonos.permission.HEADSET_CAMERA"))
        {
            Permission.RequestUserPermission("horizonos.permission.HEADSET_CAMERA");
        }

        if (autoStart)
        {
            StartCoroutine(InitializeCamera());
        }
    }

    public IEnumerator InitializeCamera()
    {
        if (isRunning) yield break;

        Log("Initializing camera...");

        if (!cameraAccess.enabled)
        {
            cameraAccess.enabled = true;
            yield return null;
        }

        // Wait for camera to start (10s timeout)
        float elapsed = 0f;
        while (!cameraAccess.IsPlaying && elapsed < 10f)
        {
            yield return null;
            elapsed += Time.deltaTime;
        }

        if (!cameraAccess.IsPlaying)
        {
            Debug.LogError("[Quforia] Camera failed to start!");
            yield break;
        }

        // Get resolution and allocate buffer
        Vector2Int resolution = cameraAccess.CurrentResolution;
        width = resolution.x;
        height = resolution.y;
        imageDataRGB = new byte[width * height * 3];

        var sensorRes = cameraAccess.Intrinsics.SensorResolution;
        Log($"Camera initialized: Current={width}x{height}, Sensor={sensorRes.x}x{sensorRes.y}");
        if (width != sensorRes.x || height != sensorRes.y)
        {
            Log($"CurrentResolution ({width}x{height}) is a crop of SensorResolution ({sensorRes.x}x{sensorRes.y}); " +
                "crop offset is accounted for in SetupCameraIntrinsics().");
        }

        // Setup intrinsics
        SetupCameraIntrinsics();

        isRunning = true;
        lastStatsTime = Time.time;
        StartCoroutine(ProcessFrames());
    }

    private void SetupCameraIntrinsics()
    {
        try
        {
            var intrinsics = cameraAccess.Intrinsics;
            var sensorRes = intrinsics.SensorResolution;

            // Meta's intrinsics (FocalLength, PrincipalPoint) are expressed in the SENSOR frame
            // (e.g. 1280x1280), NOT the current output resolution. The current resolution is a
            // centered CROP of the sensor (and possibly a further scale), exactly as defined by
            // Meta's PassthroughCameraAccess.CalcSensorCropRegion(). For 1280x960 from a 1280x1280
            // sensor this means: full width, top/bottom 160px cropped, NO vertical downscale.
            //
            // Therefore the focal length must NOT be scaled by height/sensorHeight (a crop does not
            // change pixels-per-radian). We mirror Meta's crop model to derive correct intrinsics:
            float sfX = (float)width / sensorRes.x;
            float sfY = (float)height / sensorRes.y;
            float norm = Mathf.Max(sfX, sfY);
            float cropScaleX = sfX / norm;
            float cropScaleY = sfY / norm;
            float cropOffsetX = sensorRes.x * (1f - cropScaleX) * 0.5f;
            float cropOffsetY = sensorRes.y * (1f - cropScaleY) * 0.5f;
            float cropWidth = sensorRes.x * cropScaleX;
            float s = width / cropWidth;   // crop-region -> output scale (== height/cropHeight)

            float fx = intrinsics.FocalLength.x * s;
            float fy = intrinsics.FocalLength.y * s;                       // NOT * (height/sensorHeight)
            float cx = (intrinsics.PrincipalPoint.x - cropOffsetX) * s;
            float cyImg = (intrinsics.PrincipalPoint.y - cropOffsetY) * s; // cy in output (unflipped) frame

            cachedIntrinsics = new float[14];
            cachedIntrinsics[0] = width;
            cachedIntrinsics[1] = height;
            cachedIntrinsics[2] = fx;
            cachedIntrinsics[3] = fy;
            cachedIntrinsics[4] = cx;
            // When the pixel buffer is flipped vertically (required for Vuforia detection),
            // the principal point must be flipped to match: cy_flipped = height - cyImg.
            cachedIntrinsics[5] = flipImageVertically ? (height - cyImg) : cyImg;

            // Single-line logs: Unity's Android logging drops everything after embedded '\n',
            // so multi-line Debug.Log calls show an empty body in logcat.
            Debug.Log($"[Quforia] RAW Meta: sensor={sensorRes.x}x{sensorRes.y} cur={width}x{height} " +
                $"f=({intrinsics.FocalLength.x:F1},{intrinsics.FocalLength.y:F1}) " +
                $"pp=({intrinsics.PrincipalPoint.x:F1},{intrinsics.PrincipalPoint.y:F1})");

            QuestVuforiaBridge.SetCameraIntrinsics(cachedIntrinsics);

            Debug.Log($"[Quforia] Vuforia intr: fx={fx:F1} fy={fy:F1} cx={cx:F1} cy={cachedIntrinsics[5]:F1} " +
                $"cropOff=({cropOffsetX:F0},{cropOffsetY:F0}) s={s:F3} flip={flipImageVertically} " +
                $"(expect fy~=fx, cy~={height * 0.5f:F0})");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Quforia] Failed to get intrinsics: {e.Message}");
        }
    }

    private IEnumerator ProcessFrames()
    {
        while (isRunning)
        {
            if (cameraAccess.IsPlaying)
            {
                try
                {
                    ProcessCurrentFrame();
                    framesProcessed++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Quforia] Frame processing error: {e.Message}");
                }
            }

            // Stats logging
            if (showFrameStats && Time.time - lastStatsTime >= statsInterval)
            {
                float fps = framesProcessed / (Time.time - lastStatsTime);
                Log($"Processing: {fps:F1} FPS | Total: {frameCount}");
                lastStatsTime = Time.time;
                framesProcessed = 0;
            }

            yield return null;
        }
    }

    private void ProcessCurrentFrame()
    {
        // Get camera frame pixels
        NativeArray<Color32> pixels = cameraAccess.GetColors();

        if (!pixels.IsCreated || pixels.Length != width * height)
        {
            return;
        }

        // Convert Color32 to RGB888
        for (int i = 0; i < pixels.Length; i++)
        {
            int rgbIndex = i * 3;
            Color32 pixel = pixels[i];
            imageDataRGB[rgbIndex] = pixel.r;
            imageDataRGB[rgbIndex + 1] = pixel.g;
            imageDataRGB[rgbIndex + 2] = pixel.b;
        }

        // Flip Y-axis if needed
        if (flipImageVertically)
        {
            FlipImageVertically(imageDataRGB, width, height);
        }

        // Get synchronized timestamp and pose
        DateTime currentTime = DateTime.Now;
        long timestampNs = currentTime.Ticks * 100;
        Pose cameraPose = cameraAccess.GetCameraPose();

        // Choose rotation based on setting
        Quaternion rotation = useCameraRotation ? cameraPose.rotation : Quaternion.identity;

        // Debug pose info
        if (showPoseDebug && frameCount % 30 == 0)
        {
            Debug.Log($"[Quforia] Camera Pose: pos=({cameraPose.position.x:F3}, {cameraPose.position.y:F3}, {cameraPose.position.z:F3}), " +
                     $"rot=({cameraPose.rotation.x:F3}, {cameraPose.rotation.y:F3}, {cameraPose.rotation.z:F3}, {cameraPose.rotation.w:F3}), " +
                     $"useCameraRotation={useCameraRotation}");
        }

        // Feed to Vuforia (pose first, then frame with same timestamp)
        QuestVuforiaBridge.FeedDevicePose(cameraPose.position, rotation, timestampNs);
        QuestVuforiaBridge.FeedCameraFrame(imageDataRGB, width, height, null, timestampNs);

        frameCount++;
    }

    private void FlipImageVertically(byte[] imageData, int width, int height)
    {
        int stride = width * 3;
        byte[] rowBuffer = new byte[stride];

        for (int row = 0; row < height / 2; row++)
        {
            int topRow = row * stride;
            int bottomRow = (height - 1 - row) * stride;

            Array.Copy(imageData, topRow, rowBuffer, 0, stride);
            Array.Copy(imageData, bottomRow, imageData, topRow, stride);
            Array.Copy(rowBuffer, 0, imageData, bottomRow, stride);
        }
    }

    public void StopCamera()
    {
        if (!isRunning) return;

        isRunning = false;
        if (cameraAccess != null && cameraAccess.enabled)
        {
            cameraAccess.enabled = false;
        }
        Log("Camera stopped");
    }

    private void OnDestroy() => StopCamera();

    private void OnApplicationPause(bool isPaused)
    {
        if (cameraAccess == null) return;

        if (isPaused && cameraAccess.enabled)
        {
            cameraAccess.enabled = false;
        }
        else if (!isPaused && isRunning && !cameraAccess.enabled)
        {
            cameraAccess.enabled = true;
        }
    }

    private void Log(string message)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[Quforia] {message}");
        }
    }
}
