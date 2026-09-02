// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using FFmpeg.AutoGen;
using osu.Framework.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Size = System.Drawing.Size;

namespace osu.Framework.Graphics.Video
{
    //todo: need to build encoders and muxer, see : https://github.com/ppy/osu-framework/issues/5974
    // likely:
    // - encoders: libx264 (video), aac (audio)
    // - muxer: mp4
    /// <remarks>
    /// Heavily based on https://github.com/FFmpeg/FFmpeg/blob/release/4.3/doc/examples/muxing.c.
    /// Only works in single thread in mind!
    /// </remarks>
    public unsafe class VideoEncoder : FFmpegComponent, IDisposable
    {
        public int VideoFrameRate { get; init; } = 60;
        public Size VideoSize { get; init; } = new Size(1920, 1080);

        public int AudioBitRate { get; init; } = 192000;
        public int AudioSampleRate { get; init; } = 44100;

        private AVOutputFormat* fmt;
        private AVFormatContext* oc = null;

        private OutputStream videoStream;
        private OutputStream audioStream;

        private AVCodec* videoCodec;
        private AVCodec* audioCodec;

        private bool haveVideo;
        private bool haveAudio;

        public const int SCALE_FLAGS = FFmpegFuncs.SWS_BICUBIC;

        public EncoderState State { get; private set; } = EncoderState.Idle;
        private byte[]? pixelBuffer;

        private struct OutputStream
        {
            public AVStream* St;
            public AVCodecContext* Enc;
            public long NextPts;
            public int SamplesCount;
            public AVFrame* Frame;
            public AVFrame* TmpFrame;
            public AVPacket* TmpPkt;
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

            ost->TmpPkt = Ffmpeg.av_packet_alloc();

            ost->Enc = c;

