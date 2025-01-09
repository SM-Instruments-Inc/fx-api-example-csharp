using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FxApiCSharp;

public unsafe partial class H264FrameDecoder : IDisposable
{
    private readonly AVCodecContext* _codecContext;
    private readonly AVFrame* _frame;
    private readonly AVPacket* _packet;
    private SwsContext* _swsContext;
    private Lock _padlock = new();

    public H264FrameDecoder()
    {
        var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
        _codecContext = ffmpeg.avcodec_alloc_context3(codec);
        _frame = ffmpeg.av_frame_alloc();
        _packet = ffmpeg.av_packet_alloc();

        if (ffmpeg.avcodec_open2(_codecContext, codec, null) < 0)
            throw new ApplicationException("Could not open codec.");
    }

    public bool Feed(byte[] data)
    {
        lock (_padlock)
        {
            if (_disposed) return false;

            fixed (byte* pData = data)
            {
                ffmpeg.av_packet_unref(_packet);

                _packet->data = pData;
                _packet->size = data.Length;
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (ffmpeg.avcodec_send_packet(_codecContext, _packet) < 0)
                {
                    Trace.WriteLine($"Frame Decoding Error: {data.Length}");
                    return false;
                }

                if (ffmpeg.avcodec_receive_frame(_codecContext, _frame) < 0)
                {
                    return false;
                }
                var width = _frame->width;
                var height = _frame->height;
            }
            return true;
        }
    }

    public AVFrame* GetFrame()
    {
        lock (_padlock)
        {
            if (_disposed) return null;
            return _frame;
        }
    }

    public byte[] GetImage(AVPixelFormat pixelFormat)
    {
        lock (_padlock)
        {
            try
            {
                if (_disposed) return null;

                int width = _frame->width;
                int height = _frame->height;

                if (width + height == 0) return null;

                var destFormat = pixelFormat;

                if (_swsContext == null)
                {
                    _swsContext = ffmpeg.sws_getContext(width, height, (AVPixelFormat)_frame->format,
                                                        width, height, destFormat,
                                                        ffmpeg.SWS_BILINEAR, null, null, null);
                }

                var dstData = new byte[width * height * 4];
                var dstLineSize = new int[] { 4 * width };

                fixed (byte* pDstData = dstData)
                {
                    fixed (int* pDstLineSize = dstLineSize)
                    {
                        byte_ptrArray4 dstDataArray = new();
                        dstDataArray[0] = pDstData;

                        int_array4 dstLineSizeArray = new();
                        dstLineSizeArray[0] = dstLineSize[0];

                        ffmpeg.sws_scale(_swsContext, _frame->data, _frame->linesize, 0, height, dstDataArray, dstLineSizeArray);
                    }
                }
                return dstData;
            }
            catch { return null; } // Ignore errors (prevent crashing the application)
        }
    }


    private bool _disposed;
    public unsafe void Dispose()
    {
        lock (_padlock)
        {
            _disposed = true;

            // Free the codec context
            var codecContext = _codecContext;
            ffmpeg.avcodec_free_context(&codecContext);

            // Free the frame
            var frame = _frame;
            ffmpeg.av_frame_free(&frame);

            // Free the packet
            var packet = _packet;
            ffmpeg.av_packet_free(&packet);

            // Free the sws context
            ffmpeg.sws_freeContext(_swsContext);
        }

        GC.SuppressFinalize(this);
    }
}
