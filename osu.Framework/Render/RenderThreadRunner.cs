// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Development;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Video;
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
            encoder = new VideoEncoder
            {
                AudioEnable = false, //todo: add audio
                VideoFrameRate = RenderFramerateHelper.GetIntFramerate(renderFramerate)
            };
        }

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        private readonly VideoEncoder encoder;
        private bool wait;

        private readonly RenderFramerate renderFramerate;
        private ulong nbFrame;

        /// <summary>
        /// Number of frames we wait before calling the DrawThread.
        /// Skipping DrawThread speed up the render.
        /// Based of the incrementedTime of the RenderClock.
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
                    case RenderFramerate.Fps15:
                        return 40;

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

        public void StartRecording()
        {
            wait = true;
            encoder.StartRecording("output.mp4");
        }

        public void StopRecording() => encoder.StopRecording();

        public override void RunMainLoop()
        {
            ExecutionMode = ExecutionMode.SingleThread;

            EnsureCorrectExecutionMode();

            lock (InternalThreads)
            {
                foreach (var t in InternalThreads)
                {
                    if (t is not DrawThread)
                    {
                        t.RunSingleFrame();
                    }

                    else if (nbFrame % (ulong)drawThreadDelay == 0)
                    {
                        t.RunSingleFrame();

                        if (encoder.State != VideoEncoder.EncoderState.Running) continue;

                        // Just in case, we wait for the game to do a full frame cycle (I dunno, maybe breaking audio?)
                        if (wait)
                        {
                            wait = false;
                            continue;
                        }

                        // Video
                        var image = renderer.TakeScreenshot();
                        encoder.SendVideoFrame(image);

                        //todo: do the audio
                    }
                }

                nbFrame += 1;

                ThreadSafety.ResetAllForCurrentThread();
            }
        }
    }
}
