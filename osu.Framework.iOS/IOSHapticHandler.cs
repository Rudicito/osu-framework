// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CoreHaptics;
using osu.Framework.Input;
using osu.Framework.Logging;
using osu.Framework.Utils;
using static osu.Framework.Input.IHapticHandler;

namespace osu.Framework.iOS
{
    [SuppressMessage("Interoperability", "CA1422:Validate platform compatibility")]
    public class IOSHapticHandler : IHapticHandler
    {
        private CHHapticEngine? engine;
        private ICHHapticAdvancedPatternPlayer? continuousPlayer;
        private float storedIntensity = 1.0f;
        private float storedSharpness;

        private readonly CHHapticEventParameter intensityParameter = new CHHapticEventParameter(CHHapticEventParameterId.HapticIntensity, 1.0f);
        private readonly CHHapticEventParameter sharpnessParameter = new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, 0.0f);

        public static bool SupportsHaptics => CHHapticEngine.GetHardwareCapabilities().SupportsHaptics;

        public IOSHapticHandler()
        {
            if (SupportsHaptics)
                createEngine();
        }

        /// <summary>
        /// Attempts to restart the haptic engine if it has stopped, retrying up to nth times if necessary.
        /// </summary>
        /// <param name="retry">Whether to retry and gracefully fail, or to throw an exception if the first kickstart attempt is unsuccessful</param>
        /// <param name="maxAttempts">The maximum number of attempts</param>
        /// <exception cref="InvalidOperationException"></exception>
        private async Task kickStartEngine(bool retry = true, int maxAttempts = 10)
        {
            int attempt = 0;
            continuousPlayer = null;

            while (true)
            {
                if (engine == null)
                    throw new InvalidOperationException("Haptic Engine is not initialized.");

                engine.Start(out var restartErr);

                if (restartErr != null)
                {
                    if (!retry)
                        throw new InvalidOperationException("Failed to restart Haptic Engine: " + restartErr.LocalizedDescription);

                    if (attempt >= maxAttempts)
                        throw new InvalidOperationException("Haptic Engine restart failed after 10 attempts, giving up. Fail reason: " + restartErr.LocalizedDescription);

                    Logger.Log($"Haptic Engine restart failed, attempt {attempt}, trying again...");

                    await Task.Delay(1000).ConfigureAwait(false);

                    attempt += 1;
                    continue;
                }

                Logger.Log("Haptic Engine Restarted");

                while (continuousPlayer == null)
                    CreateContinuousPlayer();

                break;
            }
        }

        /// <summary>
        /// Creates and starts the haptic engine. Should be called once at app start.
        /// </summary>
        private void createEngine()
        {
            while (engine == null)
            {
                try
                {
                    engine = new CHHapticEngine(out _);

                    // Engine can stop for various reasons, so we need to be able to restart it automatically. If restarting fails after several attempts, we give up and disable haptics for the session.
                    engine.StoppedHandler = reason =>
                    {
                        Logger.Log("Haptic Engine Stopped: " + reason);

                        try
                        {
                            _ = kickStartEngine();
                        }
                        catch
                        {
                            engine = null;
                            Logger.Log("Stability of the Haptic Engine is compromised, disabling Haptics for the remainder of this session. Please restart the application to re-enable haptics.",
                                LoggingTarget.Runtime, LogLevel.Error);
                        }
                    };

                    // An engine reset points to a more serious issue. In this case, try restarting the engine, and if that fails, throw a fatal error.
                    engine.ResetHandler = () =>
                    {
                        Logger.Log("Haptic Engine Reset - Restarting...");

                        try
                        {
                            _ = kickStartEngine();
                        }
                        catch
                        {
                            engine = null;
                            Logger.Log("Stability of the Haptic Engine is compromised, disabling Haptics for the remainder of this session. Please restart the application to re-enable haptics.",
                                LoggingTarget.Runtime, LogLevel.Error);
                        }
                    };

                    engine.Start(out var engineStartErr);

                    if (engineStartErr != null)
                        throw new InvalidOperationException("Failed to start Haptic Engine: " + engineStartErr.LocalizedDescription);

                    while (continuousPlayer == null)
                        CreateContinuousPlayer();

                    Logger.Log("Haptic Engine Started");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to create Haptic Engine");
                    engine = null;
                }
            }
        }

        public void CreateContinuousPlayer()
        {
            if (engine == null) return;

            // !! WARNING !!
            // The values here do NOT do what you think they do!

            // The intensity here is multiplied by the intensity set in dynamic parameters, effectively making this value a "maximum" intensity.

            // Now sharpness behaves differently - the dynamic parameter value is ADDED to this value, effectively making this value a "minimum" sharpness.

            // In either case, you'll likely want to keep the values as they are and only modify them via dynamic parameters, as that gives the most predictable behavior.

            var hapticEvent = new CHHapticEvent(
                CHHapticEventType.HapticContinuous,
                [intensityParameter, sharpnessParameter],
                time: 0,
                duration: 10.0f // Duration is effectively meaningless here since we loop the player indefinitely. Has a maximum of 30 seconds.
            );

            var pattern = new CHHapticPattern(new[] { hapticEvent }, Array.Empty<CHHapticDynamicParameter>(), out var patternErr);

            if (patternErr != null)
            {
                Logger.Log("Failed to create haptic pattern: " + patternErr.LocalizedDescription, LoggingTarget.Runtime, LogLevel.Error);
                continuousPlayer = null;
                return;
            }

            continuousPlayer = engine.CreateAdvancedPlayer(pattern, out var contPlayerErr);

            if (contPlayerErr != null || continuousPlayer == null)
            {
                Logger.Log("Failed to create continuous haptic player: " + contPlayerErr?.LocalizedDescription, LoggingTarget.Runtime, LogLevel.Error);
                continuousPlayer = null;
                return;
            }

            // Loops the continuous haptic player indefinitely until stopped
            continuousPlayer.LoopEnabled = true;

            UpdateIntensity(0.0f, true);
            UpdateSharpness(0.0f, true);

            continuousPlayer.Start(0, out var contPlayerStartErr);

            if (contPlayerStartErr != null)
            {
                Logger.Log("Failed to start continuous haptic player: " + contPlayerStartErr.LocalizedDescription, LoggingTarget.Runtime, LogLevel.Error);
                continuousPlayer = null;
                return;
            }

            Logger.Log("Continuous Haptic Player created and started.");
        }

