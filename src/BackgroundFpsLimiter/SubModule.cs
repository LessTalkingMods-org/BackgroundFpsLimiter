using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;

namespace BackgroundFpsLimiter
{
    public class SubModule : MBSubModuleBase
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        // Our own process id, captured once. The game window is in the foreground when the OS
        // foreground window belongs to this process.
        private static readonly uint _ourPid = (uint)Process.GetCurrentProcess().Id;

        // Win32 foreground polling is the primary detection (works on Windows and, via Wine, under
        // Proton). If user32.dll can't be reached (some Linux/Proton setups), we flip this off and
        // fall back to the engine's platform-agnostic OnConstrainedStateChanged event.
        private static bool _win32Available = true;
        private static bool _engineFallbackSubscribed;
        private static bool _engineConstrained;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            TryLoadMcm();
            InformationManager.DisplayMessage(new InformationMessage("Background FPS Limiter loaded.", Colors.Green));
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);

            // Poll the real OS foreground window every frame. This is independent of the engine's
            // focus-lost callback, which (notably in borderless/windowed-fullscreen) often never
            // fires on alt-tab — the very reason the game keeps rendering full-speed in the
            // background. GetForegroundWindow is a cheap syscall.
            bool inForeground = IsGameInForeground();
            if (!BfpsConfig.Enabled || inForeground)
            {
                return;
            }

            // Throttle by stalling the managed main loop. Sleeping here delays frame presentation,
            // which caps the framerate even if VSync is on — no engine setting involved.
            //
            // IMPORTANT: we deliberately do NOT touch NativeOptions.FrameLimiter. The engine writes
            // its NativeOptions to disk on its own (e.g. on exit), so driving FrameLimiter to the
            // background value here meant that quitting/closing the game while throttled silently
            // overwrote the user's real Frame Limiter setting with the background value, leaving the
            // game permanently capped (and a restart couldn't undo it). Never mutate a persisted
            // engine setting for a transient effect — the Thread.Sleep below is a complete throttle
            // on its own.
            int fps = ClampFps(BfpsConfig.BackgroundFps);
            int budgetMs = 1000 / fps;
            int sleepMs = budgetMs - (int)(dt * 1000f);
            if (sleepMs > 0)
            {
                Thread.Sleep(sleepMs);
            }
        }

        private static bool IsGameInForeground()
        {
            if (_win32Available)
            {
                try
                {
                    IntPtr fg = GetForegroundWindow();
                    if (fg == IntPtr.Zero)
                    {
                        // No foreground window (e.g. fully minimized / locked screen) => background.
                        return false;
                    }
                    GetWindowThreadProcessId(fg, out uint pid);
                    return pid == _ourPid;
                }
                catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
                {
                    // user32.dll unavailable (some Linux/Proton setups). Switch to the engine event
                    // once, then read its state from here on. Don't spam: only logged on the flip.
                    _win32Available = false;
                    EnsureEngineFallback();
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Background FPS Limiter: window API unavailable, using engine focus events.", Colors.Yellow));
                }
            }

            // Fallback path: engine-driven constrained state (platform-agnostic).
            return !_engineConstrained;
        }

        private static void EnsureEngineFallback()
        {
            if (_engineFallbackSubscribed)
            {
                return;
            }
            _engineFallbackSubscribed = true;
            try
            {
                EngineController.OnConstrainedStateChanged += OnEngineConstrainedStateChanged;
            }
            catch
            {
            }
        }

        private static void OnEngineConstrainedStateChanged(bool isConstrained)
        {
            _engineConstrained = isConstrained;
        }

        private static int ClampFps(int fps)
        {
            if (fps < 1)
            {
                return 1;
            }
            return fps;
        }

        // Soft dependency: only load the MCM settings assembly when MCM is actually present, so
        // the core mod runs (on BfpsConfig defaults) without it. The settings type derives from
        // an MCM base type, so it lives in a SEPARATE assembly that the core never references.
        private static void TryLoadMcm()
        {
            try
            {
                if (ModuleHelper.GetModuleInfo("Bannerlord.MBOptionScreen") == null)
                {
                    return;
                }
                string dir = System.IO.Path.GetDirectoryName(typeof(SubModule).Assembly.Location);
                string mcm = System.IO.Path.Combine(dir, "BackgroundFpsLimiter.MCM.dll");
                if (File.Exists(mcm))
                {
                    Assembly.LoadFrom(mcm);
                }
            }
            catch
            {
            }
        }
    }
}
