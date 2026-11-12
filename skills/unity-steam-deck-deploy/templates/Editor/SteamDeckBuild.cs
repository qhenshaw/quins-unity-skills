using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyGame.Editor
{
    /// <summary>
    /// Steam Deck build: a Windows x64 player run through Proton. Built with Direct3D 11 first (Proton translates D3D11
    /// via DXVK, its most mature path) and fullscreen-window mode so it takes the Deck's native 1280x800. The project's
    /// own graphics API and fullscreen settings are restored afterwards.
    /// CLI: unity command script eval with  return MyGame.Editor.SteamDeckBuild.Build();
    /// </summary>
    public static class SteamDeckBuild
    {
        public const string Folder = "Builds/SteamDeck";

        [MenuItem("Build/Steam Deck (Proton)")]
        private static void Menu() => Debug.Log(Build());

        /// <summary>Builds into <see cref="Folder"/>. Returns "Succeeded Builds/SteamDeck size=..MB time=.. errors=0 ...".</summary>
        public static string Build()
        {
            // A Play session stopped while paused can leave timeScale at 0; the settings save below would write it into
            // ProjectSettings and the build would start frozen.
            Time.timeScale = 1f;
            var target = BuildTarget.StandaloneWindows64;
            bool savedAuto = PlayerSettings.GetUseDefaultGraphicsAPIs(target);
            var savedApis = PlayerSettings.GetGraphicsAPIs(target);
            var savedMode = PlayerSettings.fullScreenMode;
            string exe = $"{Folder}/{PlayerSettings.productName}.exe";
            try
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
                PlayerSettings.SetGraphicsAPIs(target, new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12 });
                PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = exe,
                    target = target,
                    options = BuildOptions.None,
                });
                var s = report.summary;
                var errors = string.Join(" | ", report.steps.SelectMany(st => st.messages)
                    .Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content).Take(5));
                if (s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                    File.WriteAllText($"{Folder}/STEAM_DECK_README.txt", Readme(PlayerSettings.productName));
                return $"{s.result} {Folder} size={s.totalSize / 1048576}MB time={s.totalTime:mm\\:ss} errors={s.totalErrors} warnings={s.totalWarnings} {errors}";
            }
            finally
            {
                PlayerSettings.SetGraphicsAPIs(target, savedApis);
                PlayerSettings.SetUseDefaultGraphicsAPIs(target, savedAuto);
                PlayerSettings.fullScreenMode = savedMode;
                AssetDatabase.SaveAssets(); // or ProjectSettings.asset keeps the mid-build state on disk
            }
        }

        private static string Readme(string product) =>
$@"{product} - Steam Deck (Proton) build
This is a Windows build that runs through Proton.

Manual install (without the dev kit)
1. Copy this folder to the Deck (USB stick, SD card or network share).
2. Desktop Mode > Steam > Games > Add a Non-Steam Game > Browse... > {product}.exe (file filter: All Files).
3. Properties > Compatibility > Force a specific Steam Play tool > Proton Experimental.
4. Launch from Gaming Mode (Non-Steam tab).

Black screen or crash at start: Properties > Launch Options:  -force-d3d12  (or  -force-vulkan ).
Player.log: ~/.local/share/Steam/steamapps/compatdata/<appid>/pfx/drive_c/users/steamuser/AppData/LocalLow/<company>/{product}/Player.log
";
    }
}
