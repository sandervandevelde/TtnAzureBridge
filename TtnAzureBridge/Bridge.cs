using Microsoft.Azure.Devices;
using Newtonsoft.Json;
using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace TtnAzureBridge
{
    public class Bridge
    {
        private DeviceClientList _deviceClientList;

        private RegistryManager _registryManager;

        private MqttClient _mqttClient;

        private WhiteList _whiteList;

        private readonly int _removeDevicesAfterMinutes;

        private readonly string _applicationId;

        private readonly string _deviceKeyKind;

        private readonly string _brokerHostName;

        private readonly string _exitOnConnectionClosed;

        private readonly string _topic;

        private readonly ushort? _keepAlivePeriod;

        private readonly string _applicationAccessKey;

        private readonly string _iotHub;

        private readonly string _iotHubName;

        private readonly string _silentRemoval;

        private readonly string _whiteListFileName;

        private readonly bool _addGatewayInfo;

        public Bridge(int removeDevicesAfterMinutes, string applicationId, string iotHub, string iotHubName, string topic, string brokerHostName, ushort? keepAlivePeriod, string applicationAccessKey, string deviceKeyKind, string exitOnConnectionClosed, string silentRemoval, string whiteListFileName, bool addGatewayInfo)
        {
            _removeDevicesAfterMinutes = removeDevicesAfterMinutes;

            _applicationId = applicationId;

            _deviceKeyKind = deviceKeyKind;

            _brokerHostName = brokerHostName;

            _exitOnConnectionClosed = exitOnConnectionClosed;

            _topic = topic;

            _keepAlivePeriod = keepAlivePeriod;

            _applicationAccessKey = applicationAccessKey;

            _iotHub = iotHub;

            _iotHubName = iotHubName;

            _silentRemoval = silentRemoval;

            _whiteListFileName = whiteListFileName;

            _addGatewayInfo = addGatewayInfo;
        }

        public void Start()
        {
            ConstructDeviceList(_silentRemoval);

            ConstructIoTHubInfrastructure();

            ConstructWhiteList(_whiteListFileName);

            StartMqttConnection();
        }

        /// <summary>
        /// Construct a device list for unique device handling
        /// </summary>
        private void ConstructDeviceList(string silentRemoval)
        {
            _deviceClientList = new DeviceClientList(_iotHubName, _removeDevicesAfterMinutes);

            _deviceClientList.DeviceRemoved += (sender, message) =>
            {
                var silent = silentRemoval.ToUpper() == "TRUE";

                if (!silent)
                {
                    WriteLine(message);
                }
            };

            _deviceClientList.IoTHubMessageReceived += (sender, message) =>
            {
                Write($"{DateTime.Now:HH:mm:ss} IoT Hub Downlink");

                var payload = Convert.ToBase64String(message.Bytes);
                var jsonMessage = "{\"payload_raw\":\"" + payload + "\", \"port\": 1}";

                Write($"; Uploaded: {jsonMessage}");

                var encoding = Encoding.UTF8;
                var bytes = encoding.GetBytes(jsonMessage);

                var mqttResult =
                 _mqttClient.Publish(
                     $"{_applicationId}/devices/{message.DeviceId}/down",
                     bytes,
                     MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE,
                     false);

                WriteLine($" - Id {mqttResult}");
            };
        }

        public void ConstructWhiteList(string fileName)
        {
            _whiteList = new WhiteList();

            var count = _whiteList.Load(fileName);

            switch (count)
            {
                case -1:
                    WriteLine("No whitelist filtering");
                    break;

                case 0:
                    WriteLine("Whitelist is empty");
                    break;

                default:
                    WriteLine($"Whitelist contains {count} entries");
                    break;
            }
        }

        /// <summary>
        /// Connect to Azure IoT Hub
        /// </summary>
        private void ConstructIoTHubInfrastructure()
        {
            _registryManager = RegistryManager.CreateFromConnectionString(_iotHub);

            Write($"time {DateTime.Now} -> ");

            WriteLine($"IoT Hub {_iotHubName} connected");
        }

        /// <summary>
        /// Open MQTT connection
        /// </summary>
        private void StartMqttConnection()
        {
            _mqttClient = new MqttClient(_brokerHostName);

            _mqttClient.Subscribe(
                new[] { _topic },
                new[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE });

            _mqttClient.ConnectionClosed -= Client_ConnectionClosed;
            _mqttClient.ConnectionClosed += Client_ConnectionClosed;

            _mqttClient.MqttMsgSubscribed -= Client_MqttMsgSubscribed;
            _mqttClient.MqttMsgSubscribed += Client_MqttMsgSubscribed;

            _mqttClient.MqttMsgPublishReceived -= Client_MqttMsgPublishReceived;
            _mqttClient.MqttMsgPublishReceived += Client_MqttMsgPublishReceived;

            _mqttClient.MqttMsgPublished -= mqttClient_MqttMsgPublished;
            _mqttClient.MqttMsgPublished += mqttClient_MqttMsgPublished;

            _mqttClient.MqttMsgUnsubscribed -= mqttClient_MqttMsgUnsubscribed;
            _mqttClient.MqttMsgUnsubscribed += mqttClient_MqttMsgUnsubscribed;

            byte response;

            if (_keepAlivePeriod.HasValue)
            {
                response = _mqttClient.Connect(
                    Guid.NewGuid().ToString(),
                    _applicationId,
                    _applicationAccessKey,
                    true,
                    _keepAlivePeriod.Value);

                WriteLine($"MQTT KeepAlivePeriod is {_keepAlivePeriod}");
            }
            else
            {
                response = _mqttClient.Connect(
                    Guid.NewGuid().ToString(),
                    _applicationId,
                    _applicationAccessKey);
            }

            if (response != 0)
            {
                WriteLine("Mqtt connection failed. Check TTN credentials.");
            }
        }

        /// <summary>
        /// Log MQTT publish
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void mqttClient_MqttMsgPublished(object sender, MqttMsgPublishedEventArgs e)
        {
            WriteLine($"MQTT handling downlink Id {e.MessageId} published: {e.IsPublished}");
        }

        /// <summary>
        /// Publish MQTT message
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            // Get id of device

            var deviceId = e.Topic.Split('/')[2];

            if (e.Message.Length < 200)
            {
                // ignore rogue messages

                return;
            }

            WriteLine("MQTT handling uplink");

            // Create or get device

            var device = await AddDeviceAsync(deviceId);

            if (!_whiteList.Accept(deviceId))
            {
                WriteLine($"Device {deviceId} is not whitelisted");

                return;
            }

            if (device.Status != DeviceStatus.Enabled)
            {
                WriteLine($"Device {deviceId} disabled");

                return;
            }

            // Convert message to json

            var jsonText = Encoding.UTF8.GetString(e.Message);

            dynamic jsonObject = JsonConvert.DeserializeObject(jsonText);

            var counter = jsonObject.counter?.ToString();
            var deviceMessage = jsonObject.payload_fields?.ToString();

            if (string.IsNullOrEmpty(deviceMessage))
            {
                WriteLine($"Device {deviceId} seen");

                return;
            }

            var metadata = jsonObject.metadata;
            var frequency = metadata?.frequency?.ToString();
            var gateway = metadata?.gateways?[0];
            var gatewayEui = gateway?.gtw_id?.ToString();
            var latitude = gateway?.latitude?.ToString();
            var longitude = gateway?.longitude?.ToString();
            var rssi = gateway?.rssi?.ToString();

            // construct message for IoT Hub

            dynamic iotHubMessage = JsonConvert.DeserializeObject(deviceMessage);

            if (_addGatewayInfo)
            {
                iotHubMessage.ttnCounter = counter;
                iotHubMessage.ttnGatewayEui = gatewayEui;
                iotHubMessage.ttnGatewayLat = latitude;
                iotHubMessage.ttnGatewayLon = longitude;
                iotHubMessage.ttnGatewayFreq = frequency;
                iotHubMessage.ttnGatewayRssi = rssi;
            }
            
            var iotHubMessageString = JsonConvert.SerializeObject(iotHubMessage);

            Write($"{DateTime.Now:HH:mm:ss} Message received ({counter}/{deviceId}/{gatewayEui}/{latitude}/{longitude}/{frequency}/{rssi}): {iotHubMessageString}");

            // create device client

            var key = (_deviceKeyKind == "Primary")
                ? device.Authentication.SymmetricKey.PrimaryKey
                : device.Authentication.SymmetricKey.SecondaryKey;

            var deviceClient = _deviceClientList.GetDeviceClient(deviceId, key);

            // send message

            var message = new Microsoft.Azure.Devices.Client.Message(Encoding.UTF8.GetBytes(iotHubMessageString));

            await deviceClient.SendEventAsync(message);

            WriteLine("-IoT Hub message sent");
        }

        /// <summary>
        /// Log MQTT client subscription
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Client_MqttMsgSubscribed(object sender, MqttMsgSubscribedEventArgs e)
        {
            WriteLine($"MQTT subscribed to {_applicationId} on {_brokerHostName}");
        }

        private void mqttClient_MqttMsgUnsubscribed(object sender, MqttMsgUnsubscribedEventArgs e)
        {
            Write($"time {DateTime.Now} -> ");

            Write($"MQTT unsubscribed to {_applicationId} on {_brokerHostName};");

            if (_exitOnConnectionClosed.ToUpper() == "TRUE")
            {
                WriteLine(" Exit for restart");

                Environment.Exit(1);
            }

            Write(" No exit");
        }

        private void Client_ConnectionClosed(object sender, EventArgs e)
        {
            Write($"time {DateTime.Now} -> ");

            Write("MQTT connection closed");

            if (_exitOnConnectionClosed.ToUpper() == "TRUE")
            {
                WriteLine(" Exit for restart");

                Environment.Exit(1);
            }

            Write(" No exit");
        }

        /// <summary>
        /// Add a device to the IoT Hub registry
        /// </summary>
        /// <param name="deviceId"></param>
        /// <returns>Microsoft.Azure.Devices.Device</returns>
        private async Task<Device> AddDeviceAsync(string deviceId)
        {
            Device device;

            {
                try
                {
                    device = await _registryManager.AddDeviceAsync(new Device(deviceId));

                    WriteLine($"Device {deviceId} added");
                }
                catch (Microsoft.Azure.Devices.Common.Exceptions.DeviceAlreadyExistsException)
                {
                    // there are actually two different DeviceAlreadyExistsException exceptions. We react on the right one.

                    device = await _registryManager.GetDeviceAsync(deviceId);
                }
            }

            return device;
        }

        private void Write(string message)
        {
            Notified?.Invoke(this, message);
        }

        private void WriteLine(string message)
        {
            LineNotified?.Invoke(this, message);
        }

        public event EventHandler<string> Notified;

        public event EventHandler<string> LineNotified;
    }
}