// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Runtime.InteropServices;
using System.Text;
using osu.Framework.Platform.Linux.Native;

namespace osu.Framework.Graphics.Video
{
    public abstract unsafe class FFmpegComponent
    {
        protected readonly FFmpegFuncs Ffmpeg;

        protected FFmpegComponent()
        {
            Ffmpeg = CreateFuncs();
        }

        static FFmpegComponent()
        {
            if (RuntimeInfo.OS == RuntimeInfo.Platform.Linux)
            {
                void loadVersionedLibraryGlobally(string name)
                {
                    int version = FFmpeg.AutoGen.ffmpeg.LibraryVersionMap[name];
                    Library.Load($"lib{name}.so.{version}", Library.LoadFlags.RTLD_LAZY | Library.LoadFlags.RTLD_GLOBAL);
                }

                // FFmpeg.AutoGen doesn't load libraries as RTLD_GLOBAL, so we must load them ourselves to fix inter-library dependencies
                // otherwise they would fallback to the system-installed libraries that can differ in version installed.
                loadVersionedLibraryGlobally("avutil");
                loadVersionedLibraryGlobally("avcodec");
                loadVersionedLibraryGlobally("avformat");
                loadVersionedLibraryGlobally("swscale");
            }
        }

        protected virtual FFmpegFuncs CreateFuncs()
        {
            // other frameworks should handle native libraries themselves
            FFmpeg.AutoGen.ffmpeg.GetOrLoadLibrary = name =>
            {
                int version = FFmpeg.AutoGen.ffmpeg.LibraryVersionMap[name];

                // "lib" prefix and extensions are resolved by .net core
                string libraryName;

                switch (RuntimeInfo.OS)
                {
                    case RuntimeInfo.Platform.macOS:
                        libraryName = $"{name}.{version}";
                        break;

                    case RuntimeInfo.Platform.Windows:
                        libraryName = $"{name}-{version}";
                        break;

                    // To handle versioning in Linux, we have to specify the entire file name
                    // because Linux uses a version suffix after the file extension (e.g. libavutil.so.56)
                    // More info: https://learn.microsoft.com/en-us/dotnet/standard/native-interop/native-library-loading?view=net-6.0
                    case RuntimeInfo.Platform.Linux:
                        libraryName = $"lib{name}.so.{version}";
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(RuntimeInfo.OS), RuntimeInfo.OS, null);
                }

                return NativeLibrary.Load(libraryName, RuntimeInfo.EntryAssembly, DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.SafeDirectories);
            };

