// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CoreHaptics;
using osu.Framework.Input;
using osu.Framework.Logging;
using osu.Framework.Utils;
using UIKit;
using static osu.Framework.Input.IHapticHandler;

// TODO: Any documentation on methods here should be ported to IHapticHandler interface. For now, I'm leaving them here for my own reference.
namespace osu.Framework.iOS
{
    [SuppressMessage("Interoperability", "CA1422:Validate platform compatibility")]
    public class IOSHapticHandler : IHapticHandler, IDisposable
    {
        private bool disposedValue;
        private CHHapticEngine? engine;
        private ICHHapticAdvancedPatternPlayer? continuousPlayer;
        private readonly UISelectionFeedbackGenerator selectionFeedbackGenerator = new UISelectionFeedbackGenerator();
        private readonly UIImpactFeedbackGenerator impactFeedbackGenerator = new UIImpactFeedbackGenerator(UIImpactFeedbackStyle.Medium);
        private readonly UIImpactFeedbackGenerator rigidImpactFeedbackGenerator = new UIImpactFeedbackGenerator(UIImpactFeedbackStyle.Rigid);
        private readonly UIImpactFeedbackGenerator softImpactFeedbackGenerator = new UIImpactFeedbackGenerator(UIImpactFeedbackStyle.Soft);
        private readonly UINotificationFeedbackGenerator notificationFeedbackGenerator = new UINotificationFeedbackGenerator();

        // !! WARNING !!
        // The values here do NOT do what you think they do! You should leave these as-is and only modify via dynamic parameters.

        // The intensity here is MULTIPLIED by the intensity set from dynamic parameters, effectively making this value a "maximum" intensity.
        private readonly CHHapticEventParameter intensityParameter = new CHHapticEventParameter(CHHapticEventParameterId.HapticIntensity, 1.0f);

        // Sharpness behaves differently - dynamic parameter values are ADDED to this base value, effectively making this value a "minimum" sharpness.
        private readonly CHHapticEventParameter sharpnessParameter = new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, 0.0f);

        private float storedIntensity = 1.0f;
        private float storedSharpness;

        public static bool SupportsHaptics => CHHapticEngine.GetHardwareCapabilities().SupportsHaptics;

        public IOSHapticHandler()
        {
            // TODO: Add user setting to disable haptics entirely.
            if (SupportsHaptics)
            {
                createEngine();
                selectionFeedbackGenerator.Prepare();
            }
        }

        /// <summary>
        /// Attempts to restart the haptic engine if it has stopped, retrying up to nth times if necessary.
        /// </summary>
        /// <param name="retry">Whether to retry and gracefully fail, or to throw an exception if the first kickstart attempt is unsuccessful</param>
        /// <param name="maxAttempts">The maximum number of attempts</param>
        /// <exception cref="InvalidOperationException"></exception>
        private async Task restartEngine(bool retry = true, int maxAttempts = 10)
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
                    createContinuousPlayer();

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
                            _ = restartEngine();
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
                            _ = restartEngine();
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
                        createContinuousPlayer();

                    Logger.Log("Haptic Engine Started");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to create Haptic Engine");
                    engine = null;
                }
            }
        }

        /// <summary>
        /// Creates a continuous haptic player with 0 intensity and 0 sharpness.
        /// Modify intensity and sharpness via dynamic parameters using <see cref="UpdateIntensity"/> and <see cref="UpdateSharpness"/>.
        /// </summary>
        private void createContinuousPlayer()
        {
            if (engine == null) return;

            var hapticEvent = new CHHapticEvent(
                CHHapticEventType.HapticContinuous,
                [intensityParameter, sharpnessParameter],
                time: 0,
                duration: 10.0f // Duration is effectively meaningless here since we loop the player indefinitely. Has a maximum of 30 seconds.
            );

            var pattern = new CHHapticPattern([hapticEvent], Array.Empty<CHHapticDynamicParameter>(), out var patternErr);

            if (patternErr != null)
            {
                Logger.Log("Failed to create continuous haptic pattern: " + patternErr.LocalizedDescription, LoggingTarget.Runtime, LogLevel.Error);
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

        public void StartContinuous(float initialIntensity = DEFAULT_SLIDER_INTENSITY, float initialSharpness = DEFAULT_SLIDER_SHARPNESS)
        {
            Logger.Log($"[Haptic] Slider Start (i {initialIntensity} s {initialSharpness})");
            UpdateIntensity(initialIntensity);
            UpdateSharpness(initialSharpness);
        }

        public void ButtonPress()
        {
            Logger.Log("[Haptic] Button Press");
            impactFeedbackGenerator.ImpactOccurred();
        }

        public void ToggleOn()
        {
            Logger.Log("[Haptic] Toggle On");
            rigidImpactFeedbackGenerator.ImpactOccurred();
        }

        public void ToggleOff()
        {
            Logger.Log("[Haptic] Toggle Off");
            softImpactFeedbackGenerator.ImpactOccurred();
        }

        public void SelectionChanged()
        {
            Logger.Log("[Haptic] Selection Changed");
            selectionFeedbackGenerator.SelectionChanged();
        }

        public void SuccessNotification()
        {
            Logger.Log("[Haptic] Success Notification");
            notificationFeedbackGenerator.NotificationOccurred(UINotificationFeedbackType.Success);
        }

        public void WarningNotification()
        {
            Logger.Log("[Haptic] Warning Notification");
            notificationFeedbackGenerator.NotificationOccurred(UINotificationFeedbackType.Warning);
        }

        public void ErrorNotification()
        {
            Logger.Log("[Haptic] Error Notification");
            notificationFeedbackGenerator.NotificationOccurred(UINotificationFeedbackType.Error);
        }

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

                // easeOutSine Fade Out Curve
                const int point_count = 10;
                var curvePoints = new CHHapticParameterCurveControlPoint[point_count + 1];

                for (int i = 0; i <= point_count; i++)
                {
                    float time = durationSeconds * i / point_count;
                    float value = (float)Math.Cos(i / (float)point_count * Math.PI / 2.0);
                    curvePoints[i] = new CHHapticParameterCurveControlPoint(time, value);
                }

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

        /// <summary>
        /// Creates a buzz haptic effect - a temporary strong rumble.
        /// Ensure the duration is short to avoid discomfort.
        /// </summary>
        /// <param name="duration"></param>
        /// <param name="intensity"></param>
        /// <param name="sharpness"></param>
        public void Buzz(float duration, float intensity = 1f, float sharpness = 1f)
        {
        }

        #endregion

        public void ReleaseContinuous()
        {
            Logger.Log("[Haptic] Releasing continuous haptics");
            // Currently only sets intensity to 0.
            // Will have more use when lifecycle management is overhauled
            UpdateIntensity(0.0f);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposedValue)
                return;

            engine?.Stop(null);
            engine = null;
            continuousPlayer = null;
            disposedValue = true;
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
