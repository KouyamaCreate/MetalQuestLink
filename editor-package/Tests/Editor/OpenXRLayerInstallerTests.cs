using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

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
            StringAssert.Contains("XR_EXT_hand_tracking", json);
        }

        [Test]
        public void ReleasePackageContainsNativeLayerManifestAndQuestApk()
        {
            var library = OpenXRLayerInstaller.FindLayerLibrary();
            var apk = OpenXRLayerInstaller.FindDefaultApk();
            Assert.That(library, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(library), Is.True, library);
            Assert.That(apk, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(apk), Is.True, apk);

            var packageRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(library), "..", ".."));
            var bundledManifest = Path.Combine(
                packageRoot, "Native~", "macOS", "XrApiLayer_maquestlink.json");
            Assert.That(File.Exists(bundledManifest), Is.True, bundledManifest);
        }

        [Test]
        public void StatusSchemaHasStableVersionField()
        {
            var status = JsonUtility.FromJson<LayerStatus>(
                "{\"version\":1,\"connected\":false,\"fps\":72.0}");
            Assert.That(status.version, Is.EqualTo(1));
            Assert.IsFalse(status.connected);
            Assert.That(status.fps, Is.EqualTo(72.0f));
        }
    }
}
