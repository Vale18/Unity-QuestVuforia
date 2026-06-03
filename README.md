# Quforia - Quest + Vuforia Integration

![Status](https://img.shields.io/badge/Status-Work%20In%20Progress%20|%20Experimental-orange)

An experimental Unity project integrating Vuforia Engine AR tracking with Meta Quest 3's passthrough cameras through a custom native driver implementation.

![Demo](/Media/image-target-demo.gif)

## Overview

Quforia bridges Vuforia Engine 11.4.4 with Meta Quest passthrough camera system by implementing a custom C++ plugin using the Vuforia Driver Framework. This enables AR image tracking directly on Quest's passthrough view without requiring external devices.

**Built With:**

- Unity 6000.0.61f1
- Vuforia Engine 11.4.4 (Driver Framework/External Camera API)
- Meta XR SDK 81.0.0

## Current State

### Working

- **Image Target Tracking**: Image recognition and 6DoF target pose functional, correctly aligned on the passthrough view
- **Camera Integration**: Quest passthrough camera frames fed to Vuforia with correct intrinsics
- **Camera Pose Anchoring**: Target placement anchored to the physical passthrough camera pose (`GetCameraPose`), accounting for the camera-to-eye lens offset and tilt
- **Real-time Processing**: Stable frame delivery and tracking updates


### Known Limitations

- **Device pose fusion disabled**: tracking runs image-only (`DEBUG_DISABLE_POSE_TRACKER = 1` in `vuforia_driver.cpp`). The external positional device tracker (`external_tracker.cpp`) is therefore **currently unused**. Its Unity→Vuforia coordinate transform still introduces a residual offset that must be corrected before pose fusion can be re-enabled (fusion would add pose prediction while the target is occluded).
- No lens distortion is passed to Vuforia (Meta's intrinsics expose none); frame timestamps use the wall clock rather than the hardware capture time.
- Resolution limited to 1280x960 instead of 1280x1280. Vuforia Driver Framework doesn't call start() when feeding a square image
### In Development

- **Model Target Support**: Integration planned but not yet implemented
## Setup

- Clone this project.
- Make sure you have a [Vuforia License Key](https://developer.vuforia.com/home) (works with the free tier).
- Go to Assets/StreamingAssets and create a copy of `VuforiaLicenseKey.text.template` and more the suffix `VuforiaLicenseKey.txt`.
- Paste your license key in this new file. Now you should have a file named `VuforiaLicenseKey.txt` with the key pasted.

## Running Sample Scenes

**Image Target Sample**

- Create an Image Target Database inside Vuforia Dashboard.
- Export it into unity.
- Go to `Assets/Samples/ImageTarget` and open `ImageTargetScene.unity`
- Find the `GameObject` called `ImageTarget`.
- Modify the `Database` param within `ImageTargetBehaviour` component and look for your database.
- Locate your Image Target in the dropdown below.
- Run sample in your headset.
- After the permission prompt restart the app to make the tracking work (only on first install)
**Model Target Sample**

_This is currently work in progress_

## Technical Approach

The project uses a two-layer architecture:

1. **Unity C# Layer**: Captures Quest camera frames via `PassthroughCameraAccess`, derives the camera intrinsics with Meta's crop model, and feeds frames to the native plugin through P/Invoke bridges. A dedicated AR-camera GameObject is driven each frame to the physical camera pose (`GetCameraPose`) by `VuforiaCameraPoseDriver`, so Vuforia places targets relative to the camera that actually produced the image.

2. **Native C++ Plugin** (`libquforia.so`): Implements the Vuforia Driver Framework interface, managing camera lifecycle and frame queuing. (The pose/coordinate-transform path in `external_tracker.cpp` exists but is inactive while pose fusion is disabled.)

### Responsibility split

- **Meta** provides the passthrough camera **image** and the **camera world pose** (headset 6DoF tracking).
- **Vuforia** performs target **recognition** and computes the target's **6DoF pose relative to the camera**.
- The `VuforiaCameraPoseDriver` anchor combines the two into a correct world placement.

### Key Challenges

- **Coordinate System Complexity**: Converting between Unity's left-handed Y-up and Vuforia's right-handed Y-down coordinate systems while handling camera-to-world pose inversions
- **Camera Extrinsics**: Managing lens offset (camera position relative to head center) in the Vuforia Driver Framework
- **Sparse Documentation**: Limited official guidance on implementing Vuforia Driver Framework with offset cameras
- **Cross-Platform Build Chain**: Integrating Unity, Android NDK, Meta SDK, and Vuforia Engine native libraries

## Building

### Native Plugin

```bash
cd QuforiaPlugin
./build.sh
```

### Unity Project

1. Open in Unity 6000.0.61f1
2. File → Build Settings → Android
3. Build and Run to Quest 3

## Requirements

- Meta SDK 81
- Unity 6000.0.61f1
- Vuforia Engine license (free development license available)

## Project Structure

```
Assets/
├── QuestVuforia/          # C# integration scripts
│   ├── QuestVuforiaDriverInit.cs
│   ├── MetaCameraProvider.cs        # frames + intrinsics (crop model)
│   ├── VuforiaCameraPoseDriver.cs   # anchors AR camera to GetCameraPose()
│   └── QuestVuforiaBridge.cs
├── Plugins/Android/       # Native plugin (.so)
└── Samples/               # Example scenes

QuforiaPlugin/             # C++ native plugin source
├── src/
│   ├── vuforia_driver.cpp
│   ├── external_camera.cpp           # feeds frames (active)
│   └── external_tracker.cpp          # device pose (currently unused)
└── build.sh
```

## Future Work

- Correct the Unity→Vuforia pose transform in `external_tracker.cpp` to re-enable device pose fusion (occlusion robustness)
- Implement Model / Object Target tracking
- Pass lens distortion and use hardware capture timestamps
- Add dual-camera (stereo) support for improved robustness
- Performance profiling and optimization

## Contributing

This is an experimental research project. Contributions, suggestions, and issue reports are welcome as we work through the integration challenges.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Vuforia Engine by PTC
- Meta Quest SDK
- Unity Technologies

---

**Note**: This project is experimental and under active development. Use at your own risk.
