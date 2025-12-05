// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CoreHaptics;
using Foundation;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Input;
using osu.Framework.Logging;
using osu.Framework.Utils;
using UIKit;

// TODO: Any documentation on methods here should be ported to HapticManager interface. For now, I'm leaving them here for my own reference.
namespace osu.Framework.iOS
{
    [SuppressMessage("Interoperability", "CA1422:Validate platform compatibility")]
    public class IOSHapticManager : HapticManager, IDisposable
    {
        // TODO: This being static makes it insanely difficult to query haptic support in a cross-platform manner.
        // But making it non-static means GameHost cannot query it without instantiating the manager first, which is also not ideal.
        public override bool SupportsHaptics => CHHapticEngine.GetHardwareCapabilities().SupportsHaptics;

        private UINotificationFeedbackGenerator? notificationFeedbackGenerator;

        // !! WARNING !!
        // The values here do NOT do what you think they do! You should leave these as-is and only modify via dynamic parameters.
        // The intensity here is MULTIPLIED by the intensity set from dynamic parameters, effectively making this value a "maximum" intensity.
        private readonly CHHapticEventParameter intensityParameter = new CHHapticEventParameter(CHHapticEventParameterId.HapticIntensity, 1.0f);

        // Sharpness behaves differently - dynamic parameter values are ADDED to this base value, effectively making this value a "minimum" sharpness.
        private readonly CHHapticEventParameter sharpnessParameter = new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, 0.0f);

        private bool disposedValue;
        private CHHapticEngine? engine;
        private ICHHapticAdvancedPatternPlayer? continuousPlayer;

        private float storedIntensity = 1.0f;
        private float storedSharpness;

        public IOSHapticManager(FrameworkConfigManager configManager)
            : base(configManager)
        { }

        protected override void Initialize()
        {
            base.Initialize();

            if (!SupportsHaptics)
            {
                Logger.Log("Haptics not supported on this device.");
                return;
            }

            if (engine == null)
                createEngine();
            else
                _ = restartEngine();

            notificationFeedbackGenerator = new UINotificationFeedbackGenerator();
        }

        protected override void Disable()
        {
            base.Disable();

            // Dispose the continuous player to allow the Taptic Engine to fully power down.
            continuousPlayer?.Dispose();
            continuousPlayer = null;

            engine?.Stop(null);

            // Dispose feedback generators to free up resources and prevent their continued use.
            notificationFeedbackGenerator?.Dispose();
            notificationFeedbackGenerator = null;
        }

        public override void UpdateIntensity(float intensity, bool force = false)
        {
            if (engine == null)
                return;

            if (continuousPlayer == null)
                throw new InvalidOperationException("Continuous haptic player is not initialized.");

            base.UpdateIntensity(intensity, force);

            if (!force && Math.Abs(storedIntensity - intensity) < Precision.FLOAT_EPSILON) return;

            var param = new CHHapticDynamicParameter(
                CHHapticDynamicParameterId.HapticIntensityControl,
                intensity,
                0
            );

            continuousPlayer.Send(
                [param],
                0,
                out NSError? updateIntensityErr
            );

            if (updateIntensityErr != null)
                throw new InvalidOperationException("Failed to update intensity: " + updateIntensityErr.LocalizedDescription);

            storedIntensity = intensity;
        }

        public override void UpdateSharpness(float sharpness, bool force = false)
        {
            if (engine == null)
                return;

            // If the continuous player is not initialized, that indicates a serious issue with the haptic engine, we should not silently fail here.
            if (continuousPlayer == null)
                throw new InvalidOperationException("Continuous haptic player is not initialized.");

            base.UpdateSharpness(sharpness, force);

            if (!force && Math.Abs(storedSharpness - sharpness) < Precision.FLOAT_EPSILON)
                return;

            var param = new CHHapticDynamicParameter(
                CHHapticDynamicParameterId.HapticSharpnessControl,
                sharpness,
                0
            );

            continuousPlayer.Send(
                [param],
                0,
                out NSError? updateSharpnessErr
            );

            if (updateSharpnessErr != null)
            {
                Logger.Log("Failed to update sharpness: " + updateSharpnessErr.LocalizedDescription, LoggingTarget.Runtime, LogLevel.Error);
                return;
            }

            storedSharpness = sharpness;
        }

        public override void PlayTransient(float intensityValue, float sharpnessValue)
        {
            if (engine == null)
                return;

            base.PlayTransient(intensityValue, sharpnessValue);

            var intensity = new CHHapticEventParameter(CHHapticEventParameterId.HapticIntensity, intensityValue);
            var sharpness = new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, sharpnessValue);

