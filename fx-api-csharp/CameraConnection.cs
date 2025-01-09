using FFmpeg.AutoGen;
using FxApiCSharp.DataTypes;
using FxApiCSharp.Enums;
using Google.Protobuf;
using OpenCvSharp;
using RtspClientSharp;
using RtspClientSharp.RawFrames;
using RtspClientSharp.RawFrames.Video;
using seesv.protocol;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using Websocket.Client;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace FxApiCSharp;

/// <summary>
/// Represents a connection to a camera. (RTSP and WebSocket)
/// </summary>
/// <param name="camera">The camera to connect to.</param>
public class CameraConnection(Camera camera) : IDisposable
{
    /// <summary>
    /// Invoked when a video frame is received. (Should be used to update the video image)
    /// </summary>
    public event EventHandler<RawFrame> VideoFrameReceived;

    /// <summary>
    /// Invoked when a beamforming message is received. (Should be used to update the beamforming image)
    /// </summary>
    public event EventHandler<Beamforming> BeamformingMessageReceived;

    private const double ImageWidth = 640;
    private const double ImageHeight = 480;
    private const int GridWidth = 40;
    private const int GridHeight = 30;
    private const float Beta = 3.8f;

    public H264FrameDecoder H264FrameDecoder { get; private set; }

    private RtspClient _rtspClient;
    private WebsocketClient _beamformingWebSocket;
    private CancellationTokenSource _rtspClientCancellationTokenSource;

    /// <summary>
    /// Connects to the camera. (Opens the RTSP and WebSocket connections)
    /// </summary>
    /// <returns>See <see cref="Task"/></returns>
    /// <exception cref="InvalidOperationException">Thrown when already connected or connecting to the camera.</exception>
    public async Task ConnectAsync()
    {
        if (_rtspClient != null) throw new InvalidOperationException("Already connected or connecting to the camera.");

        await InitializeRtspClientAsync();
        await InitializeWebSocketAsync();
    }

    /// <summary>
    /// Disconnects from the camera. (Closes the RTSP and WebSocket connections)
    /// </summary>
    public void Disconnect()
    {
        DisconnectRtspClient();
        DisconnectWebSocketClient();
    }

    /// <summary>
    /// Gets the video frame as a pointer. (AVFrame*)
    /// </summary>
    /// <returns>The video frame as a pointer. (AVFrame*)</returns>
    /// <exception cref="InvalidOperationException">Thrown when video decoder is not initialized.</exception>
    public unsafe AVFrame* GetVideoFrame()
    {
        if (H264FrameDecoder == null) throw new InvalidOperationException("Video decoder is not initialized.");
        return H264FrameDecoder.GetFrame();
    }

    /// <summary>
    /// Gets the video image as a byte array. (Raw pixel data)
    /// </summary>
    /// <param name="pixelFormat">The pixel format to use for the video image.</param>
    /// <returns>The video image (Default: AVPixelFormat.AV_PIX_FMT_RGBA). as a byte array.</returns>
    public byte[] GetVideoImage(AVPixelFormat pixelFormat = AVPixelFormat.AV_PIX_FMT_RGBA) => H264FrameDecoder?.GetImage(pixelFormat);

    /// <summary>
    /// Gets the video image as a PNG byte array.
    /// </summary>
    /// <returns>The video image as a PNG byte array.</returns>
    /// <exception cref="InvalidOperationException">Thrown when video frame is not initialized.</exception>
    public unsafe byte[] GetVideoImageAsPng()
    {
        var image = GetVideoImage() ?? throw new InvalidOperationException("Video frame is not initialized.");
        var frame = GetVideoFrame();

        return ConvertImageToPng(image, frame->width, frame->height);
    }

    /// <summary>
    /// Gets the beamforming image as a byte array. (Raw pixel data, RGBA32)
    /// </summary>
    /// <param name="settings">The settings to use for beamforming.</param>
    /// <returns>The beamforming image as a byte array.</returns>
    /// <exception cref="InvalidOperationException">Thrown when beamforming data is not initialized.</exception>
    public byte[] GetBeamformingImage(BeamformingSettings settings)
    {
        if (_beamforming == null) throw new InvalidOperationException("Beamforming data is not initialized.");

        return ProcessBeamforming(_beamforming, settings);
    }