            return new FFmpegFuncs
            {
                av_dict_set = FFmpeg.AutoGen.ffmpeg.av_dict_set,
                av_dict_free = FFmpeg.AutoGen.ffmpeg.av_dict_free,
                av_frame_alloc = FFmpeg.AutoGen.ffmpeg.av_frame_alloc,
                av_frame_free = FFmpeg.AutoGen.ffmpeg.av_frame_free,
                av_frame_unref = FFmpeg.AutoGen.ffmpeg.av_frame_unref,
                av_frame_move_ref = FFmpeg.AutoGen.ffmpeg.av_frame_move_ref,
                av_frame_get_buffer = FFmpeg.AutoGen.ffmpeg.av_frame_get_buffer,
                av_strdup = FFmpeg.AutoGen.ffmpeg.av_strdup,
                av_strerror = FFmpeg.AutoGen.ffmpeg.av_strerror,
                av_malloc = FFmpeg.AutoGen.ffmpeg.av_malloc,
                av_freep = FFmpeg.AutoGen.ffmpeg.av_freep,
                av_packet_alloc = FFmpeg.AutoGen.ffmpeg.av_packet_alloc,
                av_packet_unref = FFmpeg.AutoGen.ffmpeg.av_packet_unref,
                av_packet_free = FFmpeg.AutoGen.ffmpeg.av_packet_free,
                av_read_frame = FFmpeg.AutoGen.ffmpeg.av_read_frame,
                av_seek_frame = FFmpeg.AutoGen.ffmpeg.av_seek_frame,
                av_hwdevice_ctx_create = FFmpeg.AutoGen.ffmpeg.av_hwdevice_ctx_create,
                av_hwframe_transfer_data = FFmpeg.AutoGen.ffmpeg.av_hwframe_transfer_data,
                av_codec_iterate = FFmpeg.AutoGen.ffmpeg.av_codec_iterate,
                av_codec_is_decoder = FFmpeg.AutoGen.ffmpeg.av_codec_is_decoder,
                avcodec_get_hw_config = FFmpeg.AutoGen.ffmpeg.avcodec_get_hw_config,
                avcodec_alloc_context3 = FFmpeg.AutoGen.ffmpeg.avcodec_alloc_context3,
                avcodec_free_context = FFmpeg.AutoGen.ffmpeg.avcodec_free_context,
                avcodec_parameters_to_context = FFmpeg.AutoGen.ffmpeg.avcodec_parameters_to_context,
                avcodec_open2 = FFmpeg.AutoGen.ffmpeg.avcodec_open2,
                avcodec_receive_frame = FFmpeg.AutoGen.ffmpeg.avcodec_receive_frame,
                avcodec_send_packet = FFmpeg.AutoGen.ffmpeg.avcodec_send_packet,
                avcodec_flush_buffers = FFmpeg.AutoGen.ffmpeg.avcodec_flush_buffers,
                avformat_alloc_context = FFmpeg.AutoGen.ffmpeg.avformat_alloc_context,
                avformat_close_input = FFmpeg.AutoGen.ffmpeg.avformat_close_input,
                avformat_find_stream_info = FFmpeg.AutoGen.ffmpeg.avformat_find_stream_info,
                avformat_open_input = FFmpeg.AutoGen.ffmpeg.avformat_open_input,
                av_find_best_stream = FFmpeg.AutoGen.ffmpeg.av_find_best_stream,
                avio_alloc_context = FFmpeg.AutoGen.ffmpeg.avio_alloc_context,
                avio_context_free = FFmpeg.AutoGen.ffmpeg.avio_context_free,
                sws_freeContext = FFmpeg.AutoGen.ffmpeg.sws_freeContext,
                sws_getCachedContext = FFmpeg.AutoGen.ffmpeg.sws_getCachedContext,
                sws_scale = FFmpeg.AutoGen.ffmpeg.sws_scale,
                avcodec_send_frame = FFmpeg.AutoGen.ffmpeg.avcodec_send_frame,
                avcodec_receive_packet = FFmpeg.AutoGen.ffmpeg.avcodec_receive_packet,
                av_opt_set = FFmpeg.AutoGen.ffmpeg.av_opt_set,
                av_frame_make_writable = FFmpeg.AutoGen.ffmpeg.av_frame_make_writable,
                av_packet_rescale_ts = FFmpeg.AutoGen.ffmpeg.av_packet_rescale_ts,
                av_interleaved_write_frame = FFmpeg.AutoGen.ffmpeg.av_interleaved_write_frame,
                avformat_new_stream = FFmpeg.AutoGen.ffmpeg.avformat_new_stream,
                swr_free = FFmpeg.AutoGen.ffmpeg.swr_free,
                av_dict_copy = FFmpeg.AutoGen.ffmpeg.av_dict_copy,
                avcodec_parameters_from_context = FFmpeg.AutoGen.ffmpeg.avcodec_parameters_from_context,
                swr_alloc = FFmpeg.AutoGen.ffmpeg.swr_alloc,
                av_opt_set_int = FFmpeg.AutoGen.ffmpeg.av_opt_set_int,
                av_opt_set_sample_fmt = FFmpeg.AutoGen.ffmpeg.av_opt_set_sample_fmt,
                swr_init = FFmpeg.AutoGen.ffmpeg.swr_init,
                av_write_trailer = FFmpeg.AutoGen.ffmpeg.av_write_trailer,
                av_get_channel_layout_nb_channels = FFmpeg.AutoGen.ffmpeg.av_get_channel_layout_nb_channels,
                avio_closep = FFmpeg.AutoGen.ffmpeg.avio_closep,
                avformat_free_context = FFmpeg.AutoGen.ffmpeg.avformat_free_context,
                avformat_write_header = FFmpeg.AutoGen.ffmpeg.avformat_write_header,
                avformat_alloc_output_context2 = FFmpeg.AutoGen.ffmpeg.avformat_alloc_output_context2,
                av_codec_is_encoder = FFmpeg.AutoGen.ffmpeg.av_codec_is_encoder,
                avio_open = FFmpeg.AutoGen.ffmpeg.avio_open,
                sws_getContext = FFmpeg.AutoGen.ffmpeg.sws_getContext,
                swr_convert = FFmpeg.AutoGen.ffmpeg.swr_convert,
                av_rescale_q = FFmpeg.AutoGen.ffmpeg.av_rescale_q,
                avcodec_find_encoder = FFmpeg.AutoGen.ffmpeg.avcodec_find_encoder,
            };
        }

        protected string GetErrorMessage(int errorCode)
        {
            const ulong buffer_size = 256;
            byte[] buffer = new byte[buffer_size];

            int strErrorCode;

            fixed (byte* bufPtr = buffer)
            {
                strErrorCode = Ffmpeg.av_strerror(errorCode, bufPtr, buffer_size);
            }

            if (strErrorCode < 0)
                return $"{errorCode} (av_strerror failed with code {strErrorCode})";

            int messageLength = Math.Max(0, Array.IndexOf(buffer, (byte)0));
            return $"{Encoding.ASCII.GetString(buffer[..messageLength])} ({errorCode})";
        }
    }
}
