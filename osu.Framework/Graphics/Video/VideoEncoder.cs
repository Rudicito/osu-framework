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
    /// Heavily based of https://github.com/FFmpeg/FFmpeg/blob/release/4.3/doc/examples/muxing.c
    /// </remarks>
    public unsafe class VideoEncoder : FFmpegComponent, IDisposable
    {
        public const double STREAM_DURATION = 10.0;
        public const int STREAM_FRAME_RATE = 25; // 25 frame/s
        public const AVPixelFormat STREAM_PIX_FMT = AVPixelFormat.AV_PIX_FMT_YUV420P; // default pix_fmt

        private AVFormatContext* oc = null;
        private OutputStream videoStream;
        private OutputStream audioStream;

        public const double SCALE_FLAGS = FFmpegFuncs.SWS_BICUBIC;

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

        // Add an output stream.
        private void addStream(OutputStream* ost, AVFormatContext* oc, AVCodec** codec, AVCodecID codecID)
        {
            // find the encoder
            *codec = Ffmpeg.avcodec_find_encoder(codecID);

            if (*codec == null)
            {
                throw new InvalidOperationException($"Could not find encoder for {codecID}");
                    // avcodec_get_name(codecID));
            }

            ost->St = Ffmpeg.avformat_new_stream(oc, null);

            if (ost->St == null)
                throw new InvalidOperationException("Could not allocate stream");

            ost->St->id = (int)(oc->nb_streams - 1);
            var c = Ffmpeg.avcodec_alloc_context3(*codec);

            if (c == null)
                throw new InvalidOperationException("Could not alloc an encoding context");

            ost->Enc = c;

            switch ((*codec)->type)
            {
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    c->sample_fmt = (*codec)->sample_fmts != null
                        ? (*codec)->sample_fmts[0]
                        : AVSampleFormat.AV_SAMPLE_FMT_FLTP;

                    c->bit_rate = 64000;
                    c->sample_rate = 44100;

                    int i;

                    if ((*codec)->supported_samplerates != null)
                    {
                        c->sample_rate = (*codec)->supported_samplerates[0];

                        for (i = 0; (*codec)->supported_samplerates[i] != 0; i++)
                        {
                            if ((*codec)->supported_samplerates[i] == 44100)
                                c->sample_rate = 44100;
                        }
                    }

                    c->channels = Ffmpeg.av_get_channel_layout_nb_channels(c->channel_layout);
                    c->channel_layout = FFmpegFuncs.AV_CH_LAYOUT_STEREO;

                    if ((*codec)->channel_layouts != null)
                    {
                        c->channel_layout = (*codec)->channel_layouts[0];

                        for (i = 0; (*codec)->channel_layouts[i] != 0; i++)
                        {
                            if ((*codec)->channel_layouts[i] == FFmpegFuncs.AV_CH_LAYOUT_STEREO)
                                c->channel_layout = FFmpegFuncs.AV_CH_LAYOUT_STEREO;
                        }
                    }

                    c->channels = Ffmpeg.av_get_channel_layout_nb_channels(c->channel_layout);
                    ost->St->time_base = new AVRational { den = 1, num = c->sample_rate };
                    break;

                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    c->codec_id = codecID;

                    c->bit_rate = 400000;
                    // Resolution must be a multiple of two.
                    c->width = 352;
                    c->height = 288;
                    // timebase: This is the fundamental unit of time (in seconds) in terms
                    // of which frame timestamps are represented. For fixed-fps content,
                    // timebase should be 1/framerate and timestamp increments should be
                    // identical to 1.
                    ost->St->time_base = new AVRational { den = 1, num = STREAM_FRAME_RATE };
                    c->time_base = ost->St->time_base;

                    c->gop_size = 12; // emit one intra frame every twelve frames at most
                    c->pix_fmt = STREAM_PIX_FMT;

                    if (c->codec_id == AV_CODEC_ID_MPEG2VIDEO)
                    {
                        // just for testing, we also add B-frames
                        c->max_b_frames = 2;
                    }

                    if (c->codec_id == AV_CODEC_ID_MPEG1VIDEO) {
                        // Needed to avoid using macroblocks in which some coeffs overflow.
                        // This does not happen with normal video, it just happens here as
                        // the motion of the chroma plane does not match the luma plane.
                        c->mb_decision = 2;
                    }

                    break;

                default:
                    break;
            }

            // Some formats want stream headers to be separate.
            if ((oc->oformat->flags & FFmpegFuncs.AVFMT_GLOBALHEADER) != 0)
                c->flags |= FFmpegFuncs.AV_CODEC_FLAG_GLOBAL_HEADER;
        }

        #region Audio

        private AVFrame* allocAudioFrame(AVSampleFormat sampleFmt, ulong channelLayout, int sampleRate, int nbSamples)
        {
            AVFrame* frame = Ffmpeg.av_frame_alloc();

            if (frame == null)
                throw new InvalidOperationException("Error allocating an audio frame");

            frame->format = (int)sampleFmt;
            frame->channel_layout = channelLayout;
            frame->sample_rate = sampleRate;
            frame->nb_samples = nbSamples;

            if (nbSamples != 0)
            {
                int ret = Ffmpeg.av_frame_get_buffer(frame, 0);
                if (ret < 0)
                    throw new InvalidOperationException("Error allocating an audio buffer");
            }

            return frame;
        }

        private void openAudio(AVFormatContext* oc, AVCodec* codec, OutputStream* ost, AVDictionary* optArg)
        {
            AVDictionary* opt = null;

            var c = ost->Enc;

            // open it
            Ffmpeg.av_dict_copy(&opt, optArg, 0);
            int ret = Ffmpeg.avcodec_open2(c, codec, &opt);
            Ffmpeg.av_dict_free?.Invoke(&opt);

            if (ret < 0)
                throw new InvalidOperationException($"Could not open audio codec: {GetErrorMessage(ret)}");

            // init signal generator
            ost->T = 0;
            ost->Tincr = (float)(2 * Math.PI * 110.0 / c->sample_rate);
            // increment frequency by 110 Hz per second
            ost->Tincr2 = (float)(2 * Math.PI * 110.0 / c->sample_rate / c->sample_rate);

            int nbSamples = (c->codec->capabilities & FFmpegFuncs.AV_CODEC_CAP_VARIABLE_FRAME_SIZE) != 0 ? 10000 : c->frame_size;

            ost->Frame = Ffmpeg.alloc_audio_frame(c->sample_fmt, c->channel_layout, c->sample_rate, nbSamples);
            ost->TmpFrame = Ffmpeg.alloc_audio_frame(AVSampleFormat.AV_SAMPLE_FMT_S16, c->channel_layout, c->sample_rate, nbSamples);

            // copy the stream parameters to the muxer
            ret = Ffmpeg.avcodec_parameters_from_context(ost->St->codecpar, c);

            if (ret < 0)
                throw new InvalidOperationException("Could not copy the stream parameters");

            // create resampler context
            ost->SwrCtx = Ffmpeg.swr_alloc();

            if (ost->SwrCtx == null)
                throw new InvalidOperationException("Could not allocate resampler context\n");

            // set options
            Ffmpeg.av_opt_set_int(ost->SwrCtx, "in_channel_count", c->channels, 0);
            Ffmpeg.av_opt_set_int(ost->SwrCtx, "in_sample_rate", c->sample_rate, 0);
            Ffmpeg.av_opt_set_sample_fmt(ost->SwrCtx, "in_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_S16, 0);
            Ffmpeg.av_opt_set_int(ost->SwrCtx, "out_channel_count", c->channels, 0);
            Ffmpeg.av_opt_set_int(ost->SwrCtx, "out_sample_rate", c->sample_rate, 0);
            Ffmpeg.av_opt_set_sample_fmt(ost->SwrCtx, "out_sample_fmt", c->sample_fmt, 0);

            // initialize the resampling context
            if (Ffmpeg.swr_init(ost->SwrCtx) < 0)
                throw new InvalidOperationException("Failed to initialize the resampling context");
        }

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

        //todo: should use fixed()?
        private void closeStream(AVFormatContext* oc, OutputStream* ost)
        {
            Ffmpeg.avcodec_free_context(&ost->Enc);
            Ffmpeg.av_frame_free(&ost->Frame);
            Ffmpeg.av_frame_free(&ost->TmpFrame);
            Ffmpeg.sws_freeContext(ost->SwsCtx);
            Ffmpeg.swr_free(&ost->SwrCtx);
            //todo: don't forget TmpPkt
        }

        // sws_scale for colour changes

        public void StartRecord(string filename)
        {
            fixed (AVFormatContext** ocPtr = &oc)
            {
                int ret = Ffmpeg.avformat_alloc_output_context2(ocPtr, null, "mp4", filename);
                if (ret < 0 || oc == null)
                    throw new InvalidOperationException($"Could not create output context: {GetErrorMessage(ret)}");
            }

            var fmt = oc->oformat;

            // Add the audio and video streams using the default format codecs
            // and initialize the codecs.
            if (fmt->video_codec != AVCodecID.AV_CODEC_ID_NONE)
            {
                addStream(&videoStream, oc, &video_codec, fmt->video_codec);
                have_video = 1;
                encode_video = 1;
            }

            if (fmt->audio_codec != AV_CODEC_ID_NONE) {
                add_stream(&audio_st, oc, &audio_codec, fmt->audio_codec);
                have_audio = 1;
                encode_audio = 1;
            }

            /* Now that all the parameters are set, we can open the audio and
             * video codecs and allocate the necessary encode buffers. */
            if (have_video)
                open_video(oc, video_codec, &video_st, opt);

            if (have_audio)
                open_audio(oc, audio_codec, &audio_st, opt);
        }

        public void FinishRecord()
        {
            // Write the trailer, if any. The trailer must be written before you
            // close the CodecContexts open when you wrote the header; otherwise
            // av_write_trailer() may try to use memory that was freed on
            // av_codec_close().
            Ffmpeg.av_write_trailer(oc);

            // Close each codec.
            if (have_video)
            {
                fixed (OutputStream* st = &videoStream)
                    closeStream(oc, st);
            }

            if (have_audio)
            {
                fixed (OutputStream* st = &audioStream)
                    closeStream(oc, st);
            }

            if (!(fmt->flags & AVFMT_NOFILE))
                // Close the output file.
                avio_closep(&oc->pb);

            // free the stream
            Ffmpeg.avformat_free_context(oc);
        }

        public void Dispose()
        {
            // TODO release managed resources here
        }
    }
}
