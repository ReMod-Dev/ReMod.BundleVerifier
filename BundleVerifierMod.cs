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
using ReMod.Core.Managers;
using ReMod.Core.UI.QuickMenu;
using ReMod.Core.VRChat;
using VRC;
using VRC.DataModel;

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

        private ReMenuToggle _bvEnabled;
        private ReMenuToggle _bvOnlyPublics;
        private ReMenuButton _bvTimeLimitButton;
        private ReMenuButton _bvMemoryLimitButton;
        private ReMenuButton _bvComponentLimitButton;
        private ReMenuButton _bvClearCache;
        private ReMenuButton _bvForceAllow;

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
        
        public override void OnUiManagerInit(UiManager uiManager)
        {
            var bundleVerifierMenu = uiManager.MainMenu.GetCategoryPage("Protection").AddCategory("Bundle Verifier");

            _bvEnabled = bundleVerifierMenu.AddToggle("Enabled", "Enable/disable the bundle verifier", EnabledSetting);
            _bvOnlyPublics = bundleVerifierMenu.AddToggle("Only Publics", "Only check bundles in public worlds", OnlyPublics);

            _bvTimeLimitButton = bundleVerifierMenu.AddButton($"Time Limit: {TimeLimit}", "Time limit (seconds)",
                () => VRCUiPopupManager.prop_VRCUiPopupManager_0.ShowInputPopup("Time Limit", TimeLimit, _bvTimeLimitButton),
                ResourceManager.GetSprite("remod.cogwheel"));

            _bvMemoryLimitButton = bundleVerifierMenu.AddButton($"Memory Limit: {MemoryLimit}", "Memory limit (megabytes)",
                () => VRCUiPopupManager.prop_VRCUiPopupManager_0.ShowInputPopup("Memory Limit", MemoryLimit, _bvMemoryLimitButton),
                ResourceManager.GetSprite("remod.cogwheel"));

            _bvComponentLimitButton = bundleVerifierMenu.AddButton($"Component Limit: {ComponentLimit}", "Component limit (0=unlimited)",
                () => VRCUiPopupManager.prop_VRCUiPopupManager_0.ShowInputPopup("Component Limit", ComponentLimit, _bvComponentLimitButton),
                ResourceManager.GetSprite("remod.cogwheel"));

            _bvClearCache = bundleVerifierMenu.AddButton("Reset Cache", "Resets the corrupted bundle cache.",
                BadBundleCache.Clear, ResourceManager.GetSprite("remod.reload"));

            _bvForceAllow = uiManager.TargetMenu.AddButton("BundleVerifier Allow", "Force allow this bundle.", () =>
            {
                var selectedUser = QuickMenuEx.SelectedUserLocal.field_Private_IUser_0;
                if (selectedUser == null) return;
                
                var player = PlayerManager.field_Private_Static_PlayerManager_0.GetPlayer(selectedUser.GetUserID());
                if (player == null)
                    return;

                var apiAvatar = player.GetApiAvatar();
                if (apiAvatar == null)
                    return;
                    
                if (BadBundleCache.Contains(apiAvatar.assetUrl) && !ForceAllowedCache.Contains(apiAvatar.assetUrl))
                {
                    ForceAllowedCache.Add(apiAvatar.assetUrl);
                    VRCPlayer.field_Internal_Static_VRCPlayer_0.ReloadAllAvatars();
                }
            }, ResourceManager.GetSprite("remod.cogwheel"));
        }

        public override void OnSelectUser(IUser user, bool isRemote)
        {
            if (isRemote) return;
            
            var player = PlayerManager.field_Private_Static_PlayerManager_0.GetPlayer(user.GetUserID());
            if (player == null)
                return;

            var apiAvatar = player.GetApiAvatar();
            if (apiAvatar == null)
                return;

            if (ForceAllowedCache.Contains(apiAvatar.assetUrl))
            {
                _bvForceAllow.Interactable = false;
                _bvForceAllow.Text = "BundleVerifier Allowed (Forced)";
            }
            else if (BadBundleCache.Contains(apiAvatar.assetUrl))
            {
                _bvForceAllow.Interactable = true;
                _bvForceAllow.Text = "BundleVerifier Allow";
            }
            else
            {
                _bvForceAllow.Interactable = false;
                _bvForceAllow.Text = "BundleVerifier Allowed (Not Blocked)";
            }
        }

        public override void OnJoinedRoom()
        {
            MelonCoroutines.Start(CheckInstanceType());
        }
        
        public override void OnLeftRoom()
        {
            BundleDlInterceptor.ShouldIntercept = false;
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
