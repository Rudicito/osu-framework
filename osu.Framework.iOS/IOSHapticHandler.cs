// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics.CodeAnalysis;
using CoreHaptics;
using osu.Framework.Input;
using osu.Framework.Logging;
using osu.Framework.Utils;

namespace osu.Framework.iOS
{
    [SuppressMessage("Interoperability", "CA1422:Validate platform compatibility")]
    public class IOSHapticHandler : IHapticHandler
    {
        private CHHapticEngine? engine;
        private ICHHapticAdvancedPatternPlayer? continuousPlayer;
        private float storedIntensity = 0.3f;
        private float storedSharpness = 0.1f;

        public static bool SupportsHaptics => CHHapticEngine.GetHardwareCapabilities().SupportsHaptics;

        public IOSHapticHandler()
        {
            if (SupportsHaptics)
                createEngine();
        }

        /// <summary>
        /// Creates and starts the haptic engine. Should be called once at app start.
        /// </summary>
        private void createEngine()
        {
            try
            {
                while (engine == null)
                {
                    try
                    {
                        engine = new CHHapticEngine(out _);

                        // Engine can stop for various reasons, so we need to be able to restart it automatically. If restarting fails, we simply leave it stopped.
                        engine.StoppedHandler = reason =>
                        {
                            Logger.Log("Haptic Engine Stopped: " + reason);

                            try
                            {
                                engine?.Start(out _);
                                Logger.Log("Haptic Engine Restarted");
                            }
                            catch
                            {
                                Logger.Log("Failed to restart the Haptic Engine");
                                engine = null;
                            }
                        };

                        // An engine reset points to a more serious issue. In this case, try restarting the engine, and if that fails, throw a fatal error.
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

                            CreateContinuousPlayer();
                        };

                        engine.Start(out _);
                        Logger.Log("Haptic Engine Started");
                        CreateContinuousPlayer();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Failed to create Haptic Engine");
                        engine = null;
                    }
                }
            }
            catch (Exception)
            { }
        }

        public void CreateContinuousPlayer(float defaultIntensity = IHapticHandler.DEFAULT_SLIDER_INTENSITY, float defaultSharpness = IHapticHandler.DEFAULT_SLIDER_SHARPNESS)
        {
            if (engine == null) return;

            if (defaultIntensity is < 0.0f or > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(defaultIntensity), "Intensity must be between 0.0 and 1.0");
            if (defaultSharpness is < 0.0f or > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(defaultSharpness), "Sharpness must be between 0.0 and 1.0");

            var intensity = new CHHapticEventParameter(CHHapticEventParameterId.HapticIntensity, defaultIntensity);
            var sharpness = new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, defaultSharpness);
            storedIntensity = defaultIntensity;
            storedSharpness = defaultSharpness;

            var hapticEvent = new CHHapticEvent(
                CHHapticEventType.HapticContinuous,
                new[] { intensity, sharpness },
                time: 0,
                duration: 3600.0f); // Long duration of an hour to allow for continuous playback

            var pattern = new CHHapticPattern(new[] { hapticEvent }, Array.Empty<CHHapticDynamicParameter>(), out _);

            continuousPlayer = engine.CreateAdvancedPlayer(pattern, out _);

            if (continuousPlayer == null) return;

            continuousPlayer.LoopEnabled = true;

            UpdateIntensity(0.0f);

            continuousPlayer.Start(0, out _);
            Logger.Log("Continuous Haptic Player created and started.");
        }

        public void UpdateIntensity(float intensity)
        {
            if (continuousPlayer == null)
                throw new InvalidOperationException("Continuous haptic player is not initialized.");

            if (intensity is < 0.0f or > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(intensity), "Intensity must be between 0.0 and 1.0");

            if (Math.Abs(storedIntensity - intensity) < Precision.FLOAT_EPSILON)
                return;

            var param = new CHHapticDynamicParameter(
                CHHapticDynamicParameterId.HapticIntensityControl,
                intensity,
                0);

            ICHHapticPatternPlayer? basePlayer = continuousPlayer;
            basePlayer?.Send(new[] { param }, 0, out _);
            storedIntensity = intensity;
            Logger.Log($"[Haptic] Intensity Update: {intensity}");
        }

        public void UpdateSharpness(float sharpness)
        {
            if (continuousPlayer == null)
                throw new InvalidOperationException("Continuous haptic player is not initialized.");

            if (sharpness is < 0.0f or > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(sharpness), "Intensity must be between 0.0 and 1.0");

            if (Math.Abs(storedSharpness - sharpness) < Precision.FLOAT_EPSILON)
                return;

            var param = new CHHapticDynamicParameter(
                CHHapticDynamicParameterId.HapticSharpnessControl,
                sharpness,
                0);

            ICHHapticPatternPlayer? basePlayer = continuousPlayer;
            basePlayer?.Send(new[] { param }, 0, out _);
            Logger.Log($"[Haptic] Sharpness Update: {sharpness}");
        }

        #region Helpers

        public void StartSlider(float initialIntensity = IHapticHandler.DEFAULT_SLIDER_INTENSITY, float initialSharpness = IHapticHandler.DEFAULT_SLIDER_SHARPNESS)
        {
            Logger.Log($"[Haptic] Slider Start (i {initialIntensity} s {initialSharpness})");
            UpdateIntensity(initialIntensity);
            UpdateSharpness(initialSharpness);
        }

        public void StopSlider()
        {
            Logger.Log("[Haptic] Slider End");
            UpdateIntensity(0.0f);
        }

        public void PlayButtonPress() => PlayTransient(1.0f, 1.0f);

        public void PlayButtonRelease() => PlayTransient(0.5f, 0.35f);

        #endregion

        public void ReleaseAll()
        {
            UpdateIntensity(0.0f);
            Logger.Log("Releasing continuous haptics.");
        }

        public void PlayTransient(float intensityValue, float sharpnessValue)
        {
            if (!SupportsHaptics)
                throw new InvalidOperationException("Haptics are not supported on this device or the engine is not initialized.");

            if (engine == null)
                return;

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
                Logger.Log($"[Haptic] Played Transient (i {intensityValue} s {sharpnessValue})");
            }
            catch
            {
            }
        }
    }
}
