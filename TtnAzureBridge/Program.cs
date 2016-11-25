using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Common.Exceptions;
using Newtonsoft.Json;
using System;
using System.Configuration;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace TtnAzureBridge
{
    internal class Program
    {
        private static DeviceClientList _deviceClientList;

        private static RegistryManager _registryManager;

        private static MqttClient _mqttClient;

        /// <summary>
        /// Execute all logic
        /// </summary>
        /// <param name="args"></param>
        private static void Main(string[] args)
        {
            ConstructDeviceList();

            ConstructIoTHubInfrastructure();

            StartMqttConnection();

            while (true)
            {
                Thread.Sleep(10000);
            }
        }

        /// <summary>
        /// Construct a device list for unique device handling
        /// </summary>
        private static void ConstructDeviceList()
        {
            _deviceClientList = new DeviceClientList(Convert.ToInt32(ConfigurationManager.AppSettings["RemoveDevicesAfterMinutes"]));

            _deviceClientList.DeviceRemoved += (sender, message) =>
            {
                Console.Write(message);
            };

            _deviceClientList.IoTHubMessageReceived += (sender, message) =>
            {
                Console.Write("IoT Hub Downlink");

                var payload = Convert.ToBase64String(message.Bytes);
                var jsonMessage = "{\"payload\":\"" + payload + "\", \"port\": 1, \"ttl\": \"1h\"}";

                Console.Write($"; Uploaded: {jsonMessage}");

                var encoding = Encoding.UTF8;
                var bytes = encoding.GetBytes(jsonMessage);

                var mqttResult =
                 _mqttClient.Publish(
                     $"{ConfigurationManager.AppSettings["ApplicationEui"]}/devices/{message.DeviceId}/down",
                     bytes,
                     MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE,
                     false);

                Console.Write($" - Id {mqttResult}");

                Console.WriteLine();
            };
        }

        /// <summary>
        /// Connect to Azure IoT Hub
        /// </summary>
        private static void ConstructIoTHubInfrastructure()
        {
            _registryManager = RegistryManager.CreateFromConnectionString(ConfigurationManager.ConnectionStrings["IoTHub"].ConnectionString);

            Console.Write($"time {DateTime.Now} -> ");

            Console.WriteLine($"IoT Hub {ConfigurationManager.AppSettings["IotHubName"]} connected");
        }

        /// <summary>
        /// Open MQTT connection
        /// </summary>
        private static void StartMqttConnection()
        {
            _mqttClient = new MqttClient(ConfigurationManager.AppSettings["BrokerHostName"]);

            _mqttClient.Subscribe(
                new[] { ConfigurationManager.AppSettings["Topic"] },
                new[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });

            _mqttClient.ConnectionClosed += Client_ConnectionClosed;

            _mqttClient.MqttMsgSubscribed += Client_MqttMsgSubscribed;

            _mqttClient.MqttMsgPublishReceived += Client_MqttMsgPublishReceived;

            _mqttClient.MqttMsgPublished += _mqttClient_MqttMsgPublished;

            byte response;

            if (!string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["KeepAlivePeriod"]))
            {
                var keepAlivePeriod = Convert.ToUInt16(ConfigurationManager.AppSettings["KeepAlivePeriod"]);

                response = _mqttClient.Connect(
                    Guid.NewGuid().ToString(),
                    ConfigurationManager.AppSettings["ApplicationEui"],
                    ConfigurationManager.AppSettings["ApplicationAccessKey"],
                    true,
                    keepAlivePeriod);

                Console.WriteLine($"MQTT KeepAlivePeriod is {keepAlivePeriod}");
            }
            else
            {
                response = _mqttClient.Connect(
                    Guid.NewGuid().ToString(),
                    ConfigurationManager.AppSettings["ApplicationEui"],
                    ConfigurationManager.AppSettings["ApplicationAccessKey"]);
            }

            if (response != 0)
            {
                Console.WriteLine("Mqtt connection failed. Check TTN credentials.");
            }
        }

        /// <summary>
        /// Log MQTT publish
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void _mqttClient_MqttMsgPublished(object sender, MqttMsgPublishedEventArgs e)
        {
            Console.WriteLine($"MQTT handling downlink Id {e.MessageId} published: {e.IsPublished}");
        }

        /// <summary>
        /// Publish MQTT message
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static async void Client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            Console.WriteLine("MQTT handling uplink");

            // Convert message to json

            var jsonText = Encoding.ASCII.GetString(e.Message);

            dynamic jsonMessage = JsonConvert.DeserializeObject(jsonText);

            // Get id of device

            var deviceId = (string)jsonMessage.dev_eui;

            // Create or get device

            var device = await AddDeviceAsync(deviceId);

            if (device.Status != DeviceStatus.Enabled)
            {
                Console.WriteLine($"Device {deviceId} disabled");

                return;
            }

            // extract data from json

            var counter = jsonMessage.counter.ToString();
            var deviceMessage = jsonMessage.fields.ToString();

            var metaText = jsonMessage.metadata.ToString();
            var jsonMeta = JsonConvert.DeserializeObject(metaText);

            var time = jsonMeta[0].server_time.ToString();
            var gatewayEui = jsonMeta[0].gateway_eui.ToString();
            var latitude = jsonMeta[0].latitude.ToString();
            var longitude = jsonMeta[0].longitude.ToString();
            var rssi = jsonMeta[0].rssi.ToString();
            var frequency = jsonMeta[0].frequency.ToString();

            // construct message for IoT Hub

            dynamic iotHubMessage = JsonConvert.DeserializeObject(deviceMessage);

            //      iotHubMessage.deviceId = deviceId;
            //      iotHubMessage.time = time;

            var iotHubMessageString = JsonConvert.SerializeObject(iotHubMessage);

            Console.WriteLine($"IoT Hub message ({counter}/{gatewayEui}/{latitude}/{longitude}/{rssi}/{frequency}): {iotHubMessageString}");

            // create device client

            string key;

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

            var message = new Microsoft.Azure.Devices.Client.Message(Encoding.UTF8.GetBytes(iotHubMessageString));

            await deviceClient.SendEventAsync(message);

            Console.WriteLine("IoT Hub message sent");
        }

        /// <summary>
        /// Log MQTT client subscription
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Client_MqttMsgSubscribed(object sender, MqttMsgSubscribedEventArgs e)
        {
            Console.WriteLine($"MQTT subscribed to {ConfigurationManager.AppSettings["ApplicationEui"]} on {ConfigurationManager.AppSettings["BrokerHostName"]}");
        }

        private static void Client_ConnectionClosed(object sender, EventArgs e)
        {
            Console.Write($"time {DateTime.Now} -> ");

            Console.Write("MQTT connection closed.");

            if (ConfigurationManager.AppSettings["ExitOnConnectionClosed"].ToUpper() == "TRUE")
            {
                Console.WriteLine(" Exit for restart.");

                Environment.Exit(1);
            }

            Console.Write(" No exit.");
        }

        /// <summary>
        /// Add a device to the IoT Hub registry
        /// </summary>
        /// <param name="deviceId"></param>
        /// <returns>Microsoft.Azure.Devices.Device</returns>
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