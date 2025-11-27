// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Logging;

namespace osu.Framework.Input
{
    public class DefaultHapticHandler : IHapticHandler
    {
        public void ButtonPress()
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
            UpdateIntensity(intensity);
            UpdateSharpness(sharpness);
        }

        public void StopSlider()
        {
            Logger.Log("[Haptic] Slider Stopped (i 0.0)");
            UpdateIntensity(0.0f);
        }

        public void CreateContinuousPlayer()
        {
            Logger.Log("[Haptic] Created Continuous Player");
            UpdateIntensity(0.0f);
            UpdateSharpness(0.0f);
        }

        public void UpdateIntensity(float intensity, bool force = false)
        {
            Logger.Log($"[Haptic] Updated intensity (i {intensity})");
        }

        public void UpdateSharpness(float sharpness, bool force = false)
        {
            Logger.Log($"[Haptic] Updated sharpness (s {sharpness})");
        }

        public void ReleaseAll()
        {
            Logger.Log("[Haptic] Release All");
            UpdateIntensity(0.0f);
        }

        public void Crash(float intensity = 1, float sharpness = 1, float durationSeconds = 1)
        {
            Logger.Log("[Haptic] Crash");
        }
    }
}
