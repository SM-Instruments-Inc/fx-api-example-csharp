using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FxApiCSharp.DataTypes;

public class Camera(string ipAddress, string userName = "admin", string password = "admin", int port = 80, int rtspPort = 8554)
{
    public string IpAddress { get; } = ipAddress;
    public int Port { get; } = port;
    public int RtspPort { get; } = rtspPort;
    public string UserName { get; } = userName; 
    public string Password { get; } = password;

    public string RtspUri => $"rtsp://{IpAddress}:{RtspPort}/raw";

    public Camera() : this(null, default, default, default, default) { } // For Serialization / Deserialization
}
