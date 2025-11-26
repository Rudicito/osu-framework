// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Logging;

namespace osu.Framework.Input
{
    public class DefaultHapticHandler : IHapticHandler
    {
        public void PlayButtonPress()
        {
            Logger.Log("Haptic: Button Press");
        }

        public void PlayTransient(float intensity, float sharpness)
        {
            Logger.Log($"Haptic: Play Transient Requested (i {intensity} s {sharpness})");
        }

        public void StartSlider()
        {
            Logger.Log("Haptic: Slider Started");
        }

        public void StopSlider()
        {
            Logger.Log("Haptic: Slider Stopped");
        }

        public void ReleaseAll()
        {
            Logger.Log("Haptic: Release All");
        }
    }
}
