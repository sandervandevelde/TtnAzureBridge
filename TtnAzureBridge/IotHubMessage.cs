namespace TtnAzureBridge
{
    public class IotHubMessage
    {
        public string DeviceId { get; set; }

        public byte[] Bytes { get; set; }
    }
}