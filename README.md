# FX API (C#)

This is a C# library that enables seamless integration with cameras using both RTSP and WebSocket protocols. It provides functionalities for video streaming, beamforming image processing, and more.

---

## Features

- **RTSP Video Streaming**:
  
  - Receive and process video frames in real time.
  - Decode H.264 video frames using the integrated decoder.

- **Beamforming Visualization**:
  
  - Retrieve and process beamforming data for visualization.
  - Customize beamforming settings such as threshold, range, and mode (Full View or Multi-Source).

- **WebSocket Support**:
  
  - Subscribe to and handle WebSocket messages for beamforming updates.

- **Image Conversion Utilities**:
  
  - Convert video frames and beamforming data to various formats, including raw pixel data and PNG.

- **Customizable Image Processing**:
  
  - Advanced interpolation and dampening algorithms for hotspot visualization.

---

## Installation

1. Clone the repository or download the source code.
2. Add the project to your solution or include the library in your project.
3. Ensure all required dependencies are referenced as a nuget package:
   - `FFmpeg.AutoGen`
   - `OpenCvSharp`
   - `Google.Protobuf`
   - `Websocket.Client`

---

## Usage

### Initialize Camera Connection

```csharp
var camera = new Camera("192.168.1.100", "admin", "admin", 80, 8554); // Same as var camera = new Camera("192.168.1.100");

var cameraConnection = new CameraConnection(camera);
await cameraConnection.ConnectAsync();
```

### Receive Video Frames

Subscribe to the `VideoFrameReceived` event and update video frame:

```csharp
cameraConnection.VideoFrameReceived += (sender, frame) =>
{ 
    var png = cameraConnection.GetVideoImageAsPng();
    // Draw png image (or use cameraConnection.GetVideoImage)
};
```

### Retrieve Beamforming Data

Subscribe to the `BeamformingMessageReceived` event and update beamforming frame:

```csharp
cameraConnection.BeamformingMessageReceived += (sender, beamforming) =>
{
    var png = cameraConnection.GetBeamformingImageAsPng(settings);
    // Draw png image (or use cameraConnection.GetBeamformingImage)
};
```

### Process Video Image

Retrieve the video frame as a byte array or PNG:

```csharp
byte[] image = cameraConnection.GetVideoImage();
byte[] pngImage = cameraConnection.GetVideoImageAsPng();
```

### Process Beamforming Data

Generate beamforming images using custom settings:

```csharp
var settings = new BeamformingSettings
{
    Threshold = 0.5f,
    Range = 1.0f,
    Mode = BeamformingMode.FullView,
    UseAverage = true,
    Average = 3.0f
};

byte[] beamformingImage = cameraConnection.GetBeamformingImage(settings);
byte[] beamformingPng = cameraConnection.GetBeamformingImageAsPng(settings);
```

### Disconnect Camera

Disconnect the camera and clean up resources:

```csharp
cameraConnection.Disconnect();
```

---

## Dependencies

- [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen)
- [OpenCvSharp](https://github.com/shimat/opencvsharp)
- [Google.Protobuf](https://developers.google.com/protocol-buffers/)
- [Websocket.Client](https://github.com/Marfusios/websocket-client)

---
