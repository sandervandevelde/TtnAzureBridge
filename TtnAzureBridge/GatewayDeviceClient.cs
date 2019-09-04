using Microsoft.Azure.Devices.Client;
using System;
using System.Threading;

namespace TtnAzureBridge
{
    public class GatewayDeviceClient
    {
        public DeviceClient DeviceClient { get; set; }

        public Thread Thread { get; set; }

        public DateTime DateTimeLastVisit { get; set; }
    }
}