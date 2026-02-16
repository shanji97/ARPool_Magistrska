#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;

public static class AdbForwardPostBuild
{
    private const int PcPort = 5005;
    private const int QuestPort = 5005;

    private const string DeviceSerial = null;

    [PostProcessBuild(999)] // Last command, after we write to disk
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuildProject)
    {
        if (target != BuildTarget.Android) return;
        EnsureForwarding(logPrefix: "[PostBuild] ");
        EditorApplication.delayCall += () => EnsureForwarding(logPrefix: "[PostBuild:Delay] ");
    }

    [MenuItem("Tools/Quest/ADB Forward 5005 -> 5005")]
    private static void MenuForward()
    {
        EnsureForwarding(logPrefix: "[Menu] ");
    }

    [MenuItem("Tools/Quest/ADB Remove Forward 5005")]
    private static void MenuRemoveForward()
    {
        RunAdb(new[] { "forward", "--remove", $"tcp:{PcPort}" }, "[Menu] ");
    }

    [MenuItem("Tools/Quest/ADB Show Forwards")]
    private static void MenuListForward()
    {
        RunAdb(new[] { "forward", "--list" }, "[Menu] ");
    }

    private static void EnsureForwarding(string logPrefix = "")
    {
        // adb forward tcp:5005 tcp:5005
        var args = DeviceSerialArg(
            new[] { "forward", $"tcp:{PcPort}", $"tcp:{QuestPort}" }
        );
        RunAdb(args, logPrefix);
        UnityEngine.Debug.Log($"{logPrefix}Port forward ready: PC:{PcPort} -> Quest:{QuestPort}");
    }

    private static string[] DeviceSerialArg(string[] baseArgs)
    {
        if (string.IsNullOrEmpty(DeviceSerial)) return baseArgs;

        // Insert "-s <serial>" after command verb
        // Example: adb -s <serial> forward tcp:5005 tcp:5005
        var withSerial = new string[baseArgs.Length + 2];
        withSerial[0] = "-s";
        withSerial[1] = DeviceSerial;
        Array.Copy(baseArgs, 0, withSerial, 2, baseArgs.Length);
        return withSerial;
    }
    private static void RunAdb(string[] args, string logPrefix)
    {
        try
        {
            var adbPath = ResolveAdbPath();
            if (string.IsNullOrEmpty(adbPath) || !File.Exists(adbPath))
            {
                UnityEngine.Debug.LogError($"{logPrefix}ADB not found. Make sure Android SDK is installed and set in Preferences > External Tools.");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = string.Join(" ", args),
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var p = Process.Start(psi);
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(5000);

            if (!string.IsNullOrEmpty(stdout))
                UnityEngine.Debug.Log($"{logPrefix}adb {string.Join(" ", args)}\n{stdout}");
            if (!string.IsNullOrEmpty(stderr))
                UnityEngine.Debug.LogWarning($"{logPrefix}adb stderr: {stderr}");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"{logPrefix}Failed to run adb: {e}");
        }
    }

    private static string ResolveAdbPath()
    {
        // Prefer Unity's configured Android SDK
#if UNITY_2022_1_OR_NEWER
        // Unity 6/2022+: use AndroidExternalToolsSettings if available
        var sdkRoot = UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath;
#else
        var sdkRoot = EditorPrefs.GetString("AndroidSdkRoot");
#endif
        if (!string.IsNullOrEmpty(sdkRoot))
        {
#if UNITY_EDITOR_WIN
            var candidate = Path.Combine(sdkRoot, "platform-tools", "adb.exe");
#else
            var candidate = Path.Combine(sdkRoot, "platform-tools", "adb");
#endif
            if (File.Exists(candidate)) return candidate;
        }

        // Fallbacks: try PATH
#if UNITY_EDITOR_WIN
        return "adb.exe";
#else
        return "adb";
#endif
    }
}
#endif