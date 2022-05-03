# ReMod.BundleVerifier
A ReMod module implementation of AdvancedSafetys Bundle Verifier  
The purpose of this repository is to respect the GPL license of AdvancedSafety. There is no difference when it comes to features compared to the original!

# Description
This is a ReMod module implementation for knah's AdvancedSafety Bundle Verifier. The original code can be found [here](https://github.com/knah/VRCMods).  

### Bundle verifier notes
Corrupted bundle protection uses a separate Unity process to crash instead of the main one. This comes with several implications:
 * The extra process uses additional system resources, up to a limit
   * Parallel avatar downloads can create multiple processes with corresponding memory usage
   * Avatar data is held in memory while it's being checked, increasing memory consumption even more during avatar download
 * Newly downloaded avatars take longer to load - this is usually a few seconds extra per avatar
   * Avatars loaded from cache are not checked and load as fast as before.
   * Most corrupted avatars never get cached (VRC crashes before writing them into cache), but in some rare situations you might end up with a cached corrupted avatar. In this case you may need to clear your cache, once.
   * The download progress completes twice for checked avatars - once for the download, second time for loading it in Unity itself
 * Some avatars might be rejected because they require too many resources to load. Default limits are 15 CPU-seconds (download time is not included in those) and 2GB RAM. Limits can be changed in settings.
   * After changing limits to be higher, click the "Reset corrupted bundle cache" button to allow re-checking already-rejected avatars.
   * If you have a weaker PC, you may need to increase time limit to be higher.
   * If people around you use horribly unoptimized avatars, you may need to increase the RAM limit.
   * The point above also applies to component limit.
 * Bundle checking is only enabled in public instances by default, but if your friends are somehow not nice, you can enable the mod in all instance types.
   * It ignores "show avatar" as it works on a more-global level

## License
This module here is provided under the terms of [GNU GPLv3 license](LICENSE)
