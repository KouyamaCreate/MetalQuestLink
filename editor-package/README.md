# MaQuestLink Editor

This local UPM package registers the MaQuestLink OpenXR layer and provides Quest APK/ADB controls.

1. Add `com.maquestlink.editor` from this directory in Unity Package Manager.
2. Open **Window > MaQuestLink** and click **Install Quest APK** once.
3. Press Play with Meta XR Simulator selected.

The package sets up `adb reverse`, launches the installed Quest client, and shows connection, FPS,
and native copy/encode latency. During repository development, build the native layer and Quest APK
before opening the sample. Packaged binaries are added in Phase 7.