            var hapticEvent = new CHHapticEvent(
                CHHapticEventType.HapticTransient,
                [intensity, sharpness],
                0
            );

            var pattern = new CHHapticPattern(
                [hapticEvent],
                Array.Empty<CHHapticDynamicParameter>(),
                out NSError? patternErr
            );

            if (patternErr != null)
            {
                Logger.Log("Failed to create transient haptic pattern: " + patternErr.LocalizedDescription, LoggingTarget.Runtime, LogLevel.Error);
                return;
            }

            // This method creates a one-shot player that plays the transient haptic and then disposes itself.
            // Players are lightweight, so creating and disposing them frequently is not an issue.
            // The main issue here is the latency introduced by creating a new player each time.
            // If we want to reuse players, we forfeit the ability to adjust intensity/sharpness per transient event.
            // It would be wise to benchmark both methods and see just how much latency is introduced by creating a new player each time.
            // See https://developer.apple.com/documentation/corehaptics/playing-a-single-tap-haptic-pattern?language=objc
            ICHHapticPatternPlayer? player = engine.CreatePlayer(pattern, out NSError? playerErr);

            if (playerErr != null || player == null)
            {
                Logger.Log("Failed to create transient haptic player: " + playerErr?.LocalizedDescription, LoggingTarget.Runtime, LogLevel.Error);
                return;
            }

            player.Start(0, out NSError? startErr);

            if (startErr != null)
            {
                Logger.Log("Failed to start transient haptic player: " + startErr.LocalizedDescription, LoggingTarget.Runtime, LogLevel.Error);
                return;
            }

