using MelonLoader;
using ReMod.Core;
using ReMod.Loader;
using System;
using System.Collections;
using System.IO.Compression;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using VRC.Core;

namespace ReMod.BundleVerifier
{
    public class BundleVerifierMod : ModComponent
    {
        internal static ConfigValue<int> TimeLimit;
        internal static ConfigValue<int> MemoryLimit;
        internal static ConfigValue<int> ComponentLimit;

        internal static ConfigValue<bool> OnlyPublics;
        internal static ConfigValue<bool> EnabledSetting;

        internal static BundleHashCache BadBundleCache;
        internal static BundleHashCache ForceAllowedCache;

        internal static string BundleVerifierPath;

        public BundleVerifierMod()
        {
            TimeLimit = new ConfigValue<int>("BVTimeLimit", 15, "Time limit (seconds)");
            MemoryLimit = new ConfigValue<int>("BVMemLimit", 2048, "Memory limit (megabytes)");
            ComponentLimit = new ConfigValue<int>("BVComponentLimit", 10_000, "Component limit (0=unlimited)");

            EnabledSetting = new ConfigValue<bool>("BVEnabled", true, "Check for corrupted bundles");
            OnlyPublics = new ConfigValue<bool>("BVOnlyPublics", true, "Only check bundles in public worlds");

            BadBundleCache = new BundleHashCache(Path.Combine(MelonUtils.UserDataDirectory, "BadBundleHashes.bin"));
            ForceAllowedCache = new BundleHashCache(null);


            var initSuccess = BundleDownloadMethods.Init();
            if (!initSuccess) return;

            try
            {
                PrepareVerifierDir();
            }
            catch (IOException ex)
            {
                ReLogger.Error("Unable to extract bundle verifier app, the mod will not work");
                ReLogger.Error(ex.ToString());
                return;
            }

            EnabledSetting.OnValueChanged += () => MelonCoroutines.Start(CheckInstanceType());
            OnlyPublics.OnValueChanged += () => MelonCoroutines.Start(CheckInstanceType());
        }

        public override void OnApplicationQuit()
        { 
            BadBundleCache?.Dispose();
        }

        public override void OnLeftRoom()
        {
            BundleDlInterceptor.ShouldIntercept = false;
        }

        public override void OnJoinedRoom()
        {
            MelonCoroutines.Start(CheckInstanceType());
        }

        private static IEnumerator CheckInstanceType()
        {
            while (RoomManager.field_Internal_Static_ApiWorldInstance_0 == null)
                yield return null;

            if (!EnabledSetting.Value)
            {
                BundleDlInterceptor.ShouldIntercept = false;
                ReLogger.Msg($"Bundle intercept disabled in settings");
                yield break;
            }

            var currentInstance = RoomManager.field_Internal_Static_ApiWorldInstance_0;
            BundleDlInterceptor.ShouldIntercept = !OnlyPublics.Value || currentInstance.type == InstanceAccessType.Public;
        }

        private const string VerifierVersion = "1.1-2019.4.31";

        private static void PrepareVerifierDir()
        {
            var baseDir = Path.Combine(MelonUtils.UserDataDirectory, "BundleVerifier");
            Directory.CreateDirectory(baseDir);
            BundleVerifierPath = Path.Combine(baseDir, "BundleVerifier.exe");
            var versionFile = Path.Combine(baseDir, "version.txt");
            if (File.Exists(versionFile))
            {
                var existingVersion = File.ReadAllText(versionFile);
                if (existingVersion == VerifierVersion) return;
            }

            File.Copy(Path.Combine(MelonUtils.GameDirectory, "UnityPlayer.dll"), Path.Combine(baseDir, "UnityPlayer.dll"), true);
            using var zipFile = new ZipArchive(Assembly.GetExecutingAssembly().GetManifestResourceStream("ReMod.BundleVerifier.BundleVerifier.zip")!, ZipArchiveMode.Read, false);
            foreach (var zipArchiveEntry in zipFile.Entries)
            {
                var targetFile = Path.Combine(baseDir, zipArchiveEntry.FullName);
                var looksLikeDir = Path.GetFileName(targetFile).Length == 0;
                Directory.CreateDirectory(looksLikeDir
                    ? targetFile
                    : Path.GetDirectoryName(targetFile)!);
                if (!looksLikeDir)
                    zipArchiveEntry.ExtractToFile(targetFile, true);
            }

            File.WriteAllText(versionFile, VerifierVersion);
        }

        private static readonly Regex ourUrlRegex = new("file_([^/]+)/([^/]+)");
        internal static (string, string) SanitizeUrl(string url)
        {
            var matches = ourUrlRegex.Match(url);
            if (!matches.Success) return ("", url);

            var chars = matches.Groups[1].Value.ToCharArray();
            Array.Reverse(chars);

            return (new string(chars), matches.Groups[2].Value);
        }
    }
}
