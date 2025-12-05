// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Configuration;

namespace osu.Framework.Input
{
    public class DefaultHapticManager : HapticManager
    {
        public DefaultHapticManager(FrameworkConfigManager config)
            : base(config) { }
    }
}
