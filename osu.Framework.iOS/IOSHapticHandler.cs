// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CoreHaptics;
using osu.Framework.Input;
using osu.Framework.Logging;

namespace osu.Framework.iOS
{
    [SuppressMessage("Interoperability", "CA1422:Validate platform compatibility")]
    public class IOSHapticHandler : IHapticHandler
    {
        private CHHapticEngine? engine;
        private ICHHapticAdvancedPatternPlayer? continuousPlayer;

        public static bool SupportsHaptics => CHHapticEngine.GetHardwareCapabilities().SupportsHaptics;

        public IOSHapticHandler()
        {
            if (SupportsHaptics)
                createEngine();
        }

        private async void createEngine()
        {
            try
            {
                while (engine == null)
                {
                    try
                    {
                        engine = new CHHapticEngine(out _);

                        // 2. Handle engine stops (e.g. app goes to background)
                        engine.StoppedHandler = reason =>
                        {
                            Logger.Log("Haptic Engine Stopped: " + reason);
                            engine = null;
                        };

                        // 3. Handle engine reset (server restart)
                        engine.ResetHandler = () =>
                        {
                            Logger.Log("Haptic Engine Reset - Restarting...");

                            try
                            {
                                engine?.Start(out _);
                            }
                            catch
                            {
                                Logger.Log("Failed to restart the Haptic Engine");
                                engine = null;
                            }

                            createContinuousPlayer();
                        };

                        engine.Start(out _);
                        Logger.Log("Haptic Engine Started");
                        createContinuousPlayer();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Failed to create Haptic Engine, retrying in 5 seconds...");
                        engine = null;
                        await Task.Delay(5000).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception)
            { }
        }

        /// <summary>
        /// Creates a continuous player for haptics. Should be called at the start of each Player session.
        /// </summary>
        private void createContinuousPlayer()
        {
            if (engine == null) return;

            var intensity = new CHHapticEventParameter(CHHapticEventParameterId.HapticIntensity, 1.0f);
            var sharpness = new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, 0.1f);

            var hapticEvent = new CHHapticEvent(
                CHHapticEventType.HapticContinuous,
                new[] { intensity, sharpness },
                time: 0,
                duration: 3600.0f); // Long duration of an hour to allow for continuous playback

            var pattern = new CHHapticPattern(new[] { hapticEvent }, Array.Empty<CHHapticDynamicParameter>(), out _);

            continuousPlayer = engine.CreateAdvancedPlayer(pattern, out _);

            if (continuousPlayer == null) return;

            continuousPlayer.LoopEnabled = true;

            updateIntensity(0.0f);

            continuousPlayer.Start(0, out _);
            Logger.Log("Continuous Haptic Player created and started.");
        }

        private void updateIntensity(float value)
        {
            var param = new CHHapticDynamicParameter(
                CHHapticDynamicParameterId.HapticIntensityControl,
                value,
                0);

            ICHHapticPatternPlayer? basePlayer = continuousPlayer;
            basePlayer?.Send(new[] { param }, 0, out _);
        }

        public void ReleaseAll()
        {
            if (continuousPlayer == null) return;

            updateIntensity(0.0f);
            Logger.Log("Releasing all haptics.");
        }

        public void StartSlider()
        {
            if (continuousPlayer == null) return;

            updateIntensity(0.3f);
            Logger.Log("Haptic Slider Start");
        }

        public void StopSlider()
        {
            if (continuousPlayer == null) return;

            updateIntensity(0.0f);
            PlayTransient(0.5f, 1.0f);
            Logger.Log("Haptic Slider End");
        }

        public void PlayButtonPress() => PlayTransient(1.0f, 1.0f);

        public void PlayButtonRelease() => PlayTransient(0.5f, 0.35f);

        public void PlayTransient(float intensityValue, float sharpnessValue)
        {
            if (!SupportsHaptics || engine == null) return;

            if (intensityValue is < 0.0f or > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(intensityValue), "Intensity must be between 0.0 and 1.0");
            if (sharpnessValue is < 0.0f or > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(sharpnessValue), "Sharpness must be between 0.0 and 1.0");

            try
            {
                var intensity = new CHHapticEventParameter(CHHapticEventParameterId.HapticIntensity, intensityValue);
                var sharpness = new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, sharpnessValue);

                var hapticEvent = new CHHapticEvent(
                    CHHapticEventType.HapticTransient,
                    new[] { intensity, sharpness },
                    0);

                var pattern = new CHHapticPattern(new[] { hapticEvent }, Array.Empty<CHHapticDynamicParameter>(), out _);
                var player = engine.CreatePlayer(pattern, out _);

                player?.Start(0, out _);
                Logger.Log("Haptic Transient Played with intensity " + intensityValue + " and sharpness " + sharpnessValue);
            }
            catch
            {
            }
        }
    }
}
