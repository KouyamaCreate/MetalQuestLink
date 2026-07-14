using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace MaQuestLink.Editor
{
    public readonly struct AdbResult
    {
        public AdbResult(int exitCode, string output)
        {
            ExitCode = exitCode;
            Output = output;
        }

        public int ExitCode { get; }
        public string Output { get; }
        public bool Success => ExitCode == 0;
    }

    public static class AdbBridge
    {
        public const string PackageName = "com.maquestlink.questclient";

        public static AdbResult Reverse()
        {
            var port = MaQuestLinkSettings.instance.port;
            return Run($"reverse tcp:{port} tcp:{port}");
        }

        public static AdbResult Install()
        {
            var apk = OpenXRLayerInstaller.FindDefaultApk();
            if (string.IsNullOrEmpty(apk) || !File.Exists(apk))
            {
                return new AdbResult(2, $"APK not found: {apk}");
            }
            return Run($"install -r {Quote(apk)}", 180000);
        }

        public static AdbResult StartClient()
        {
            var reverse = Reverse();
            if (!reverse.Success)
            {
                return reverse;
            }
            return Run($"shell monkey -p {PackageName} -c android.intent.category.LAUNCHER 1");
        }

        public static AdbResult Run(string arguments, int timeoutMilliseconds = 15000)
        {
            var adb = FindAdb();
            if (string.IsNullOrEmpty(adb))
            {
                return new AdbResult(127, "adb was not found. Set its path in Window > MaQuestLink.");
            }
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = adb,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var process = Process.Start(start))
                {
                    if (process == null)
                    {
                        return new AdbResult(1, "adb process could not be started");
                    }
                    var standardOutput = process.StandardOutput.ReadToEnd();
                    var standardError = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        process.Kill();
                        return new AdbResult(124, "adb command timed out");
                    }
                    return new AdbResult(process.ExitCode, (standardOutput + standardError).Trim());
                }
            }
            catch (Exception exception)
            {
                return new AdbResult(1, exception.Message);
            }
        }

        public static string FindAdb()
        {
            var configured = MaQuestLinkSettings.instance.adbPath;
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
            {
                return configured;
            }
            var environmentPath = Environment.GetEnvironmentVariable("ADB");
            if (!string.IsNullOrEmpty(environmentPath) && File.Exists(environmentPath))
            {
                return environmentPath;
            }
            var androidToolsType = Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
            var sdkProperty = androidToolsType?.GetProperty("sdkRootPath", BindingFlags.Public | BindingFlags.Static);
            var sdkRoot = sdkProperty?.GetValue(null) as string;
            if (!string.IsNullOrEmpty(sdkRoot))
            {
                var bundled = Path.Combine(sdkRoot, "platform-tools", "adb");
                if (File.Exists(bundled))
                {
                    return bundled;
                }
            }
            foreach (var candidate in new[] { "/opt/homebrew/bin/adb", "/usr/local/bin/adb" })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return "adb";
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
