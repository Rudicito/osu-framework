// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Runtime.Versioning;
using Android.OS;
using osu.Framework.Graphics;
using osu.Framework.Input;
using osu.Framework.Logging;
using osu.Framework.Utils;

namespace osu.Framework.Android.HapticHandler
{
    [SupportedOSPlatform("android26.0")]
    public class AndroidHapticHandler : IHapticHandler
    {
        private readonly IAndroidHaptics engine;

        public static IAndroidHaptics? GetAndroidHaptics()
        {
            try
            {
                // Try the modern handler
                if (OperatingSystem.IsAndroidVersionAtLeast(31))
                    return new VibratorManagerHandler();

                // Try the legacy handler
                if (OperatingSystem.IsAndroidVersionAtLeast(26))
                    return new VibratorHandler();
            }
            catch (InvalidOperationException)
            {
            }

            Logger.Log("Android Vibration handler not available");

            return null;
        }

        public AndroidHapticHandler(IAndroidHaptics androidHaptics)
        {
            engine = androidHaptics;
        }

        public void PlayTransient(float intensity, float sharpness)
        {
            int amplitude = GetAmplitude(intensity);

            if (amplitude == 0)
            {
                ReleaseAll();
                return;
            }

            long[] timings = { 50 };
            int[] amplitudes = { amplitude };

            ReleaseAll();
            engine.Vibrate(VibrationEffect.CreateWaveform(timings, amplitudes, -1)!);
        }

        public void ButtonPress() => PlayTransient(1.0f, 1.0f);

        public void StartSlider(float intensity = IHapticHandler.DEFAULT_SLIDER_INTENSITY, float sharpness = IHapticHandler.DEFAULT_SLIDER_SHARPNESS)
        {
            UpdateIntensity(intensity);
            UpdateSharpness(sharpness);
        }

        public void StopSlider() => UpdateIntensity(0.0f);

        public void CreateContinuousPlayer()
        {
            UpdateIntensity(0.0f);
            UpdateSharpness(0.0f);
        }

        public void UpdateIntensity(float intensity, bool force = false)
        {
            int amplitude = GetAmplitude(intensity);

            if (amplitude == 0)
            {
                ReleaseAll();
                return;
            }

            long[] timings = { 1000 };
            int[] amplitudes = { amplitude };

            ReleaseAll();
            engine.Vibrate(VibrationEffect.CreateWaveform(timings, amplitudes, 0)!);
        }

        /// <remarks>
        /// Do nothing because Sharpness is not an option on Android (can be recreated with custom amplitudes?)
        /// </remarks>
        public void UpdateSharpness(float sharpness, bool force = false)
        {
        }

        public void ReleaseAll()
        {
            engine.Cancel();
        }

        public void Crash(float intensity = 1, float sharpness = 1, float durationSeconds = 1)
        {
            const int precision = 50;

            long durationMilliSeconds = (long)(durationSeconds * 1000f);

            long[] timings = new long[precision];
            int[] amplitudes = new int[precision];

            long step = durationMilliSeconds / precision;

            for (int i = 0; i < precision; i++)
            {
                timings[i] = step;

                float t = Interpolation.ValueAt(i, 1, 0, 0, precision - 1, Easing.OutExpo);
                amplitudes[i] = GetAmplitude(intensity * t);
            }

            ReleaseAll();
            engine.Vibrate(VibrationEffect.CreateWaveform(timings, amplitudes, -1)!);
        }

        public int GetAmplitude(float intensity)
        {
            return Math.Clamp((int)(intensity * 255f), 0, 255);
        }
    }
}