    /// <summary>
    /// Gets the beamforming image as a PNG byte array.
    /// </summary>
    /// <param name="settings">The settings to use for beamforming.</param>
    /// <returns>The beamforming image as a PNG byte array.</returns>
    public byte[] GetBeamformingImageAsPng(BeamformingSettings settings) => ProcessBeamforming(_beamforming, settings, true);

    /// <summary>
    /// Initializes the WebSocket connection.
    /// </summary>
    /// <returns>See <see cref="Task"/></returns>
    private async Task InitializeWebSocketAsync()
    {
        var factory = new Func<ClientWebSocket>(() =>
        {
            var native = new ClientWebSocket();
            native.Options.AddSubProtocol("subscribe");
            native.Options.Credentials = new NetworkCredential(camera.UserName, camera.Password);
            return native;
        });

        // Beamforming WebSocket
        _beamformingWebSocket = new(new Uri($"ws://{camera.IpAddress}:{camera.Port}/ws"), factory) { ErrorReconnectTimeout = TimeSpan.Zero };
        _beamformingWebSocket.MessageReceived.Subscribe(OnWebSocketMessageReceived);
        _beamformingWebSocket.ReconnectionHappened.Subscribe(OnBeamformingWebSocketReconnectionHappened);

        // Start WebSockets
        await _beamformingWebSocket.Start();
    }

    private bool _disposed;
    /// <summary>
    /// Disposes the camera connection. (Disconnects and cleanup)
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            Disconnect();

            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Initializes the RTSP connection. (Blocking until disconnected)
    /// </summary>
    /// <param name="taskCompletionSource">The task completion source that is used to signal the connection initialization.</param>
    /// <returns>See <see cref="Task"/></returns>
    private async Task InitializeRtspClientAsync(TaskCompletionSource taskCompletionSource = null)
    {
        try
        {
            H264FrameDecoder?.Dispose();
            H264FrameDecoder = new();
            var serverUri = new Uri($"rtsp://{camera.IpAddress}:{camera.RtspPort}/raw");
            var credentials = new NetworkCredential(camera.UserName, camera.Password);
            var connectionParameters = new ConnectionParameters(serverUri, credentials) { ReceiveTimeout = TimeSpan.FromSeconds(5) };
            _rtspClientCancellationTokenSource = new();
            _rtspClient = new RtspClient(connectionParameters);
            _rtspClient.FrameReceived += OnRtspClientFrameReceived;
            await _rtspClient.ConnectAsync(_rtspClientCancellationTokenSource.Token);
            taskCompletionSource?.SetResult();
            await _rtspClient.ReceiveAsync(_rtspClientCancellationTokenSource.Token);
        }
        catch (Exception exception)
        {
            if (_disposed) return;
            else if (exception is OperationCanceledException) return;

            var exceptionName = exception.GetType().Name;
            await RefreshRtspConnectionAsync();
        }
    }

    /// <summary>
    /// Refreshes the RTSP connection. (Disconnects and reconnects)
    /// </summary>
    /// <returns>See <see cref="Task"/></returns>
    private async Task RefreshRtspConnectionAsync()
    {
        _isFirstFrameReceived = false;

        await Task.Delay(500);
        if (_rtspClient != null) Disconnect();
        await Task.Delay(500);
        var taskCompletionSource = new TaskCompletionSource();
        _ = Task.Run(() => InitializeRtspClientAsync(taskCompletionSource));
        await taskCompletionSource.Task;
    }

