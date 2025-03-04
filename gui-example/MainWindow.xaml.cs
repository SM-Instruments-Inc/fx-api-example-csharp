using FxApiCSharp.DataTypes;
using FxApiCSharp.Enums;
using FxApiCSharp;
using Microsoft.UI.Xaml;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace gui_example;

public sealed partial class MainWindow : Window
{
    private Camera _camera;
    private CameraConnection _cameraConnection;
    private const string CameraIp = "10.1.0.137";
    // You can add more parameters to the Camera constructor if needed
    // private const string UserName = "admin";
    // private const string Password = "admin";
    // private const int Port = 80;
    // private const int RtspPort = 8554;

    public MainWindow()
    {
        this.InitializeComponent();
        Task.Run(Initialize);
    }

    private async void Initialize()
    {
        _camera = new(CameraIp);
        _cameraConnection = new(_camera);

        var settings = new BeamformingSettings
        {
            Threshold = 0,
            Range = 0,
            Mode = BeamformingMode.FullView,
            UseAverage = false,
            Average = 3
        };

        _cameraConnection.BeamformingMessageReceived += (sender, message) =>
        {
            try
            {
                var png = _cameraConnection.GetBeamformingImage(settings);
                DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var writableBitmap = ImgBeamforming.Source as WriteableBitmap;
                        if (writableBitmap == null)
                        {
                            writableBitmap = new WriteableBitmap(640, 480);
                            ImgBeamforming.Source = writableBitmap;
                        }

                        using (var stream = writableBitmap.PixelBuffer.AsStream())
                        {
                            await Task.Run(() => stream.Write(png, 0, png.Length));
                        }
                        writableBitmap.Invalidate();
                    }
                    catch { }// Ignore errors
                });
            } catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"Beamforming is not ready. {ex.Message}");
            } catch (Exception ex)
            {
                Debug.WriteLine($"Exception: {ex.Message}");
            }
        };

        _cameraConnection.VideoFrameReceived += (sender, frame) =>
        {
            var png = _cameraConnection.GetVideoImage(FFmpeg.AutoGen.AVPixelFormat.AV_PIX_FMT_BGRA);
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var writableBitmap = ImgVideo.Source as WriteableBitmap;
                    if (writableBitmap == null)
                    {
                        writableBitmap = new WriteableBitmap(1600, 1200);
                        ImgVideo.Source = writableBitmap;
                    }

                    using (var stream = writableBitmap.PixelBuffer.AsStream())
                    {
                        await Task.Run(() => stream.Write(png, 0, png.Length));
                    }
                    writableBitmap.Invalidate();
                }
                catch { }// Ignore errors
            });
        };

        await _cameraConnection.ConnectAsync();
    }
}
