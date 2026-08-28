// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using FFmpeg.AutoGen;
using osu.Framework.Logging;

namespace osu.Framework.Graphics.Video
{
    public unsafe class VideoEncoder : FFmpegComponent, IDisposable
    {
        private AVStream* st;
        private AVCodecContext* enc;

        /* pts of the next frame that will be generated */
        long next_pts;
        private int samples_count;

        private AVFrame* frame;
        private AVFrame* tmp_frame;

        private AVPacket* tmpPkt;

        private float t, tincr, tincr2;

        SwsContext* sws_ctx;
        SwrContext* swr_ctx;

        private bool writeFrame(AVFormatContext* fmtCtx, AVCodecContext* c, AVStream* st, AVFrame* frame, AVPacket* pkt)
        {
            // send the frame to the decoder
            int ret = Ffmpeg.avcodec_send_frame(c, frame);

            if (ret < 0)
            {
                // Error sending a frame to the encoder: %s
                Logger.Log($"Error sending a frame to the encoder: {GetErrorMessage(ret)}");
                return false;
            }

            while (true)
            {
                ret = Ffmpeg.avcodec_receive_packet(c, pkt);

                if (ret == -FFmpegFuncs.EAGAIN || ret == FFmpegFuncs.AVERROR_EOF)
                    break;
                else if (ret < 0)
                {
                    Logger.Log($"Error encoding a frame: {GetErrorMessage(ret)}");
                    return false;
                }

                // rescale output packet timestamp values from codec to stream timebase
                Ffmpeg.av_packet_rescale_ts(pkt, c->time_base, st->time_base);
                pkt->stream_index = st->index;

                // Write the compressed frame to the media file.
                ret = Ffmpeg.av_interleaved_write_frame(fmtCtx, pkt);

                // pkt is now blank (av_interleaved_write_frame() takes ownership of
                // its contents and resets pkt), so that no unreferencing is necessary.
                // This would be different if one used av_write_frame().
                if (ret < 0)
                {
                    Logger.Log($"Error while writing output packet: {GetErrorMessage(ret)}");
                    return false;
                }
            }

            return ret == FFmpegFuncs.AVERROR_EOF;
        }

        private void add_stream(OutputStre)
        {

        }

        // sws_scale for colour changes

        public void Dispose()
        {
            // TODO release managed resources here
        }
    }
}
