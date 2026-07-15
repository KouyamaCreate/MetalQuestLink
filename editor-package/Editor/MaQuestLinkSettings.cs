using UnityEditor;

namespace MaQuestLink.Editor
{
    [FilePath("ProjectSettings/MaQuestLinkSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class MaQuestLinkSettings : ScriptableSingleton<MaQuestLinkSettings>
    {
        public int port = 42424;
        public string adbPath = string.Empty;
        public string deviceSerial = string.Empty;
        public string apkPath = string.Empty;
        public string openXrRuntimeJsonPath = string.Empty;
        public string wifiFallbackHost = string.Empty;
        public int bitrateMbps;
        public int maxPendingFrames = 2;
        public bool autoStartQuestClient = true;
        public bool autoConfigureStandaloneOpenXR = true;
        public bool enablePassthroughPreview = true;
        public bool showTrackedHands = true;

        public void SaveSettings()
        {
            Save(true);
        }
    }
}
