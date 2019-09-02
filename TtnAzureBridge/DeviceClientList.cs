﻿using Microsoft.Azure.Devices.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TtnAzureBridge
{
    public class DeviceClientList : Dictionary<string, GatewayDeviceClient>
    {
        private DateTime _lastRemovalOfOldDevices;

        private readonly int _removeDevicesAfterMinutes;

        private readonly string _shortIotHubName;

        public DeviceClientList(string shortIotHubName, int removeDevicesAfterMinutes)
        {
            _shortIotHubName = shortIotHubName;

            _removeDevicesAfterMinutes = removeDevicesAfterMinutes;

            _lastRemovalOfOldDevices = DateTime.Now;
        }

        public DeviceClient GetDeviceClient(string deviceId, string key)
        {
            DeviceClient deviceClient;

            if (this.ContainsKey(deviceId))
            {
                this[deviceId].DateTimeLastVisit = DateTime.Now;

                deviceClient = this[deviceId].DeviceClient;
            }
            else
            {
                var deviceConnectionString = $"HostName={_shortIotHubName}.azure-devices.net;DeviceId={deviceId};SharedAccessKey={key}";

                deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Amqp);

                // Create thread
                var starter = new ThreadStart(async () => await ThreadHandler(deviceId, deviceClient));
                var thread = new Thread(starter);
                thread.Start();

                // Keep thread in memory
                var gatewayDeviceClient = new GatewayDeviceClient
                {
                    DeviceClient = deviceClient,
                    Thread = thread,
                    DateTimeLastVisit = DateTime.Now
                };

                Add(deviceId, gatewayDeviceClient);
            }

            TryToRemoveOldDevices();

            return deviceClient;
        }

        private void TryToRemoveOldDevices()
        {
            var lastCheck = DateTime.Now.AddMinutes(-1 * _removeDevicesAfterMinutes);

            if (_lastRemovalOfOldDevices < lastCheck)
            {
                DeviceRemoved?.Invoke(this, $"Removal length: {this.Count} ");

                _lastRemovalOfOldDevices = DateTime.Now;

                for (var i = this.Count - 1; i > 0; i--)
                {
                    var item = this.ElementAt(i);

                    if (item.Value.DateTimeLastVisit < lastCheck)
                    {
                        item.Value.Thread.Abort();

                        DeviceRemoved?.Invoke(this, item.Key);

                        this.Remove(item.Key);
                    }
                }

                DeviceRemoved?.Invoke(this, $"Removal count afterwards: {this.Count} ");
            }
        }

        public event EventHandler<IotHubMessage> IoTHubMessageReceived;

        public event EventHandler<string> DeviceRemoved;

        /// <summary>
        /// Handling device threads
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="deviceClient"></param>
        /// <returns>awaitable task</returns>
        private async Task ThreadHandler(string deviceId, DeviceClient deviceClient)
        {
            while (true)
            {
                var receivedMessage = await deviceClient.ReceiveAsync();

                if (receivedMessage != null)
                {
                    if (IoTHubMessageReceived != null)
                    {
                        var bytes = receivedMessage.GetBytes();

                        await deviceClient.CompleteAsync(receivedMessage);

                        IoTHubMessageReceived(this, new IotHubMessage { DeviceId = deviceId, Bytes = bytes });
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    public class GatewayDeviceClient
    {
        public DeviceClient DeviceClient { get; set; }

        public Thread Thread { get; set; }

        public DateTime DateTimeLastVisit { get; set; }
    }

    public class IotHubMessage
    {
        public string DeviceId { get; set; }

        public byte[] Bytes { get; set; }
    }
}