            // Dispose the player after a short delay to allow the haptic to play fully
            Task.Delay(3000).ContinueWith(_ =>
                player.Dispose()
            );
        }

        /// <summary>
        /// Attempts to restart the haptic engine if it has stopped, retrying up to nth times if necessary.
        /// </summary>
        /// <param name="retry">Whether to retry and gracefully fail, or to throw an exception if the first kickstart attempt is unsuccessful</param>
        /// <param name="maxAttempts">The maximum number of attempts</param>
        /// <exception cref="InvalidOperationException"></exception>
        private async Task restartEngine(bool retry = true, int maxAttempts = 10)
        {
            if (engine == null)
                throw new InvalidOperationException("Haptic Engine is not initialized.");

            int attempt = 0;

            // Dispose the existing continuous player, we'll recreate it after the engine restarts
            continuousPlayer?.Dispose();
            continuousPlayer = null;

            while (true)
            {
                try
                {
                    await engine.StartAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (!retry)
                        throw new InvalidOperationException("Failed to restart Haptic Engine: " + ex);

                    if (attempt >= maxAttempts)
                        throw new InvalidOperationException("Haptic Engine restart failed after 10 attempts, giving up. Fail reason: " + ex);

                    Logger.Log($"Haptic Engine restart failed, attempt {attempt + 1}, trying again...");
                    Logger.Log("Reason: " + ex);

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
        /// Creates and starts the haptic engine
        /// </summary>
        private void createEngine()
        {
            while (engine == null)
            {
                try
                {
                    engine = new CHHapticEngine(out NSError? engineErr);

                    if (engineErr != null)
                        throw new InvalidOperationException("Failed to create Haptic Engine: " + engineErr.LocalizedDescription);

                    // Engine can stop for various external reasons, so we need to be able to restart it automatically if that is the case
                    // If restarting fails after several attempts, we give up and disable haptics for the session.
                    engine.StoppedHandler = reason =>
                    {
                        Logger.Log("[Haptic] Haptic Engine Stopped: " + reason);

                        try
                        {
                            _ = restartEngine();
                        }
                        catch
                        {
                            engine = null;
                            Logger.Log("Stability of the Haptic Engine is compromised, disabling Haptics for the remainder of this session. Please restart osu! to re-enable haptics.",
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
                            engine.Dispose();
                            engine = null;
                            Logger.Log("Stability of the Haptic Engine is compromised, disabling Haptics for the remainder of this session. Please restart osu! to re-enable haptics.",
                                LoggingTarget.Runtime, LogLevel.Error);
                        }
                    };

                    // We don't plan to play any audio through the haptic engine, so mute it to lower latency for haptics.
                    engine.PlaysHapticsOnly = true;

                    engine.Start(out NSError? engineStartErr);

                    if (engineStartErr != null)
                        throw new InvalidOperationException("Failed to start Haptic Engine: " + engineStartErr.LocalizedDescription);

                    while (continuousPlayer == null)
                        createContinuousPlayer();

                    Logger.Log("Haptic Engine Started");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to create Haptic Engine");
                    engine?.Dispose();
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
            if (engine == null)
                return;

            var hapticEvent = new CHHapticEvent(
                CHHapticEventType.HapticContinuous,
                [intensityParameter, sharpnessParameter],
                time: 0,
                duration: 10.0f // Duration is effectively meaningless here since we loop the player indefinitely. Has a maximum of 30 seconds.
            );

            var pattern = new CHHapticPattern(
                [hapticEvent],
                Array.Empty<CHHapticDynamicParameter>(),
                out NSError? patternErr
            );

            if (patternErr != null)
            {
                Logger.Log("Failed to create continuous haptic pattern: " + patternErr.LocalizedDescription, LoggingTarget.Runtime, LogLevel.Error);
                continuousPlayer = null;
                return;
            }

            continuousPlayer = engine.CreateAdvancedPlayer(pattern, out NSError? contPlayerErr);

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

            continuousPlayer.Start(0, out NSError? contPlayerStartErr);

            if (contPlayerStartErr != null)
            {
                Logger.Log("Failed to start continuous haptic player: " + contPlayerStartErr.LocalizedDescription, LoggingTarget.Runtime, LogLevel.Error);
                continuousPlayer = null;
                return;
            }

            Logger.Log("Continuous Haptic Player created and started.");
        }

        #region Helpers

        public override void SuccessNotification()
        {
            Logger.Log("[Haptic] Success Notification");
            notificationFeedbackGenerator?.NotificationOccurred(UINotificationFeedbackType.Success);
        }

        public override void WarningNotification()
        {
            Logger.Log("[Haptic] Warning Notification");
            notificationFeedbackGenerator?.NotificationOccurred(UINotificationFeedbackType.Warning);
        }

        public override void ErrorNotification()
        {
            Logger.Log("[Haptic] Error Notification");
            notificationFeedbackGenerator?.NotificationOccurred(UINotificationFeedbackType.Error);
        }

        public override void Crash(float intensity = 1.0f, float sharpness = 1.0f, float durationSeconds = 1.0f)
        {
            if (engine == null)
                return;

            if (durationSeconds == 0)
                return;

            base.Crash(intensity, sharpness, durationSeconds);

            try
            {
                var transIntensity = new CHHapticEventParameter(CHHapticEventParameterId.HapticIntensity, intensity);
                var transSharpness = new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, sharpness);
                var hitEvent = new CHHapticEvent(
                    CHHapticEventType.HapticTransient,
                    [transIntensity, transSharpness],
                    0
                );

                // We set the base intensity of the rumble to 50% so that it isn't as overwhelming as the transient hit.
                var contIntensity = new CHHapticEventParameter(CHHapticEventParameterId.HapticIntensity, intensity * 0.5f);
                var contSharpness = new CHHapticEventParameter(CHHapticEventParameterId.HapticSharpness, 0.3f);
                var rumbleEvent = new CHHapticEvent(
                    CHHapticEventType.HapticContinuous,
                    [contIntensity, contSharpness],
                    0,
                    durationSeconds
                );

                // Since control points are linear, we need to create multiple points to simulate an ease-out curve
                // After some research, it seems like 16 points is the maximum before the engine starts ignoring extra points
                const int point_count = 16;
                var curvePoints = new CHHapticParameterCurveControlPoint[point_count + 1];

                for (int i = 0; i <= point_count; i++)
                {
                    float time = durationSeconds * i / point_count;

                    // You might wonder why the value is from 1.0 to 0.0 instead of 0.5 to 0.0
                    // This is because the curve modifies the base intensity of 0.5 set in the event parameter
                    // So a value of 1.0 means "full 0.5 intensity", and 0.0 means "0 intensity"
                    float value = (float)Interpolation.ValueAt(time, 1.0, 0.0, 0.0, durationSeconds, Easing.OutSine);

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
                    out NSError? patternErr
                );

                if (patternErr != null)
                    throw new InvalidOperationException("Failed to create HitAndFade haptic pattern: " + patternErr.LocalizedDescription);

                ICHHapticPatternPlayer? player = engine.CreatePlayer(patternWithCurve, out NSError? playerErr);

                if (playerErr != null || player == null)
                    throw new InvalidOperationException("Failed to create HitAndFade haptic player: " + playerErr?.LocalizedDescription);

                player.Start(0, out NSError? startErr);

                if (startErr != null)
                    throw new InvalidOperationException("Failed to start HitAndFade haptic player: " + startErr.LocalizedDescription);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[Haptic] Failed to play HitAndFade");
            }
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposedValue)
                return;

            if (disposing)
            {
                Disable();
                engine?.Dispose();
                engine = null;
            }

            disposedValue = true;
        }

        ~IOSHapticManager()
        {
            Dispose(false);
        }

        #endregion
    }
}
