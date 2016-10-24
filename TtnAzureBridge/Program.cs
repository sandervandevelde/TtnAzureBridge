using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Common.Exceptions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using Message = Microsoft.Azure.Devices.Client.Message;

namespace TtnAzureBridge
{
    internal class Program
    {
        private static DeviceClientList _deviceClientList;

        private static RegistryManager _registryManager;

        private static MqttClient _mqttClient;

        private static void Main(string[] args)
        {
            ConstructDeviceList();

            ConstructIoTHubInfrastructure();

            StartMqttConnection();

            while (true)
            {
                Thread.Sleep(1000);
            }
        }

        private static void ConstructDeviceList()
        {
            _deviceClientList = new DeviceClientList();

            _deviceClientList.IoTHubMessageReceived += (sender, message) =>
            {
                Console.Write("IoT Hub Downlink");

                foreach (var messageByte in message.Bytes)
                {
                    Console.Write($" {(int)messageByte}");
                }

                var mqttResult =
                    _mqttClient.Publish(
                        $"{ConfigurationManager.AppSettings["Username"]}/devices/{message.DeviceId}/down",
                        message.Bytes,
                        MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE,
                        false);

                Console.Write($" - Id {mqttResult}");

                Console.WriteLine();
            };
        }

        private static void ConstructIoTHubInfrastructure()
        {
            _registryManager = RegistryManager.CreateFromConnectionString(ConfigurationManager.AppSettings["ConnectionString"]);

            Console.WriteLine($"IoT Hub {ConfigurationManager.AppSettings["IotHubName"]} connected");
        }

        private static void StartMqttConnection()
        {
            _mqttClient = new MqttClient(ConfigurationManager.AppSettings["BrokerHostName"]);

            _mqttClient.Subscribe(new string[] { ConfigurationManager.AppSettings["Topic"] }, new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });

            _mqttClient.ConnectionClosed += Client_ConnectionClosed;

            _mqttClient.MqttMsgSubscribed += Client_MqttMsgSubscribed;

            _mqttClient.MqttMsgPublishReceived += Client_MqttMsgPublishReceived;

            _mqttClient.MqttMsgPublished += _mqttClient_MqttMsgPublished;

            _mqttClient.Connect(Guid.NewGuid().ToString(), ConfigurationManager.AppSettings["Username"], ConfigurationManager.AppSettings["Password"]);
        }

        private static void _mqttClient_MqttMsgPublished(object sender, MqttMsgPublishedEventArgs e)
        {
            Console.WriteLine($"MQTT handling downlink Id {e.MessageId} published: {e.IsPublished}");
        }

        private static async void Client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            Console.WriteLine("MQTT handling uplink");

            // Convert message to json

            var jsonText = Encoding.ASCII.GetString(e.Message);

            dynamic jsonMessage = JsonConvert.DeserializeObject(jsonText);

            // Get id of device

            var deviceId = (string)jsonMessage.dev_eui;

            // Create or get device

            var device = (Device)await AddDeviceAsync(deviceId);

            if (device.Status != DeviceStatus.Enabled)
            {
                Console.WriteLine($"Device {deviceId} disabled");

                return;
            }

            // extract data from json

            var deviceMessage = jsonMessage.fields.ToString();

            var metaText = jsonMessage.metadata.ToString();

            var jsonMeta = JsonConvert.DeserializeObject(metaText);

            var time = jsonMeta[0].server_time.ToString();

            // construct message for IoT Hub

            dynamic iotHubMessage = JsonConvert.DeserializeObject(deviceMessage);

            iotHubMessage.deviceId = deviceId;

            iotHubMessage.time = time;

            string iotHubMessageString = JsonConvert.SerializeObject(iotHubMessage);

            Console.WriteLine($"IoT Hub message {iotHubMessageString}");

            // create device client

            var key = string.Empty;

            if (ConfigurationManager.AppSettings["DeviceKeyKind"] == "Primary")
            {
                key = device.Authentication.SymmetricKey.PrimaryKey;
            }
            else
            {
                key = device.Authentication.SymmetricKey.SecondaryKey;
            }

            var deviceClient = _deviceClientList.GetDeviceClient(deviceId, key);

            // send message

            var message = new Message(Encoding.ASCII.GetBytes(iotHubMessageString));

            await deviceClient.SendEventAsync(message);

            Console.WriteLine("IoT Hub message sent");
        }

        private static void Client_MqttMsgSubscribed(object sender, MqttMsgSubscribedEventArgs e)
        {
            Console.WriteLine($"MQTT client {ConfigurationManager.AppSettings["Username"]} on {ConfigurationManager.AppSettings["BrokerHostName"]} subscribed");
        }

        private static void Client_ConnectionClosed(object sender, EventArgs e)
        {
            Console.WriteLine("MQTT connection closed");
        }

        private static async Task<Device> AddDeviceAsync(string deviceId)
        {
            Device device;

            {
                try
                {
                    device = await _registryManager.AddDeviceAsync(new Device(deviceId));

                    Console.WriteLine($"Device {deviceId} added");
                }
                catch (DeviceAlreadyExistsException)
                {
                    device = await _registryManager.GetDeviceAsync(deviceId);

                    Console.WriteLine($"Device {deviceId} already exists");
                }
            }

            return device;
        }
    }
}