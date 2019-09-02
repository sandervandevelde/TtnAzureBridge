using System;
using System.Configuration;
using System.Threading;

namespace TtnAzureBridge
{
    internal class Program
    {
        /// <summary>
        /// Execute all logic
        /// </summary>
        /// <param name="args"></param>
        private static void Main(string[] args)
        {
            var removeDevicesAfterMinutes = Convert.ToInt32(ConfigurationManager.AppSettings["RemoveDevicesAfterMinutes"]);

            var applicationId = ConfigurationManager.AppSettings["ApplicationId"];

            var applicationAccessKey = ConfigurationManager.AppSettings["ApplicationAccessKey"];

            var iotHubConnectionString = ConfigurationManager.ConnectionStrings["IoTHub"].ConnectionString;

            var shortIotHubName = ConfigurationManager.AppSettings["ShortIotHubName"];

            var brokerHostName = ConfigurationManager.AppSettings["BrokerHostName"];

            ushort? keepAlivePeriod;
            if (!string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["KeepAlivePeriod"]))
            {
                keepAlivePeriod = Convert.ToUInt16(ConfigurationManager.AppSettings["KeepAlivePeriod"]);
            }
            else
            {
                keepAlivePeriod = null;
            }

            var topic = ConfigurationManager.AppSettings["Topic"];

            var deviceKeyKind = ConfigurationManager.AppSettings["DeviceKeyKind"];

            var exitOnConnectionClosed = ConfigurationManager.AppSettings["ExitOnConnectionClosed"];

            var silentRemoval = ConfigurationManager.AppSettings["SilentRemoval"];

            var whiteListFileName = ConfigurationManager.AppSettings["WhiteListFileName"];

            var addGatewayInfo = bool.Parse(ConfigurationManager.AppSettings["AddGatewayInfo"]);

            var bridge = new Bridge(removeDevicesAfterMinutes, applicationId, iotHubConnectionString, shortIotHubName, topic, brokerHostName,
                keepAlivePeriod, applicationAccessKey, deviceKeyKind, exitOnConnectionClosed, silentRemoval, whiteListFileName, addGatewayInfo);

            bridge.Notified += (sender, message) =>
            {
                Console.Write(message);
            };

            bridge.LineNotified += (sender, message) =>
            {
                Console.WriteLine(message);
            };

            bridge.Start();

            while (true)
            {
                Thread.Sleep(10000);
            }
        }
    }
}