    private float[] _dbScale = new float[1200];
    private float[] _dbScaleAverage = new float[1200];
    /// <summary>
    /// Processes the beamforming data.
    /// </summary>
    /// <param name="beamforming">The beamforming data to process.</param>
    /// <param name="settings">The settings to use for beamforming.</param>
    /// <param name="convertToPng">Flag to convert the image to PNG format.</param>
    /// <returns>The beamforming image as a byte array. (png or RGBA32)</returns>
    private byte[] ProcessBeamforming(Beamforming beamforming, BeamformingSettings settings, bool convertToPng = false)
    {
        var threshold = settings.Threshold;
        var range = settings.Range;
        var useAverage = settings.UseAverage;
        var average = settings.Average;
        var bytes = beamforming.Bf;
        var virtualGridPositionX = beamforming.VPosX;
        var virtualGridPositionY = beamforming.VPosY;

        _dbScale = [.. bytes.Select(x => (float)x)];

        float maxValue;

        if (useAverage)
        {
            // Calculate average value
            float averageValue = average;
            lock (_dbScale)
            {
                _dbScaleAverage = [.. _dbScaleAverage.Select(x => x * (averageValue - 1))];
                _dbScaleAverage = [.. _dbScaleAverage.Zip(_dbScale, (x, y) => x + y)];
                _dbScaleAverage = [.. _dbScaleAverage.Select(x => x / averageValue)];
            }

            // Set max value
            maxValue = _dbScaleAverage.Max();
        }
        else
        {
            // Set max value
            maxValue = _dbScale.Max();
        }

        // pick data to use by useAverage flag
        var data = useAverage ? _dbScaleAverage : _dbScale;

        Mat matrix;
        // Full-View mode
        if (settings.Mode == BeamformingMode.FullView)
        {
            matrix = Mat.FromPixelData(30, 40, MatType.CV_32FC1, data);

            // Resize matrix by ImageHeight
            var size = new Size(ImageWidth, ImageHeight);
            Cv2.Resize(matrix, matrix, size, 0, 0, InterpolationFlags.Lanczos4);

            var upperLimit = maxValue - range;
            var lowerLimit = threshold;

            if (Math.Abs((range + 1) - 1) <= 1e-5)
            {
                Cv2.Threshold(matrix, matrix, lowerLimit, 0, ThresholdTypes.Tozero);
            }
            else if (upperLimit > lowerLimit)
            {
                Cv2.Threshold(matrix, matrix, upperLimit, 0, ThresholdTypes.Tozero);
            }
            else
            {
                Cv2.Threshold(matrix, matrix, lowerLimit, 0, ThresholdTypes.Tozero);
            }

            var mask = matrix.GreaterThan(0);
            Cv2.Normalize(matrix, matrix, 255, 1, NormTypes.MinMax, -1, mask);
            matrix.ConvertTo(matrix, MatType.CV_8UC1);

            using var bitmap = MatrixToBitmap(matrix, DefaultColorMap.ColorTable);
            if (convertToPng) return ConvertBitmapToPng(bitmap);
            else return ConvertBitmapToRGBA32AndGetPixelData(bitmap);
        }
        // Multi-Source mode
        else
        {
            var hotspotCount = settings.Mode == BeamformingMode.SingleSource ? 1 : 3;
            var mat = Mat.FromPixelData(30, 40, MatType.CV_32FC1, data);
            var dst = (Mat)Mat.Zeros((int)ImageHeight, (int)ImageWidth, MatType.CV_8UC1);

            for (int i = 0; i < hotspotCount; i++)
            {
                if (virtualGridPositionX[i] == -1 || virtualGridPositionY[i] == -1) continue;
                HotspotInterpolation(mat, dst, new(virtualGridPositionX[i], virtualGridPositionY[i]));
            }

            using var bitmap = MatrixToBitmap(dst, DefaultColorMap.ColorTable);
            if (convertToPng) return ConvertBitmapToPng(bitmap);
            else return ConvertBitmapToRGBA32AndGetPixelData(bitmap);
        }
    }

    /// <summary>
    /// Disconnects the WebSocket client and cleanup.
    /// </summary>
    private void DisconnectWebSocketClient()
    {
        _beamformingWebSocket.Dispose();
        _beamformingWebSocket = null;
        _beamforming = null;
    }

    /// <summary>
    /// Disconnects the RTSP client and cleanup.
    /// </summary>
    private void DisconnectRtspClient()
    {
        // Cancel RTSP client
        _rtspClientCancellationTokenSource.Cancel();
        _rtspClientCancellationTokenSource.Dispose();
        _rtspClientCancellationTokenSource = null;

        // Dispose RTSP client
        _rtspClient.FrameReceived -= OnRtspClientFrameReceived;
        _rtspClient.Dispose();
        _rtspClient = null;

        // Dispose H264 frame decoder
        H264FrameDecoder?.Dispose();
        H264FrameDecoder = null;
    }

