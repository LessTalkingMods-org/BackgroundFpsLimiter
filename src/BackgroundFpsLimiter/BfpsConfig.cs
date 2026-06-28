namespace BackgroundFpsLimiter
{
    /// <summary>
    /// Mutable runtime configuration. Defaults live here so the core mod works without MCM
    /// installed; the optional BackgroundFpsLimiter.MCM assembly overwrites these from the
    /// in-game settings screen. Holds NO MCM types, so the throttle path never touches MCM
    /// and the core assembly loads fine when MCM is absent.
    /// </summary>
    public static class BfpsConfig
    {
        // Master switch. When off, the game is never throttled on focus loss.
        public static bool Enabled = true;

        // Target framerate while the window is in the background (alt-tabbed / minimized).
        // Lower = bigger CPU/GPU savings. Clamped to >= 1 on use.
        public static int BackgroundFps = 5;
    }
}
