using UnityEditor;

namespace MaQuestLink.Editor
{
    [FilePath("ProjectSettings/MaQuestLinkSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class MaQuestLinkSettings : ScriptableSingleton<MaQuestLinkSettings>
    {
        public int port = 42424;
        public string adbPath = string.Empty;
        public string apkPath = string.Empty;
        public bool autoStartQuestClient = true;

        public void SaveSettings()
        {
            Save(true);
        }
    }
}
