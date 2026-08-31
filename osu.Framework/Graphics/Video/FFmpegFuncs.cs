// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using JetBrains.Annotations;

// ReSharper disable InconsistentNaming
#pragma warning disable IDE1006 // Naming style

namespace osu.Framework.Graphics.Video
{
    public unsafe class FFmpegFuncs
    {
        #region Delegates

        public delegate int AvDictSetDelegate(AVDictionary** pm, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int flags);

        public delegate void AvDictFreeDelegate(AVDictionary** m);

        public delegate AVFrame* AvFrameAllocDelegate();

        public delegate void AvFrameFreeDelegate(AVFrame** frame);

        public delegate void AvFrameUnrefDelegate(AVFrame* frame);

        public delegate void AvFrameMoveRefDelegate(AVFrame* dst, AVFrame* src);

        public delegate int AvFrameGetBufferDelegate(AVFrame* frame, int align);

        public delegate byte* AvStrDupDelegate(string s);

        public delegate int AvStrErrorDelegate(int errnum, byte* buffer, ulong bufSize);

        public delegate void* AvMallocDelegate(ulong size);

        public delegate void AvFreepDelegate(void* ptr);

        public delegate AVPacket* AvPacketAllocDelegate();

        public delegate void AvPacketUnrefDelegate(AVPacket* pkt);

        public delegate void AvPacketFreeDelegate(AVPacket** pkt);

        public delegate int AvReadFrameDelegate(AVFormatContext* s, AVPacket* pkt);

        public delegate int AvSeekFrameDelegate(AVFormatContext* s, int stream_index, long timestamp, int flags);

        public delegate int AvHwdeviceCtxCreateDelegate(AVBufferRef** device_ctx, AVHWDeviceType type, [MarshalAs(UnmanagedType.LPUTF8Str)] string device, AVDictionary* opts, int flags);

        public delegate int AvHwframeTransferDataDelegate(AVFrame* dst, AVFrame* src, int flags);

        public delegate AVCodec* AvCodecIterateDelegate(void** opaque);

        public delegate int AvCodecIsDecoderDelegate(AVCodec* codec);

        public delegate AVCodecHWConfig* AvcodecGetHwConfigDelegate(AVCodec* codec, int index);

        public delegate AVCodecContext* AvcodecAllocContext3Delegate(AVCodec* codec);

        public delegate void AvcodecFreeContextDelegate(AVCodecContext** avctx);

        public delegate int AvcodecParametersToContextDelegate(AVCodecContext* codec, AVCodecParameters* par);

        public delegate int AvcodecOpen2Delegate(AVCodecContext* avctx, AVCodec* codec, AVDictionary** options);

        public delegate int AvcodecReceiveFrameDelegate(AVCodecContext* avctx, AVFrame* frame);

        public delegate int AvcodecSendPacketDelegate(AVCodecContext* avctx, AVPacket* avpkt);

        public delegate void AvcodecFlushBuffersDelegate(AVCodecContext* avctx);

        public delegate AVFormatContext* AvformatAllocContextDelegate();

        public delegate void AvformatCloseInputDelegate(AVFormatContext** s);

        public delegate int AvformatFindStreamInfoDelegate(AVFormatContext* ic, AVDictionary** options);

        public delegate int AvformatOpenInputDelegate(AVFormatContext** ps, [MarshalAs(UnmanagedType.LPUTF8Str)] string url, AVInputFormat* fmt, AVDictionary** options);

        public delegate int AvFindBestStreamDelegate(AVFormatContext* ic, AVMediaType type, int wanted_stream_nb, int related_stream, AVCodec** decoder_ret, int flags);

        public delegate AVIOContext* AvioAllocContextDelegate(byte* buffer, int buffer_size, int write_flag, void* opaque, avio_alloc_context_read_packet_func read_packet, avio_alloc_context_write_packet_func write_packet, avio_alloc_context_seek_func seek);

        public delegate void AvioContextFreeDelegate(AVIOContext** s);

        public delegate void SwsFreeContextDelegate(SwsContext* swsContext);

        public delegate SwsContext* SwsGetCachedContextDelegate(SwsContext* context, int srcW, int srcH, AVPixelFormat srcFormat, int dstW, int dstH, AVPixelFormat dstFormat, int flags, SwsFilter* srcFilter, SwsFilter* dstFilter, double* param);