    private bool _isFirstFrameReceived;
    private void OnRtspClientFrameReceived(object sender, RawFrame rawFrame)
    {
        if (rawFrame.Type != FrameType.Video) return;

        try
        {
            if (rawFrame is RawH264IFrame rawH264IFrame)
            {
                if (!H264FrameDecoder.Feed([.. rawH264IFrame.SpsPpsSegment.Array, .. rawFrame.FrameSegment.Array]))
                {
                    _isFirstFrameReceived = false;
                    H264FrameDecoder?.Dispose();
                    H264FrameDecoder = new();
                }
                if (!_isFirstFrameReceived) _isFirstFrameReceived = true;
            }
            else if (rawFrame is RawH264Frame rawH264Frame)
            {
                if (_isFirstFrameReceived)
                {
                    if (!H264FrameDecoder.Feed([.. rawH264Frame.FrameSegment.Array]))
                    {
                        _isFirstFrameReceived = false;
                        H264FrameDecoder?.Dispose();
                        H264FrameDecoder = new();
                    }
                }
            }

            VideoFrameReceived?.Invoke(this, rawFrame);
        }
        catch { } // Ignore exceptions (prevent crashing the application)
    }

    private void OnWebSocketMessageReceived(ResponseMessage message)
    {
        if (message.MessageType != WebSocketMessageType.Binary) return;

        var eventData = new Event();
        eventData.MergeFrom(message.Binary);

        if (eventData.DataCase == Event.DataOneofCase.Beamforming)
        {
            BeamformingMessageReceived?.Invoke(this, eventData.Beamforming);
            _beamforming = eventData.Beamforming;
        }
    }

    private Beamforming _beamforming;
    private void OnBeamformingWebSocketReconnectionHappened(ReconnectionInfo info)
    {
        _beamformingWebSocket.Send("{\"type\": \"subscribe\", \"id\": 0}");
        _beamformingWebSocket.Send(new Subscribe()
        {
            Id = 0,
            Type = Subscribe.Types.SubscribeType.Subscribe
        }.ToByteArray());
    }

