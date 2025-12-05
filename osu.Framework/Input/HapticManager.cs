// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Logging;

namespace osu.Framework.Input
{
    // TODO: Make this disposable instead of the child classes having to implement their own disposal.
    public class HapticManager
    {
        private const float default_slider_intensity = 0.25f;
        private const float default_slider_sharpness = 0.1f;

        /// <summary>
        /// Whether the current platform supports haptics. This is not a check for whether haptics are enabled, just whether they are supported.
        /// </summary>
        public virtual bool SupportsHaptics => false;

        protected Bindable<bool> HapticsEnabled { get; set; }

        // I tried using dependency injection for FrameworkConfigManager, but
        // 1. this class isn't a drawable, so DI is largely unsupported.
        // 2. due to the binding being done in the constructor, even if DI was possible, this would run before DI could inject the dependency, resulting in a null reference.
        // So maybe this is the right approach? I dunno. Hopefully future me can figure this out.
        protected HapticManager(FrameworkConfigManager config)
        {
            HapticsEnabled = config.GetBindable<bool>(FrameworkSetting.HapticsEnabled);

            HapticsEnabled.BindValueChanged(e =>
            {
                if (e.NewValue)
                    Initialize();
                else
                    Disable();
            }, true);
        }

        /// <summary>
        /// Initializes the haptic manager, setting up any necessary resources.
        /// </summary>
        protected virtual void Initialize()
        {
            Logger.Log("[Haptic] Initializing Haptic Manager");
        }

        /// <summary>
        /// Disables the haptic manager, pausing haptic feedback but remaining ready for re-initialization.
        /// Use <see cref="Initialize"/> to re-enable. Use Dispose to permanently disable and clean up resources.
        /// </summary>
        protected virtual void Disable()
        {
            Logger.Log("[Haptic] Disabled Haptic Manager");
        }

        /// <summary>
        /// Prepares the iOS Taptic Engine to play a haptic event.
        /// Calling this method before playing a haptic event puts the Taptic Engine in a ready state, reducing latency when the event is played.
        /// This has no effect if called immediately before playing a haptic event, so it is recommended to call this method slightly in advance of when the haptic is expected to be played.
        /// This only has effect on iOS devices when using the helper methods.
        /// </summary>
        [SupportedOSPlatform("ios")]
        public virtual void PrepareHaptics()
        {
            Logger.Log("[Haptic] Prepared Haptics");
        }

        /// <summary>
        /// Plays a transient (temporary) haptic event with the specified intensity and sharpness.
        /// </summary>
        /// <param name="intensity">The overall intensity (faintness) of the haptic</param>
        /// <param name="sharpness"></param>
        public virtual void PlayTransient(float intensity, float sharpness)
        {
            Logger.Log($"[Haptic] Played Transient (i {intensity} s {sharpness})");

            if (!HapticsEnabled.Value)
                return;

            Debug.Assert(intensity is >= 0.0f and <= 1.0f);
            Debug.Assert(sharpness is >= 0.0f and <= 1.0f);
        }

        /// <summary>
        /// Helper to start a slider haptic effect. Ensures intensity and sharpness are correctly set.
        /// </summary>
        public virtual void StartContinuous(float intensity = default_slider_intensity, float sharpness = default_slider_sharpness)
        {
            Logger.Log($"[Haptic] Continuous Start (i {intensity} s {sharpness})");

            Debug.Assert(intensity is >= 0.0f and <= 1.0f);
            Debug.Assert(sharpness is >= 0.0f and <= 1.0f);

            if (!HapticsEnabled.Value)
                return;

            UpdateIntensity(intensity);
            UpdateSharpness(sharpness);
        }

        public virtual void ReleaseContinuous()
        {
            Logger.Log("[Haptic] Releasing continuous haptics");

            UpdateIntensity(0.0f);
        }

        public virtual void UpdateIntensity(float intensity, bool force = false)
        {
            Logger.Log($"[Haptic] Intensity Update (i {intensity})");

            if (!HapticsEnabled.Value)
                return;

            Debug.Assert(intensity is >= 0.0f and <= 1.0f);
        }

        /// <summary>
        /// Update the sharpness of the ongoing continuous haptic effect.
        /// </summary>
        /// <param name="sharpness">The sharpness value to update to</param>
        /// <param name="force">
        /// By default, this method doesn't update sharpness if the stored value and new value are the same.
        /// Use this parameter to forcefully send the update sharpness request of the same value to the engine
        /// </param>
        public virtual void UpdateSharpness(float sharpness, bool force = false)
        {
            Logger.Log($"[Haptic] Sharpness Update: {sharpness}");

            if (!HapticsEnabled.Value)
                return;

            Debug.Assert(sharpness is >= 0.0f and <= 1.0f);
        }

        /// <summary>
        /// Generic button press haptic feedback.
        /// </summary>
        public virtual void ButtonPress()
        {
            Logger.Log("[Haptic] Button Press");
            PlayTransient(1.0f, 1.0f);
        }

        public virtual void SelectionChanged()
        {
            Logger.Log("[Haptic] Selection Changed");
            PlayTransient(0.4f, 0.4f);
        }

        public virtual void SuccessNotification()
        {
            Logger.Log("[Haptic] Success Notification");
        }

        public virtual void WarningNotification()
        {
            Logger.Log("[Haptic] Warning Notification");
        }

        public virtual void ErrorNotification()
        {
            Logger.Log("[Haptic] Error Notification");
        }

        public virtual void ToggleOn()
        {
            Logger.Log("[Haptic] Toggle On");
            PlayTransient(1.0f, 0.8f);
        }

        public virtual void ToggleOff()
        {
            Logger.Log("[Haptic] Toggle Off");
            PlayTransient(1.0f, 0.4f);
        }

        /// <summary>
        /// Plays a crash haptic effect, a strong, sharp transient, and a rumble fading out.
        /// Useful for signifying beginnings or ends of significant events, such as map start, or map fail.
        /// Also, useful if you want to extend the default button press haptic to something more significant.
        /// </summary>
        /// <param name="intensity"></param>
        /// <param name="sharpness"></param>
        /// <param name="durationSeconds"></param>
        public virtual void Crash(float intensity = 1, float sharpness = 1, float durationSeconds = 1)
        {
            Logger.Log($"[Haptic] Played HitAndFade ({durationSeconds}s)");

            Debug.Assert(intensity is >= 0.0f and <= 1.0f);
            Debug.Assert(sharpness is >= 0.0f and <= 1.0f);
            Debug.Assert(durationSeconds >= 0);
        }

        public virtual void Buzz()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Takes a value from 0-100 and clamps it to the range 0.0-1.0 as a float, optionally scaling it to the desired scale.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="scale"></param>
        /// <returns></returns>
        public static float ClampToUnit(double value, float scale = 1.0f) => (float)(value / 100.0f * scale);
    }
}
