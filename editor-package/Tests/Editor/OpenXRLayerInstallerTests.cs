using System;
using System.IO;
using NUnit.Framework;

namespace MaQuestLink.Editor.Tests
{
    public sealed class OpenXRLayerInstallerTests
    {
        private string manifestDirectory;

        [SetUp]
        public void SetUp()
        {
            manifestDirectory = Path.Combine(Path.GetTempPath(), "maquestlink-manifest-" + Guid.NewGuid());
            Environment.SetEnvironmentVariable("MAQUESTLINK_MANIFEST_DIR", manifestDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("MAQUESTLINK_MANIFEST_DIR", null);
            if (Directory.Exists(manifestDirectory))
            {
                Directory.Delete(manifestDirectory, true);
            }
        }

        [Test]
        public void RegisterLayerWritesImplicitManifestForBuiltLibrary()
        {
            var path = OpenXRLayerInstaller.RegisterLayer();
            var json = File.ReadAllText(path);
            StringAssert.Contains(OpenXRLayerInstaller.LayerName, json);
            StringAssert.Contains("libmaquestlink_openxr_layer.so", json);
            StringAssert.Contains("MAQUESTLINK_ENABLE_API_LAYER", json);
        }
    }
}
