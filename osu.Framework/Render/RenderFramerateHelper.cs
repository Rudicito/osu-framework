// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Framework.Render
{
    public static class RenderFramerateHelper
    {
        public static int GetIntFramerate(RenderFramerate renderFramerate)
        {
            switch (renderFramerate)
            {
                case RenderFramerate.Fps15:
                    return 15;

                case RenderFramerate.Fps30:
                    return 30;

                case RenderFramerate.Fps60:
                    return 60;

                case RenderFramerate.Fps120:
                    return 120;

                default:
                    throw new ArgumentOutOfRangeException(nameof(renderFramerate), renderFramerate, null);
            }
        }
    }
}
