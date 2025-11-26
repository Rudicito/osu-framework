// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Framework.Input
{
    public interface IHapticHandler
    {
        void PlayButtonPress();

        /// <summary>
        /// Plays a transient haptic event with the specified intensity and sharpness.
        /// </summary>
        /// <param name="intensity">The overall intensity (faintness) of the haptic</param>
        /// <param name="sharpness"></param>
        void PlayTransient(float intensity, float sharpness);

        void StartSlider();

        void StopSlider();

        void ReleaseAll();
    }
}
