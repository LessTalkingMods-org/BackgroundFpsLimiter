using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace BackgroundFpsLimiter
{
    // Lives in a separate assembly loaded only when MCM is present (see SubModule.TryLoadMcm).
    // MCM discovers this type by scanning loaded assemblies; every property forwards to the
    // static BfpsConfig so the core mod keeps working on defaults without MCM.
    public class BackgroundFpsLimiterMcmSettings : AttributeGlobalSettings<BackgroundFpsLimiterMcmSettings>
    {
        public override string Id { get { return "BackgroundFpsLimiter"; } }
        public override string DisplayName { get { return "Background FPS Limiter"; } }
        public override string FolderName { get { return "BackgroundFpsLimiter"; } }
        public override string FormatType { get { return "json2"; } }

        [SettingPropertyBool("Enabled", Order = 0, RequireRestart = false, HintText = "Cap the framerate while the game window is in the background (alt-tabbed or minimized) to save CPU/GPU. Restores your normal framerate when you return to the game.")]
        [SettingPropertyGroup("Background FPS Limiter", GroupOrder = 0)]
        public bool Enabled
        {
            get { return BfpsConfig.Enabled; }
            set { BfpsConfig.Enabled = value; }
        }

        [SettingPropertyInteger("Background FPS", 1, 60, "0", Order = 1, RequireRestart = false, HintText = "Target framerate while the window is in the background. Lower = bigger savings (default 5).")]
        [SettingPropertyGroup("Background FPS Limiter", GroupOrder = 0)]
        public int BackgroundFps
        {
            get { return BfpsConfig.BackgroundFps; }
            set { BfpsConfig.BackgroundFps = value; }
        }
    }
}