    /// <summary>
    /// Converts the matrix to a bitmap.
    /// </summary>
    /// <param name="matrix">The matrix to convert to a bitmap.</param>
    /// <param name="colorTable">The color table to use for the matrix.</param>
    /// <returns>The bitmap representation of the matrix.</returns>
    /// <exception cref="ArgumentException">Thrown when color table is not 256 colors.</exception>
    private unsafe static Bitmap MatrixToBitmap(Mat matrix, List<Color> colorTable)
    {
        try
        {
            if (colorTable.Count != 256)
                throw new ArgumentException("Color table must contain 256 colors.");

            var bitmap = new Bitmap(matrix.Width, matrix.Height, PixelFormat.Format8bppIndexed);

            // Set the palette using the color table
            ColorPalette palette = bitmap.Palette;

            palette.Entries[0] = Color.FromArgb(0, Color.Transparent); // Set black to transparent

            for (int i = 1; i < 256; i++)
            {
                palette.Entries[i] = colorTable[i];
            }

            bitmap.Palette = palette;

            var data = bitmap.LockBits(new Rectangle(0, 0, matrix.Width, matrix.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);

            long bytesToCopy = matrix.Rows * matrix.Step();
            Buffer.MemoryCopy(matrix.DataPointer, data.Scan0.ToPointer(), bytesToCopy, bytesToCopy);

            bitmap.UnlockBits(data);
            return bitmap;
        }
        finally { matrix.Dispose(); }
    }

    /// <summary>
    /// Converts the image to a PNG byte array.
    /// </summary>
    /// <param name="image">The image as a byte array.</param>
    /// <param name="width">The width of the image.</param>
    /// <param name="height">The height of the image.</param>
    /// <param name="pixelFormat">The pixel format to used for the image.</param>
    /// <returns>The image as a PNG byte array.</returns>
    public static byte[] ConvertImageToPng(byte[] image, int width, int height, PixelFormat pixelFormat = PixelFormat.Format32bppArgb)
    {
        using var bitmap = new Bitmap(width, height, pixelFormat);
        var bitmapData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, pixelFormat);

        Marshal.Copy(image, 0, bitmapData.Scan0, image.Length);
        bitmap.UnlockBits(bitmapData);

        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Converts the bitmap to a byte array.
    /// </summary>
    /// <param name="originalBitmap">The bitmap to convert to a byte array.</param>
    /// <returns>The image as a byte array.</returns>
    private static byte[] ConvertBitmapToRGBA32AndGetPixelData(Bitmap originalBitmap)
    {
        // Create a new bitmap with Format32bppArgb
        using var newBitmap = new Bitmap(originalBitmap.Width, originalBitmap.Height, PixelFormat.Format32bppArgb);

        // Use Graphics to draw the original bitmap onto the new bitmap
        using (var graphics = Graphics.FromImage(newBitmap)) graphics.DrawImage(originalBitmap, new Rectangle(0, 0, originalBitmap.Width, originalBitmap.Height));

        return BitmapToRawPixelData(newBitmap);
    }

    /// <summary>
    /// Converts the bitmap to a PNG byte array.
    /// </summary>
    /// <param name="bitmap">The bitmap to convert to a png byte array.</param>
    /// <returns>The image as a byte array.</returns>
    private static byte[] ConvertBitmapToPng(Bitmap bitmap)
    {
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Converts the bitmap to a pixel data byte array.
    /// </summary>
    /// <param name="bitmap">The bitmap to convert to a pixel data byte array.</param>
    /// <returns>The bitmap as a pixel data byte array.</returns>
    private static byte[] BitmapToRawPixelData(Bitmap bitmap)
    {
        // Define the bitmap rectangle
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);

        // Lock the bitmap's bits
        var bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);

        // Get the address of the first line
        IntPtr ptr = bmpData.Scan0;

        // Declare an array to hold the bytes of the bitmap
        int bytes = Math.Abs(bmpData.Stride) * bitmap.Height;
        byte[] rgbValues = new byte[bytes];

        // Copy the RGB values into the array
        Marshal.Copy(ptr, rgbValues, 0, bytes);

        // Unlock the bits
        bitmap.UnlockBits(bmpData);

        return rgbValues;
    }

    /// <summary>
    /// Interpolates the hotspot in the matrix.
    /// </summary>
    /// <param name="mat">The source matrix.</param>
    /// <param name="dst">The destination matrix.</param>
    /// <param name="point">The point to interpolate.</param>
    private static void HotspotInterpolation(Mat mat, Mat dst, Point point)
    {
        const int subMatSize = 1;

        if (point.X < 0 || point.Y < 0 || point.X >= GridWidth || point.Y >= GridHeight) return;

        // Compute the coordinates of the top-left corner of the sub-matrix
        int startRow = Math.Max(0, point.Y - subMatSize);
        int startCol = Math.Max(0, point.X - subMatSize);

        // Compute the coordinates of the bottom-right corner of the sub-matrix
        int endRow = Math.Min(mat.Rows - 1, point.Y + subMatSize);
        int endCol = Math.Min(mat.Cols - 1, point.X + subMatSize);

        int rows = endRow - startRow + 1;
        int cols = endCol - startCol + 1;

        // Always include the center point
        rows = Math.Max(rows, subMatSize + 1);
        cols = Math.Max(cols, subMatSize + 1);

        // Compute virtual grid point location in new matrix
        int centerX = ((startCol + cols) >= mat.Cols - 1) ? subMatSize : cols - subMatSize - 1;
        int centerY = ((startRow + rows) >= mat.Rows - 1) ? subMatSize : rows - subMatSize - 1;

        // Extract the submatrix
        var subMatrix = new Mat(mat, new Rect(startCol, startRow, cols, rows)).Clone();

        var finalMat = new Mat(rows, cols, MatType.CV_32FC1);

        // Dampen the hotspot
        HotspotDampening(subMatrix, new Point(centerX, centerY), subMatSize, Beta, finalMat);

        // Smoothen the hotspot with image depth
        var imageDepth = finalMat.At<float>(centerY, centerX) - Beta;

        // Interpolate
        Cv2.Resize(finalMat, finalMat,
            new Size((ImageWidth / GridWidth) * cols, (ImageHeight / GridHeight) * rows),
            0, 0, InterpolationFlags.Linear);

        Cv2.Threshold(finalMat, finalMat, imageDepth, 0, ThresholdTypes.Tozero);


        // Normalize from [1-255] inside ROI; 0 means transparent
        var mask = finalMat.GreaterThan(0);
        Cv2.Normalize(finalMat, finalMat, 255, 1, NormTypes.MinMax, -1, mask);

        finalMat.ConvertTo(finalMat, MatType.CV_8UC1);

        // Scale zoom point w.r.t matrix center point
        Point scaledCenterPoint, scaledStartPoint;
        scaledCenterPoint = ScaleToVirtualGridSize(new Point(point.X, point.Y));

        // Get new scaled start point
        scaledStartPoint.X = (int)(scaledCenterPoint.X - ((ImageWidth / GridWidth) * Math.Abs(centerX)));
        scaledStartPoint.Y = (int)(scaledCenterPoint.Y - ((ImageHeight / GridHeight) * Math.Abs(centerY)));

        // Scaled points are in boundary limits
        if (scaledStartPoint.X < 0 || scaledStartPoint.Y < 0 ||
            (scaledStartPoint.X + finalMat.Cols) >= ImageWidth ||
            (scaledStartPoint.Y + finalMat.Rows) >= ImageHeight)
        {
            return;
        }

        var roi = new Rect(scaledStartPoint.X, scaledStartPoint.Y, finalMat.Cols, finalMat.Rows);

        // Copy only where source matrix value is greater than the destination matrix value
        mask = finalMat.GreaterThan(dst[roi]);
        finalMat.CopyTo(dst[roi], mask);
    }

