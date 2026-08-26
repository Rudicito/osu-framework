// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Development;
using osu.Framework.Platform;
using osu.Framework.Threading;

namespace osu.Framework.Render
{
    public class RenderThreadRunner : ThreadRunner
    {
        public RenderThreadRunner(InputThread mainThread, RenderFramerate renderFramerate)
            : base(mainThread)
        {
            this.renderFramerate = renderFramerate;
        }

        // private double incrementedTime = 1000 / 600;
        private readonly RenderFramerate renderFramerate;
        private ulong nbFrame;

        /// <summary>
        /// Number of frames we wait before calling the DrawThread.
        /// Skipping DrawThread speed up the render.
        /// Based of the incremetedTime of the RenderClock.
        /// For example: for 60 fps, since the incremented time is 1000 / 60 / 10 between frames,
        /// We juste need to wait 10 frame before calling the DrawThread, for making 60fps render.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private int drawThreadDelay
        {
            get
            {
                switch (renderFramerate)
                {
                    case RenderFramerate.Fps30:
                        return 20;

                    case RenderFramerate.Fps60:
                        return 10;

                    case RenderFramerate.Fps120:
                        return 5;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public override void RunMainLoop()
        {
            ExecutionMode = ExecutionMode.SingleThread;

            lock (InternalThreads)
            {
                foreach (var t in InternalThreads)
                {
                    if (t is not DrawThread || nbFrame % (ulong)drawThreadDelay == 0)
                    {
                        t.RunSingleFrame();
                    }
                }

                nbFrame += 1;

                ThreadSafety.ResetAllForCurrentThread();
            }
        }
    }
}