        public void UpdateIntensity(float intensity, bool force = false)
        {
            // If the engine is not initialized, we simply ignore intensity updates.
            if (engine == null)
                return;

            // Throw if we try to update intensity before the continuous player is created because this indicates a logic error rather than a runtime issue (i.e. haptics not being supported).
            if (continuousPlayer == null)
                throw new InvalidOperationException("Continuous haptic player is not initialized.");

            if (intensity is < 0.0f or > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(intensity), "Intensity must be equal to or between 0.0 and 1.0");

            if (!force && Math.Abs(storedIntensity - intensity) < Precision.FLOAT_EPSILON)
                return;

            var param = new CHHapticDynamicParameter(
                CHHapticDynamicParameterId.HapticIntensityControl,
                intensity,
                0);

            continuousPlayer.Send([
                    param
                ],
                0,
                out var updateIntensityErr);

            if (updateIntensityErr != null)
                throw new InvalidOperationException("Failed to update intensity: " + updateIntensityErr.LocalizedDescription);

            storedIntensity = intensity;
            Logger.Log($"[Haptic] Intensity Update: {intensity}");
        }

        public void UpdateSharpness(float sharpness, bool force = false)
        {
            if (engine == null)
                return;

            if (continuousPlayer == null)
                throw new InvalidOperationException("Continuous haptic player is not initialized.");

            if (sharpness is < 0.0f or > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(sharpness), "Intensity must be between 0.0 and 1.0");

            if (!force && Math.Abs(storedSharpness - sharpness) < Precision.FLOAT_EPSILON)
                return;

            var param = new CHHapticDynamicParameter(
                CHHapticDynamicParameterId.HapticSharpnessControl,
                sharpness,
                0);

            continuousPlayer.Send([
                    param
                ],
                0,
                out var updateSharpnessErr);

            if (updateSharpnessErr != null)
                throw new InvalidOperationException("Failed to update sharpness: " + updateSharpnessErr.LocalizedDescription);

            storedSharpness = sharpness;
            Logger.Log($"[Haptic] Sharpness Update: {sharpness}");
        }

        #region Helpers

        public void StartSlider(float initialIntensity = DEFAULT_SLIDER_INTENSITY, float initialSharpness = DEFAULT_SLIDER_SHARPNESS)
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

        public void ButtonPress() => PlayTransient(1.0f, 1.0f);

        public void PlayButtonRelease() => PlayTransient(0.5f, 0.35f);

        public void Crash(float intensity = 1.0f, float sharpness = 1.0f, float durationSeconds = 1.0f)
        {
            if (engine == null) return;
            if (durationSeconds <= 0) return;

            try
            {
                var transIntensity = new CHHapticEventParameter(CHHapticEventParameterId.HapticIntensity, intensity);
                var transSharpness = new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, sharpness);
                var hitEvent = new CHHapticEvent(
                    CHHapticEventType.HapticTransient,
                    [transIntensity, transSharpness],
                    0);

                var contIntensity = new CHHapticEventParameter(CHHapticEventParameterId.HapticIntensity, 0.5f);
                var contSharpness = new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, 0.3f); // Lower sharpness for the "tail"
                var rumbleEvent = new CHHapticEvent(
                    CHHapticEventType.HapticContinuous,
                    [contIntensity, contSharpness],
                    0,
                    durationSeconds);

                // Exponential Fade Out Curve
                var curvePoints = new[]
                {
                    new CHHapticParameterCurveControlPoint(0.0f, 1.0f),

                    new CHHapticParameterCurveControlPoint(durationSeconds * 0.1f, 0.5f),

                    new CHHapticParameterCurveControlPoint(durationSeconds * 0.2f, 0.25f),

                    new CHHapticParameterCurveControlPoint(durationSeconds * 0.3f, 0.125f),

                    new CHHapticParameterCurveControlPoint(durationSeconds * 0.5f, 0.03f),

                    new CHHapticParameterCurveControlPoint(durationSeconds, 0.0f)
                };

                // Create the curve specifically for Intensity
                var fadeCurve = new CHHapticParameterCurve(
                    CHHapticDynamicParameterId.HapticIntensityControl,
                    curvePoints,
                    0
                );

                var patternWithCurve = new CHHapticPattern(
                    [hitEvent, rumbleEvent],
                    [fadeCurve],
                    out _);

                var player = engine.CreatePlayer(patternWithCurve, out _);
                player?.Start(0, out _);

                Logger.Log($"[Haptic] Played HitAndFade ({durationSeconds}s)");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[Haptic] Failed to play HitAndFade");
            }
        }

        #endregion

        public void ReleaseAll()
        {
            // Currently only sets intensity to 0.
            // Will have more use when lifecycle management is overhauled
            UpdateIntensity(0.0f);
            Logger.Log("Releasing continuous haptics.");
        }

        public void PlayTransient(float intensityValue, float sharpnessValue)
        {
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
