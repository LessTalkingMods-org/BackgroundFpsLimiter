using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using TaleWorlds.Engine;
using TaleWorlds.Engine.Options;
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

        // True while we are actively throttling the background window.
        private static bool _throttled;

        // Debounce: a momentary activation blip must NOT latch the throttle. A Bluetooth/audio
        // device switch, a toast notification, a UAC prompt, etc. can briefly clear the OS
        // foreground window (GetForegroundWindow returns NULL) or hand it to a transient system
        // window. The background condition must hold continuously for this long before we throttle,
        // so those blips no longer pin the game at the background framerate until restart.
        private const float BackgroundDebounceSeconds = 0.75f;
        private static float _backgroundElapsed;

        // Lightweight diagnostics. We only write on throttle transitions (rare), capturing what the
        // OS reported as the foreground window so a stuck-throttle report can be traced to its cause.
        private static string _logPath;

        // The user's normal in-focus frame limiter, captured the moment we throttle so we can
        // put it back exactly on refocus. Never persisted to disk.
        private static float? _savedFrameLimiter;

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
            if (inForeground)
            {
                _backgroundElapsed = 0f;
            }
            else
            {
                _backgroundElapsed += dt;
            }

            // Throttle only after the background condition has held continuously past the debounce
            // window. The moment the game is in the foreground again we drop to zero and un-throttle
            // immediately (no debounce on the way back in).
            bool shouldThrottle = BfpsConfig.Enabled && !inForeground
                && _backgroundElapsed >= BackgroundDebounceSeconds;

            if (shouldThrottle != _throttled)
            {
                OnThrottleStateChanged(shouldThrottle);
            }

            if (!shouldThrottle)
            {
                return;
            }

            // Guaranteed throttle: sleeping the managed main loop stalls the whole frame (and thus
            // presentation), so this caps the framerate even if the native limiter is ignored or
            // bypassed by VSync.
            int fps = ClampFps(BfpsConfig.BackgroundFps);
            int budgetMs = 1000 / fps;
            int sleepMs = budgetMs - (int)(dt * 1000f);
            if (sleepMs > 0)
            {
                Thread.Sleep(sleepMs);
            }
        }

        private static void OnThrottleStateChanged(bool throttle)
        {
            DebugLog(throttle ? "THROTTLE ON" : "THROTTLE OFF");
            try
            {
                if (throttle)
                {
                    // Capture the live limiter once, then drop it to the background target.
                    // SetConfig writes the raw engine value (no 30 fps UI clamp), and
                    // ApplyConfigChanges(false) makes it live without touching the window.
                    // We deliberately do NOT SaveConfig() so the on-disk value is untouched.
                    _savedFrameLimiter = NativeOptions.GetConfig(NativeOptions.NativeOptionsType.FrameLimiter);
                    NativeOptions.SetConfig(NativeOptions.NativeOptionsType.FrameLimiter, ClampFps(BfpsConfig.BackgroundFps));
                    NativeOptions.ApplyConfigChanges(false);
                }
                else
                {
                    if (_savedFrameLimiter.HasValue)
                    {
                        NativeOptions.SetConfig(NativeOptions.NativeOptionsType.FrameLimiter, _savedFrameLimiter.Value);
                        NativeOptions.ApplyConfigChanges(false);
                    }
                }
            }
            catch
            {
                // Never let a config call crash the game; the Thread.Sleep throttle still applies.
            }
            finally
            {
                _throttled = throttle;
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
                        // No foreground window at all. This is a TRANSIENT activation-loss state
                        // (focus changing hands, a Bluetooth/audio device switch, a notification,
                        // the lock screen, etc.), NOT a deliberate alt-tab away from the game.
                        // Treating it as "background" is exactly what used to pin the game at the
                        // throttled framerate and never recover. Treat "unknown" as foreground so
                        // we can never get stuck; a real minimize/alt-tab still reports another
                        // window's PID below and throttles normally (after the debounce).
                        return true;
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

        // Append one line per throttle transition, including the raw foreground-window handle and
        // its owning PID vs. ours. If the throttle ever latches again, this file pinpoints which
        // window the OS considered foreground at the moment we decided the game was backgrounded.
        private static void DebugLog(string what)
        {
            try
            {
                if (_logPath == null)
                {
                    string dir = System.IO.Path.GetDirectoryName(typeof(SubModule).Assembly.Location);
                    _logPath = System.IO.Path.Combine(dir, "bfps-debug.log");
                }

                string fgInfo;
                try
                {
                    IntPtr fg = GetForegroundWindow();
                    if (fg == IntPtr.Zero)
                    {
                        fgInfo = "fg=0x0 (none)";
                    }
                    else
                    {
                        GetWindowThreadProcessId(fg, out uint pid);
                        fgInfo = $"fg=0x{fg.ToInt64():X} fgPid={pid}";
                    }
                }
                catch
                {
                    fgInfo = "fg=unavailable";
                }

                File.AppendAllText(_logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {what} ourPid={_ourPid} {fgInfo} win32={_win32Available} bgElapsed={_backgroundElapsed:0.00}{Environment.NewLine}");
            }
            catch
            {
                // Diagnostics must never affect gameplay.
            }
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
