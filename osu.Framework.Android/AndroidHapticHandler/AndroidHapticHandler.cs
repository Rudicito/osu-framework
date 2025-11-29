// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Input;
using osu.Framework.Logging;

namespace osu.Framework.Android.AndroidHapticHandler
{
    public class AndroidHapticHandler : IHapticHandler
    {
        private readonly IAndroidHaptics? engine;

        public AndroidHapticHandler()
        {
            try
            {
                // Try the modern handler
                if (OperatingSystem.IsAndroidVersionAtLeast(31))
                    engine = new VibratorManagerHandler();

                // Try the legacy handler
                else if (OperatingSystem.IsAndroidVersionAtLeast(26))
                    engine = new VibratorHandler();
            }
            catch (InvalidOperationException)
            {
            }

            Logger.Log("Vibration handler not available");
            engine = null;
        }

        public void PlayTransient(float intensity, float sharpness)
        {
            throw new NotImplementedException();
        }

        public void ButtonPress()
        {
            throw new NotImplementedException();
        }

        public void StartSlider(float intensity = IHapticHandler.DEFAULT_SLIDER_INTENSITY, float sharpness = IHapticHandler.DEFAULT_SLIDER_SHARPNESS)
        {
            throw new NotImplementedException();
        }

        public void StopSlider()
        {
            throw new NotImplementedException();
        }

        public void CreateContinuousPlayer()
        {
            throw new NotImplementedException();
        }

        public void UpdateIntensity(float intensity, bool force = false)
        {
            throw new NotImplementedException();
        }

        public void UpdateSharpness(float sharpness, bool force = false)
        {
            throw new NotImplementedException();
        }

        public void ReleaseAll()
        {
            throw new NotImplementedException();
        }

        public void Crash(float intensity = 1, float sharpness = 1, float durationSeconds = 1)
        {
            throw new NotImplementedException();
        }
    }
}