        public delegate int SwsScaleDelegate(SwsContext* c, byte*[] srcSlice, int[] srcStride, int srcSliceY, int srcSliceH, byte*[] dst, int[] dstStride);

        public delegate int AvcodecSendFrameDelegate(AVCodecContext* avctx, AVFrame* frame);

        public delegate int AvcodecReceivePacketDelegate(AVCodecContext* avctx, AVPacket* avpkt);

        public delegate int AvOptSetDelegate(void* obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string val, int search_flags);

        public delegate int AvFrameMakeWritableDelegate(AVFrame* frame);

        public delegate void AvPacketRescaleTsDelegate(AVPacket* pkt, AVRational tb_src, AVRational tb_dst);

        public delegate int AvInterleavedWriteFrameDelegate(AVFormatContext* s, AVPacket* pkt);

        public delegate AVStream* AvformatNewStreamDelegate(AVFormatContext* s, AVCodec* c);

        public delegate void SwrFreeDelegate(SwrContext** s);

        public delegate int AvDictCopyDelegate(AVDictionary** dst, AVDictionary* src, int flags);

        public delegate int AvcodecParametersFromContextDelegate(AVCodecParameters* par, AVCodecContext* codec);

        public delegate AVFrame* AllocAudioFrameDelegate(AVSampleFormat sample_fmt, ulong channel_layout, int sample_rate, int nb_samples);

        public delegate SwrContext* SwrAllocDelegate();

        public delegate int AvOptSetIntDelegate(void* obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, long val, int search_flags);

        public delegate int AvOptSetSampleFmtDelegate(void* obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, AVSampleFormat fmt, int search_flags);

        public delegate int SwrInitDelegate(SwrContext* s);

        public delegate int AvWriteTrailerDelegate(AVFormatContext* s);

        public delegate int AvGetChannelLayoutNbChannelsDelegate(ulong channel_layout);

        public delegate int AvioClosepDelegate(AVIOContext** s);

        public delegate void AvformatFreeContextDelegate(AVFormatContext* s);

        public delegate int AvformatWriteHeaderDelegate(AVFormatContext* s, AVDictionary** options);

        public delegate int AvformatAllocOutputContext2Delegate(AVFormatContext** avctx, AVOutputFormat* oformat, [MarshalAs(UnmanagedType.LPUTF8Str)] string format, [MarshalAs(UnmanagedType.LPUTF8Str)] string filename);

        public delegate int AvCodecIsEncoderDelegate(AVCodec* codec);