            switch ((*codec)->type)
            {
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    c->sample_fmt = (*codec)->sample_fmts != null
                        ? (*codec)->sample_fmts[0]
                        : AVSampleFormat.AV_SAMPLE_FMT_FLTP;

                    c->bit_rate = AudioBitRate;
                    c->sample_rate = AudioSampleRate;

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
                    ost->St->time_base = new AVRational { num = 1, den = c->sample_rate };
                    break;

                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    c->codec_id = codecID;
                    c->width = VideoSize.Width;
                    c->height = VideoSize.Height;
                    // timebase: This is the fundamental unit of time (in seconds) in terms
                    // of which frame timestamps are represented. For fixed-fps content,
                    // timebase should be 1/framerate and timestamp increments should be
                    // identical to 1.
                    ost->St->time_base = new AVRational { num = 1, den = VideoFrameRate };
                    c->time_base = ost->St->time_base;
                    c->gop_size = 12; // emit one intra frame every twelve frames at most
                    c->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;

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

            int nbSamples = (c->codec->capabilities & FFmpegFuncs.AV_CODEC_CAP_VARIABLE_FRAME_SIZE) != 0 ? 10000 : c->frame_size;

            ost->Frame = allocAudioFrame(c->sample_fmt, c->channel_layout, c->sample_rate, nbSamples);
            ost->TmpFrame = allocAudioFrame(AVSampleFormat.AV_SAMPLE_FMT_FLT, c->channel_layout, c->sample_rate, nbSamples);

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
            Ffmpeg.av_opt_set_sample_fmt(ost->SwrCtx, "in_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_FLT, 0);
            Ffmpeg.av_opt_set_int(ost->SwrCtx, "out_channel_count", c->channels, 0);
            Ffmpeg.av_opt_set_int(ost->SwrCtx, "out_sample_rate", c->sample_rate, 0);
            Ffmpeg.av_opt_set_sample_fmt(ost->SwrCtx, "out_sample_fmt", c->sample_fmt, 0);

            // initialize the resampling context
            if (Ffmpeg.swr_init(ost->SwrCtx) < 0)
                throw new InvalidOperationException("Failed to initialize the resampling context");
        }

        public void SendAudioFrame(float[] audioData)
        {
            writeFrame(oc, audioStream.Enc, audioStream.St, getAudioFrame(audioData), audioStream.TmpPkt);
        }

        private AVFrame* getAudioFrame(float[] audioData)
        {
            if (Ffmpeg.av_frame_make_writable(audioStream.TmpFrame) < 0)
                throw new InvalidOperationException("Audio temp frame is not writable");

            int expectedSamples = audioStream.TmpFrame->nb_samples * audioStream.Enc->channels;
            if (audioData.Length != expectedSamples)
                throw new ArgumentException($"Expected {expectedSamples} samples, got {audioData.Length}");

            fixed (float* src = audioData)
            {
                Buffer.MemoryCopy(
                    src,
                    audioStream.TmpFrame->data[0],
                    audioData.Length * sizeof(float),
                    audioData.Length * sizeof(float)
                );
            }

            if (Ffmpeg.av_frame_make_writable(audioStream.Frame) < 0)
                throw new InvalidOperationException("Audio frame is not writable");

            int ret = Ffmpeg.swr_convert(
                audioStream.SwrCtx,
                audioStream.Frame->extended_data,
                audioStream.Frame->nb_samples,
                audioStream.TmpFrame->extended_data,
                audioStream.TmpFrame->nb_samples
            );

            if (ret < 0)
                throw new InvalidOperationException("Could not convert the audio frame");

            audioStream.Frame->pts = Ffmpeg.av_rescale_q(audioStream.SamplesCount, new AVRational { num = 1, den = audioStream.Enc->sample_rate }, audioStream.Enc->time_base);
            audioStream.SamplesCount += audioStream.Frame->nb_samples;
            return audioStream.Frame;
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

            // copy the stream parameters to the muxer
            ret = Ffmpeg.avcodec_parameters_from_context(ost->St->codecpar, c);

            if (ret < 0)
                throw new InvalidOperationException("Could not copy the stream parameters");

            ost->SwsCtx = Ffmpeg.sws_getContext(c->width, c->height,
                AVPixelFormat.AV_PIX_FMT_RGBA,
                c->width, c->height,
                c->pix_fmt,
                SCALE_FLAGS, null, null, null);
        }

        public void SendVideoFrame(Image<Rgba32> image)
        {
            writeFrame(oc, videoStream.Enc, videoStream.St, getVideoFrame(image), videoStream.TmpPkt);
        }

        private AVFrame* getVideoFrame(Image<Rgba32> img)
        {
            if (Ffmpeg.av_frame_make_writable(videoStream.Frame) < 0)
                throw new InvalidOperationException("Video frame is not writable");

            if (pixelBuffer == null)
            {
                int totalBytes = videoStream.Enc->width * videoStream.Enc->height * 4;
                pixelBuffer = new byte[totalBytes];
            }

            img.CopyPixelDataTo(pixelBuffer);

            fixed (byte* p = pixelBuffer)
            {
                byte*[] srcSlice = [p, null, null, null];
                int[] srcStride = [videoStream.Enc->width * 4, 0, 0, 0];
                Ffmpeg.sws_scale(videoStream.SwsCtx,
                    srcSlice,
                    srcStride,
                    0,
                    videoStream.Enc->height,
                    videoStream.Frame->data,
                    videoStream.Frame->linesize
                );
            }

            videoStream.Frame->pts = videoStream.NextPts++;

            return videoStream.Frame;
        }

        #endregion

        private void closeStream(AVFormatContext* oc, OutputStream* ost)
        {
            Ffmpeg.avcodec_free_context(&ost->Enc);
            Ffmpeg.av_frame_free(&ost->Frame);
            Ffmpeg.av_frame_free(&ost->TmpFrame);
            Ffmpeg.sws_freeContext(ost->SwsCtx);
            Ffmpeg.swr_free(&ost->SwrCtx);
            Ffmpeg.av_packet_free(&ost->TmpPkt);
        }

        // sws_scale for colour changes

        private void prepareRecording(string filename)
        {
            int ret;
            AVDictionary* opt = null;

            fixed (AVFormatContext** ocPtr = &oc)
            {
                ret = Ffmpeg.avformat_alloc_output_context2(ocPtr, null, "mp4", filename);
                if (ret < 0 || oc == null)
                    throw new InvalidOperationException($"Could not create output context: {GetErrorMessage(ret)}");
            }

            fmt = oc->oformat;

            // Add the audio and video streams using the default format codecs
            // and initialize the codecs.
            if (fmt->video_codec != AVCodecID.AV_CODEC_ID_NONE)
            {
                fixed (OutputStream* vst = &videoStream)
                fixed (AVCodec** vco = &videoCodec)
                    addStream(vst, oc, vco, fmt->video_codec);
                haveVideo = true;
            }

            if (fmt->audio_codec != AVCodecID.AV_CODEC_ID_NONE)
            {
                fixed (OutputStream* ast = &audioStream)
                fixed (AVCodec** aco = &audioCodec)
                    addStream(ast, oc, aco, fmt->audio_codec);
                haveAudio = true;
            }

            // Now that all the parameters are set, we can open the audio and
            // video codecs and allocate the necessary encode buffers.
            if (haveVideo)
            {
                fixed (OutputStream* vst = &videoStream)
                    openVideo(oc, videoCodec, vst, opt);

                haveVideo = false;
            }

            if (haveAudio)
            {
                fixed (OutputStream* ast = &audioStream)
                    openAudio(oc, audioCodec, ast, opt);

                haveAudio = false;
            }

            if ((fmt->flags & FFmpegFuncs.AVFMT_NOFILE) == 0)
            {
                ret = Ffmpeg.avio_open(&oc->pb, filename, FFmpegFuncs.AVIO_FLAG_WRITE);

                if (ret < 0)
                    throw new InvalidOperationException($"Could not open {filename} : {GetErrorMessage(ret)}");
            }

            // Write the stream header, if any.
            ret = Ffmpeg.avformat_write_header(oc, &opt);

            if (ret < 0)
                throw new InvalidOperationException($"Error occurred when opening output file: {GetErrorMessage(ret)}");
        }

        public void StartRecording(string filename)
        {
            if (State == EncoderState.Running)
                return;

            try
            {
                prepareRecording(filename);
                State = EncoderState.Running;
            }
            catch (Exception e)
            {
                State = EncoderState.Idle;
                Logger.Log(e.Message);
                Dispose();
                throw;
            }
        }

        // private void addSchedule()
        // {
        //     if (drawDelegate != null) return;
        //
        //     drawDelegate = host.DrawThread.Scheduler.AddDelayed(() =>
        //     {
        //         var image = renderer.TakeScreenshot();
        //         SendVideoFrame(image);
        //
        //         //todo: call writeAudioFrame here when ready
        //     }, 0, true);
        // }

        public void StopRecording()
        {
            if (State != EncoderState.Running)
                return;

            if (haveVideo)
            {
                fixed (OutputStream* vst = &videoStream)
                    writeFrame(oc, vst->Enc, vst->St, null, vst->TmpPkt); // flush vidéo
            }

            if (haveAudio)
            {
                fixed (OutputStream* ast = &audioStream)
                    writeFrame(oc, ast->Enc, ast->St, null, ast->TmpPkt); // flush audio
            }

            // Write the trailer, if any. The trailer must be written before you
            // close the CodecContexts open when you wrote the header; otherwise
            // av_write_trailer() may try to use memory that was freed on
            // av_codec_close().
            Ffmpeg.av_write_trailer(oc);

            // Close each codec.
            if (haveVideo)
            {
                fixed (OutputStream* st = &videoStream)
                    closeStream(oc, st);
            }

            if (haveAudio)
            {
                fixed (OutputStream* st = &audioStream)
                    closeStream(oc, st);
            }

            if ((fmt->flags & FFmpegFuncs.AVFMT_NOFILE) == 0)
                // Close the output file.
                Ffmpeg.avio_closep(&oc->pb);

            // free the stream
            Ffmpeg.avformat_free_context(oc);
            oc = null;

            State = EncoderState.Idle;
        }

        public void Dispose()
        {
            if (State == EncoderState.Running)
            {
                StopRecording();
                return;
            }

            if (haveAudio)
            {
                fixed (OutputStream* st = &audioStream)
                    closeStream(oc, st);
            }

            if (haveVideo)
            {
                fixed (OutputStream* st = &videoStream)
                    closeStream(oc, st);
            }

            fmt = null;

            if (oc != null)
            {
                Ffmpeg.avformat_free_context(oc);
                oc = null;
            }
        }

        public enum EncoderState
        {
            Running,
            Idle,
        }
    }
}
