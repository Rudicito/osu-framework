// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.iOS;
using osu.Framework.Tests.Visual;

namespace osu.Framework.Tests.Input
{
    [TestFixture]
    public partial class HapticTest : FrameworkTestScene
    {
        public HapticTest()
        {
            Children = new Drawable[]
            {
                new HapticTriggerButton
                {
                    Size = new osuTK.Vector2(200, 100),
                    Position = new osuTK.Vector2(0, 0),
                }
            };
        }

        private partial class HapticTriggerButton : Box
        {
            private readonly HapticHandler hapticHandler = new HapticHandler();

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                Schedule(() =>
                {
                    hapticHandler.PlayButtonPress();
                });
                return true;
            }

            protected override void OnMouseUp(MouseUpEvent e)
            {
                Schedule(() =>
                {
                    hapticHandler.PlayButtonRelease();
                });
            }
        }
    }
}
