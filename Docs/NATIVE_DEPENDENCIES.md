# Native dependencies: evidence and distribution boundary

`Audit/DEPENDENCY_RESTORATION.json` records immutable official download URLs, byte sizes, SHA-256, platform and local installation status. All five native payloads match the original private project's bytes, but were fetched from the public upstream instead of copied from that project.

OpenCV+Unity's pinned package declares Apache-2.0. OpenCvSharp and OpenCV 4.11 each provide Apache-2.0 notices included under `ThirdPartyLicenses/`. This does not, by itself, identify every component statically linked into each native payload.

Embedded build information was read from the exact payloads. The Windows library was also loaded and executed by the actual inference pipeline. Other platforms' build strings and hashes were inspected; their binaries were not executed on those target platforms.

| Component observed | Where observed | Review consequence |
|---|---|---|
| OpenCV 4.11; non-free algorithms NO | All five payloads | Confirms embedded version/configuration text, not a complete SBOM |
| libjpeg-turbo 3.0.3, WebP, libpng 1.6.43 | All payloads | Separate copyright/license notices apply |
| TIFF 4.6.0 and OpenJPEG 2.5.0 | Windows/Android/Linux/macOS where present | Separate notices apply; absence/presence differs by platform |
| Intel IPP / IPP IW 2021.12.0 | Windows and Linux | Must review Intel terms and required notices before redistributing linked binaries |
| Eigen 3.4.0 or bundled Eigen | Linux/macOS where present | Header/library licensing and notices also require preservation |

The pinned upstream build configuration disables FFmpeg, GStreamer and several optional modules; the build strings do not show non-free algorithms enabled. These observations are not treated as permission to omit third-party license checks.

**Distribution decision:** native binaries are installed locally for a working project and reproducible validation, but excluded from this repository's public source snapshot. Bundling them into a repository release, installer or APK is a separate unresolved redistribution review. This source project instead supplies the official, checksum-verified installation route requested by the user.

Sources: [OpenCV+Unity package](https://github.com/neon-izm/OpenCV-plus-Unity/tree/071116d63cde332e5d0d98728d6e9d0747a1a1b9/Unity/OpenCV%2BUnity), [build configuration](https://github.com/neon-izm/OpenCV-plus-Unity/blob/071116d63cde332e5d0d98728d6e9d0747a1a1b9/Utils/opencv_options_unity.cmake), [build script](https://github.com/neon-izm/OpenCV-plus-Unity/blob/071116d63cde332e5d0d98728d6e9d0747a1a1b9/Utils/build-opencv.sh), [OpenCvSharp license](https://github.com/shimat/opencvsharp/blob/4.11.0.20250507/LICENSE), [OpenCV 4.11 license](https://github.com/opencv/opencv/blob/4.11.0/LICENSE).