    /// <summary>
    /// Dampens the hotspot in the matrix.
    /// </summary>
    /// <param name="mat">The source matrix.</param>
    /// <param name="poi">The point of interest.</param>
    /// <param name="radius">The radius of the hotspot.</param>
    /// <param name="beta">The dampening factor.</param>
    /// <param name="destinationMatrix">The destination matrix to update.</param>
    /// <exception cref="ArgumentException">Thrown when source and destination matrices have different size or type.</exception>
    private static void HotspotDampening(Mat mat, Point poi, int radius, float beta, Mat destinationMatrix)
    {
        // Validate that source and destination matrices have the same size and type
        if (mat.Size() != destinationMatrix.Size() || mat.Type() != destinationMatrix.Type())
        {
            throw new ArgumentException("Source and destination matrices must have the same size and type.");
        }

        // Precompute the value at the point of interest for efficiency
        float matPoi = mat.Get<float>(poi.Y, poi.X);

        // Iterate over each pixel in the matrix
        for (int row = 0; row < mat.Rows; row++)
        {
            for (int col = 0; col < mat.Cols; col++)
            {
                var pos = new Point(col, row);

                // Retrieve the current pixel value from the source matrix
                float matPos = mat.Get<float>(pos.Y, pos.X);

                // Calculate the absolute difference between the POI value and the current pixel value
                float absDiff = Math.Abs(matPoi - matPos);

                // Compute the Euclidean distance from the current pixel to the point of interest
                float dx = pos.X - poi.X;
                float dy = pos.Y - poi.Y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);

                // Calculate the weight based on the dampening formula
                float weight = -(absDiff + beta) * distance / radius;

                // Update the destination matrix with the dampened value
                float newValue = matPos + weight;
                destinationMatrix.Set<float>(pos.Y, pos.X, newValue);
            }
        }
    }

    /// <summary>
    /// Scales the coordinates to the virtual grid size.
    /// </summary>
    /// <param name="coordinates">The coordinates to scale.</param>
    /// <returns>The scaled coordinates.</returns>
    private static Point ScaleToVirtualGridSize(Point coordinates)
    {
        int x = (int)((ImageWidth / GridWidth) * coordinates.X);
        int y = (int)((ImageHeight / GridHeight) * coordinates.Y);

        return new Point(x, y);
    }
}
