// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Logging;

namespace osu.Framework.Input
{
    public class DefaultHapticHandler : IHapticHandler
    {
        public void PlayButtonPress()
        {
            Logger.Log("[Haptic] Button Press");
        }

        public void PlayTransient(float intensity, float sharpness)
        {
            Logger.Log($"[Haptic] Playing Transient (i {intensity} s {sharpness})");
        }

        public void StartSlider(float intensity = 0.3f, float sharpness = 0.1f)
        {
            Logger.Log($"[Haptic] Slider Started (i {intensity} s {sharpness})");
        }

        public void StopSlider()
        {
            Logger.Log("[Haptic] Slider Stopped (i 0.0)");
        }

        public void CreateContinuousPlayer(float defaultIntensity, float defaultSharpness)
        {
        }

        public void UpdateIntensity(float intensity)
        {
            Logger.Log($"[Haptic] Updated intensity (i {intensity})");
        }

        public void UpdateSharpness(float sharpness)
        {
            Logger.Log($"[Haptic] Updated sharpness to (s {sharpness})");
        }

        public void ReleaseAll()
        {
            Logger.Log("[Haptic] Release All");
        }
    }
}
