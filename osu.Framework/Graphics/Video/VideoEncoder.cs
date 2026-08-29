// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using FFmpeg.AutoGen;
using osu.Framework.Logging;

namespace osu.Framework.Graphics.Video
{
    //todo: need to build encoders and muxer, see : https://github.com/ppy/osu-framework/issues/5974
    // likely:
    // - encoders: libx264 (video), aac (audio)
    // - muxer: mp4
    /// <remarks>
    /// Heavily based of https://github.com/FFmpeg/FFmpeg/blob/df48dc624e7103ae99c59cb9d744cd17d317ec4d/doc/examples/mux.c
    /// </remarks>
    public unsafe class VideoEncoder : FFmpegComponent, IDisposable
    {
        public struct OutputStream
        {
            public AVStream* St;
            public AVCodecContext* Enc;
            public long NextPts;
            public int SamplesCount;
            public AVFrame* Frame;
            public AVFrame* TmpFrame;
            public AVPacket* TmpPkt;
            public float T, Tincr, Tincr2;
            public SwsContext* SwsCtx;
            public SwrContext* SwrCtx;
        }

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

        #region Audio

        // private AVFrame* allocAudioFrame(AVSampleFormat sample_fmt, AVChannelLayout *channel_layout, int sample_rate, int nb_samples)
        // {
        // }

        #endregion

        #region Video

        private AVFrame* allocVideoFrame(AVPixelFormat pixFmt, int width, int height)
        {
            var frame = Ffmpeg.av_frame_alloc();
            if (frame == null)
                return null;

            frame->format = (int)pixFmt;
            frame->width = width;
            frame->height = height;

            // allocate the buffers for the frame data
            int ret = Ffmpeg.av_frame_get_buffer(frame, 0);

            if (ret < 0)
            {
                Ffmpeg.av_frame_free(&frame);
                throw new InvalidOperationException("Could not allocate frame data.");
            }

            return frame;
        }

        private void openVideo(AVFormatContext* oc, AVCodec* codec, OutputStream* ost, AVDictionary* optArg)
        {
            AVCodecContext* c = ost->Enc;
            AVDictionary* opt = null;

            Ffmpeg.av_dict_copy(&opt, optArg, 0);

            // open the codec
            int ret = Ffmpeg.avcodec_open2(c, codec, &opt);
            Ffmpeg.av_dict_free?.Invoke(&opt);

            if (ret < 0)
                throw new InvalidOperationException($"Could not open video codec: {GetErrorMessage(ret)}");

            // allocate and init a reusable frame
            ost->Frame = allocVideoFrame(c->pix_fmt, c->width, c->height);

            if (ost->Frame == null)
                throw new InvalidOperationException("Could not allocate video frame");

            // If the output format is not YUV420P, then a temporary YUV420P
            // picture is needed too. It is then converted to the required
            // output format.
            ost->TmpFrame = null;

            if (c->pix_fmt != AVPixelFormat.AV_PIX_FMT_YUV420P)
            {
                ost->TmpFrame = allocVideoFrame(AVPixelFormat.AV_PIX_FMT_YUV420P, c->width, c->height);

                if (ost->TmpFrame == null)
                    //todo: free ost->Frame?
                    throw new InvalidOperationException("Could not allocate temporary video frame\n");
            }

            /* copy the stream parameters to the muxer */
            ret = Ffmpeg.avcodec_parameters_from_context(ost->St->codecpar, c);

            if (ret < 0)
                throw new InvalidOperationException("Could not copy the stream parameters");
        }

        #endregion

        // sws_scale for colour changes

        public void Dispose()
        {
            // TODO release managed resources here
        }
    }
}