        public delegate int AvioOpenDelegate(AVIOContext** s, [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, int flags);

        public delegate SwsContext* SwsGetContextDelegate(int srcW, int srcH, AVPixelFormat srcFormat, int dstW, int dstH, AVPixelFormat dstFormat, int flags, SwsFilter* srcFilter, SwsFilter* dstFilter, double* param);

        #endregion

        [CanBeNull]
        public AvDictSetDelegate av_dict_set;

        [CanBeNull]
        public AvDictFreeDelegate av_dict_free;

        public AvFrameAllocDelegate av_frame_alloc;
        public AvFrameFreeDelegate av_frame_free;
        public AvFrameUnrefDelegate av_frame_unref;
        public AvFrameMoveRefDelegate av_frame_move_ref;
        public AvFrameGetBufferDelegate av_frame_get_buffer;
        public AvStrDupDelegate av_strdup;
        public AvStrErrorDelegate av_strerror;
        public AvMallocDelegate av_malloc;
        public AvFreepDelegate av_freep;
        public AvPacketAllocDelegate av_packet_alloc;
        public AvPacketUnrefDelegate av_packet_unref;
        public AvPacketFreeDelegate av_packet_free;
        public AvReadFrameDelegate av_read_frame;
        public AvSeekFrameDelegate av_seek_frame;
        public AvHwdeviceCtxCreateDelegate av_hwdevice_ctx_create;
        public AvHwframeTransferDataDelegate av_hwframe_transfer_data;
        public AvCodecIterateDelegate av_codec_iterate;
        public AvCodecIsDecoderDelegate av_codec_is_decoder;
        public AvcodecGetHwConfigDelegate avcodec_get_hw_config;
        public AvcodecAllocContext3Delegate avcodec_alloc_context3;
        public AvcodecFreeContextDelegate avcodec_free_context;
        public AvcodecParametersToContextDelegate avcodec_parameters_to_context;
        public AvcodecOpen2Delegate avcodec_open2;
        public AvcodecReceiveFrameDelegate avcodec_receive_frame;
        public AvcodecSendPacketDelegate avcodec_send_packet;
        public AvcodecFlushBuffersDelegate avcodec_flush_buffers;
        public AvformatAllocContextDelegate avformat_alloc_context;
        public AvformatCloseInputDelegate avformat_close_input;
        public AvformatFindStreamInfoDelegate avformat_find_stream_info;
        public AvformatOpenInputDelegate avformat_open_input;
        public AvFindBestStreamDelegate av_find_best_stream;
        public AvioAllocContextDelegate avio_alloc_context;
        public AvioContextFreeDelegate avio_context_free;
        public SwsFreeContextDelegate sws_freeContext;
        public SwsGetCachedContextDelegate sws_getCachedContext;
        public SwsScaleDelegate sws_scale;
        public AvcodecSendFrameDelegate avcodec_send_frame;
        public AvcodecReceivePacketDelegate avcodec_receive_packet;
        public AvOptSetDelegate av_opt_set;
        public AvFrameMakeWritableDelegate av_frame_make_writable;
        public AvPacketRescaleTsDelegate av_packet_rescale_ts;
        public AvInterleavedWriteFrameDelegate av_interleaved_write_frame;
        public AvformatNewStreamDelegate avformat_new_stream;
        public SwrFreeDelegate swr_free;
        public AvDictCopyDelegate av_dict_copy;
        public AvcodecParametersFromContextDelegate avcodec_parameters_from_context;
        public AllocAudioFrameDelegate alloc_audio_frame;
        public SwrAllocDelegate swr_alloc;
        public AvOptSetIntDelegate av_opt_set_int;
        public AvOptSetSampleFmtDelegate av_opt_set_sample_fmt;
        public SwrInitDelegate swr_init;
        public AvWriteTrailerDelegate av_write_trailer;
        public AvGetChannelLayoutNbChannelsDelegate av_get_channel_layout_nb_channels;
        public AvioClosepDelegate avio_closep;
        public AvformatFreeContextDelegate avformat_free_context;
        public AvformatWriteHeaderDelegate avformat_write_header;
        public AvformatAllocOutputContext2Delegate avformat_alloc_output_context2;
        public AvCodecIsEncoderDelegate av_codec_is_encoder;
        public AvioOpenDelegate avio_open;
        public SwsGetContextDelegate sws_getContext;

        // Touching AutoGen.ffmpeg or its LibraryLoader in any way on non-Desktop platforms
        // will cause it to throw in static constructor, which can't be bypassed.
        // Define our own constants to avoid touching the class.

        public const int AVSEEK_FLAG_BACKWARD = 1;
        public const int AVSEEK_SIZE = 0x10000;
        public const int AVFMT_FLAG_GENPTS = 0x0001;
        public const int AV_TIME_BASE = 1000000;
        public static readonly int EAGAIN = RuntimeInfo.IsApple ? 35 : 11;
        public const int AVERROR_EOF = -('E' + ('O' << 8) + ('F' << 16) + (' ' << 24));
        public const long AV_NOPTS_VALUE = unchecked((long)0x8000000000000000);
        public const int ENOMEM = 12;
        public const int AV_CODEC_CAP_VARIABLE_FRAME_SIZE = 1 << 16;
        public const int AV_CH_FRONT_LEFT = 0x00000001;
        public const int AV_CH_FRONT_RIGHT = 0x00000002;
        public const int AV_CH_LAYOUT_STEREO = AV_CH_FRONT_LEFT | AV_CH_FRONT_RIGHT;
        public const int AV_CODEC_FLAG_GLOBAL_HEADER = 1 << 22;
        public const int AVFMT_GLOBALHEADER = 0x0040;
        public const int SWS_BICUBIC = 4;
        public const int AVFMT_NOFILE = 0x0001;
        public const int AVIO_FLAG_WRITE = 2;
    }
}
