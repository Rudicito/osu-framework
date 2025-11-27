// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Framework.Input
{
    public interface IHapticHandler
    {
        const float DEFAULT_SLIDER_INTENSITY = 0.3f;
        const float DEFAULT_SLIDER_SHARPNESS = 0.1f;

        /// <summary>
        /// Plays a transient (temporary) haptic event with the specified intensity and sharpness.
        /// </summary>
        /// <param name="intensity">The overall intensity (faintness) of the haptic</param>
        /// <param name="sharpness"></param>
        void PlayTransient(float intensity, float sharpness);

        /// <summary>
        /// Generic button press haptic feedback.
        /// </summary>
        void PlayButtonPress();

        /// <summary>
        /// Helper to start a slider haptic effect. Ensures intensity and sharpness are correctly set.
        /// </summary>
        void StartSlider(float intensity = DEFAULT_SLIDER_INTENSITY, float sharpness = DEFAULT_SLIDER_SHARPNESS);

        /// <summary>
        /// Helper to stop a slider haptic effect. Stops haptics and plays a transient to indicate the end.
        /// </summary>
        void StopSlider();

        /// <summary>
        /// Creates a continuous player for haptics. Should be called at the start of each Player session.
        /// </summary>
        void CreateContinuousPlayer(float defaultIntensity, float defaultSharpness);

        void UpdateIntensity(float intensity);

        void UpdateSharpness(float sharpness);

        void ReleaseAll();
    }
}
