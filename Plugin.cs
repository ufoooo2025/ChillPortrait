using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using Cinemachine;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace ChillPortrait
{
    [DefaultExecutionOrder(20000)]
    [BepInPlugin("local.chillportrait", "Chill Portrait", "1.1.0")]
    [BepInProcess("Chill With You.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private readonly ConcurrentQueue<Command> commands = new ConcurrentQueue<Command>();
        private TrayControls tray;
        private BackgroundWindow background;
        private ConfigEntry<string> monitorName;
        private ConfigEntry<bool> startOnRight;
        private ConfigEntry<bool> portraitEnabled;
        private bool placed, inspected;
        private bool originalRunInBackground;
        private float inspectAfter;
        private ConfigEntry<bool> diagnostics;
        private float nextPoll;
        private PortraitRoom portrait;

        private void Awake()
        {
            monitorName = Config.Bind("Window", "Monitor", "", "Windows display name. Empty selects the rightmost portrait display to the right of the primary screen.");
            startOnRight = Config.Bind("Window", "StartOnRight", true, "Open on the right display in interactive mode.");
            var hotkey = Config.Bind("Window", "Shortcut", "Ctrl+Alt+F10", "Global shortcut. Use Ctrl, Alt, Shift or Win plus a key name.");
            portraitEnabled = Config.Bind("Portrait", "Enabled", true, "Enable portrait framing and HUD layout for everyday room views.");
            diagnostics = Config.Bind("Debug", "Inspect", false, "Expose a tray command to log scene structure for troubleshooting.");
            originalRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            background = new BackgroundWindow(FindTarget, message => Logger.LogInfo(message));
            var zoom = Config.Bind("Portrait", "ZoomOffset", 0f,
                new ConfigDescription("Portrait field-of-view offset. Ctrl + mouse wheel adjusts this in the room.",
                    new AcceptableValueRange<float>(-16f, 20f)));
            portrait = new PortraitRoom(zoom);
            tray = new TrayControls(commands, hotkey.Value, diagnostics.Value, message => Logger.LogInfo(message));

            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private Native.MonitorInfo? FindTarget()
        {
            var monitors = Native.Monitors();
            var primary = monitors.FirstOrDefault(monitor => monitor.Primary);
            var target = monitors
                .Where(monitor => string.IsNullOrEmpty(monitorName.Value)
                    ? !monitor.Primary && monitor.Bounds.Left >= primary.Bounds.Right && monitor.Bounds.Height > monitor.Bounds.Width
                    : string.Equals(monitor.Device, monitorName.Value, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(monitor => monitor.Bounds.Left)
                .FirstOrDefault();
            return target.Device == null ? (Native.MonitorInfo?)null : target;
        }

        private void Update()
        {
            try
            {
                while (commands.TryDequeue(out Command command))
                {
                    switch (command)
                    {
                        case Command.Toggle:
                            if (background.Active) background.Restore(true);
                            else if (tray.Ready) background.Enter();
                            break;
                        case Command.Restore:
                            if (background.Active) background.Restore(true);
                            else background.Focus();
                            break;
                        case Command.Portrait:
                            if (background.Active) background.Restore(false);
                            background.PlaceOnTarget(false);
                            break;
                        case Command.Quit:
                            if (background.Active) background.Restore(false);
                            RequestGameExit();
                            break;
                        case Command.Inspect:
                            InspectScene();
                            break;
                    }
                }

                if (Time.realtimeSinceStartup < nextPoll)
                    return;
                nextPoll = Time.realtimeSinceStartup + 0.6f;
                if (!placed && startOnRight.Value && tray.Ready && !background.Active)
                    placed = background.PlaceOnTarget(true);
                background.Maintain();
                if (diagnostics.Value && !inspected && inspectAfter > 0 && Time.realtimeSinceStartup >= inspectAfter)
                {
                    inspected = true;
                    InspectScene();
                }
            }
            catch (Exception error)
            {
                Logger.LogError(error);
                background.Restore(false);
            }
        }

        private void OnSceneChanged(Scene previous, Scene next)
        {
            portrait.Reset();
            inspected = false;
            inspectAfter = Time.realtimeSinceStartup + 6f;
        }

        private void LateUpdate()
        {
            portrait.Tick(portraitEnabled.Value, !background.Active && Application.isFocused);
        }

        private void RequestGameExit()
        {
            background.Focus();
            var type = Type.GetType("Bulbul.ExitUI, Assembly-CSharp", false);
            var openDialog = type?.GetMethod("OnClickButtonExitGameIcon",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            var exitUi = type == null ? null : Resources.FindObjectsOfTypeAll(type)
                .OfType<MonoBehaviour>()
                .FirstOrDefault(component => component.gameObject.scene.IsValid() && component.gameObject.scene.isLoaded);
            if (exitUi && openDialog != null)
                openDialog.Invoke(exitUi, null);
            else
                Logger.LogWarning("Exit confirmation is unavailable in this scene. Game window restored.");
        }

        private void InspectScene()
        {
            Logger.LogInfo($"Scene {SceneManager.GetActiveScene().name}, resolution {Screen.width}x{Screen.height}");
            Logger.LogInfo(portrait.Describe());
            foreach (var brain in FindObjectsOfType<CinemachineBrain>())
                Logger.LogInfo($"Brain {brain.name}, active {brain.ActiveVirtualCamera?.Name}, position {brain.transform.position}, fov {brain.OutputCamera.fieldOfView}");
            foreach (var animator in FindObjectsOfType<Animator>())
            {
                if (animator.isHuman)
                    Logger.LogInfo($"Humanoid {PortraitRoom.Path(animator.transform)}, head {animator.GetBoneTransform(HumanBodyBones.Head)?.position}");
            }
            foreach (var scaler in FindObjectsOfType<CanvasScaler>())
                Logger.LogInfo($"Canvas {PortraitRoom.Path(scaler.transform)} ref {scaler.referenceResolution} match {scaler.matchWidthOrHeight}");
            foreach (var rect in FindObjectsOfType<RectTransform>())
            {
                if (!rect.GetComponent<Button>() && !rect.GetComponent<Slider>())
                    continue;
                Logger.LogInfo($"HUD {PortraitRoom.Path(rect)} size {rect.rect.size} anchor {rect.anchorMin}/{rect.anchorMax} offset {rect.anchoredPosition}");
            }
        }

        private void Start() => Logger.LogInfo("Plugin runtime started.");

        private void OnDestroy()
        {
            Logger.LogInfo("Plugin runtime shutting down.");
            SceneManager.activeSceneChanged -= OnSceneChanged;
            portrait?.Reset();
            background?.Restore(false);
            tray?.Dispose();
            Application.runInBackground = originalRunInBackground;
        }
    }
}
