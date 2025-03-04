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
4. Move dll files from `gui-example\win-x64` or `gui-example\win-arm64` to your project.
 Folder structure should be same with origin, such as `{YOUR_PROJECT}\win-64` or `{YOUR_PROJECT}\win-arm64`
5. Open your project's csproj file and add following items.
 ```csproj
<ItemGroup>
  <ARM64Binaries Include="win-arm64\**" />
  <X64Binaries Include="win-x64\**" />
</ItemGroup>

<Target Name="AfterBuildARM" AfterTargets="Build" Condition=" '$(Platform)' == 'ARM64' ">
  <Message Text="Copying ARM64 Binaries..." Importance="High" />
  <Copy SourceFiles="@(ARM64Binaries)" DestinationFolder="$(OutDir)" />
  <Message Text="Cleaning up Unsupported Architecture Dependencies..." Importance="High" />
  <RemoveDir Directories="$(OutDir)\runtimes\win-x64" />
  <RemoveDir Directories="$(OutDir)\runtimes\win-x86" />
  <RemoveDir Directories="$(OutDir)\runtimes\win10-x64" />
  <RemoveDir Directories="$(OutDir)\runtimes\win10-x86" />
</Target>

<Target Name="AfterPublishARM" AfterTargets="Publish" Condition=" '$(Platform)' == 'ARM64' ">
  <Message Text="Copying ARM64 Binaries..." Importance="High" />
  <Copy SourceFiles="@(ARM64Binaries)" DestinationFolder="$(PublishDir)" />
  <Message Text="Cleaning up Unsupported Architecture Dependencies..." Importance="High" />
  <RemoveDir Directories="$(PublishDir)\runtimes\win-x64" />
  <RemoveDir Directories="$(PublishDir)\runtimes\win-x86" />
  <RemoveDir Directories="$(PublishDir)\runtimes\win10-x64" />
  <RemoveDir Directories="$(PublishDir)\runtimes\win10-x86" />
  <RemoveDir Directories="$(LinuxPublishRuntimeDirs)*" />
</Target>

<Target Name="AfterBuildX64" AfterTargets="Build" Condition=" '$(Platform)' == 'x64' ">
  <Message Text="Copying X64 Binaries..." Importance="High" />
  <Copy SourceFiles="@(X64Binaries)" DestinationFolder="$(OutDir)" />
  <Message Text="Cleaning up Unsupported Architecture Dependencies..." Importance="High" />
  <RemoveDir Directories="$(OutDir)\runtimes\win-arm64" />
  <RemoveDir Directories="$(OutDir)\runtimes\win-x86" />
  <RemoveDir Directories="$(OutDir)\runtimes\win10-arm64" />
  <RemoveDir Directories="$(OutDir)\runtimes\win10-x86" />
  <RemoveDir Directories="$(OutDir)\runtimes\linux**" />
</Target>
<Target Name="AfterPublishX64" AfterTargets="Publish" Condition=" '$(Platform)' == 'x64' ">
  <Message Text="Copying X64 Binaries..." Importance="High" />
  <Copy SourceFiles="@(X64Binaries)" DestinationFolder="$(PublishDir)" />
  <Message Text="Cleaning up Unsupported Architecture Dependencies..." Importance="High" />
  <RemoveDir Directories="$(PublishDir)\runtimes\win-arm64" />
  <RemoveDir Directories="$(PublishDir)\runtimes\win-x86" />
  <RemoveDir Directories="$(PublishDir)\runtimes\win10-arm64" />
  <RemoveDir Directories="$(PublishDir)\runtimes\win10-x86" />
  <RemoveDir Directories="$(PublishDir)\runtimes\linux**" />
</Target>
 ```
6. Make sure you make to copy dll files when it's new. To make it copy to out directory:
  6-1. Select dll files from your Solution Explorer
  6-2. Open Properties, Change **Copy to Output Directory** value to `PreserveNewest`

---

## Usage

You could refer to gui-example code for example.

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
