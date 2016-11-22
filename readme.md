# TTN Azure Bridge

## Introduction

This is a TTN bridge for Microsoft Azure IoT Hub, written in C#. It supports both uplink and downlink. New devices are added to the Iot Hub automatically. Devices are checked for being disabled.

Just download the sourcecode and fill in the following application settings:

```xml
  <appSettings>
    <add key="BrokerHostName" value="staging.thethingsnetwork.org" />
    <add key="Username" value="[TTN App EUI]" />
    <add key="Password" value="[TTN App Access Key]" />
    <add key="DeviceKeyKind" value="Primary" />
    <add key="Topic" value="#" />
    <add key="IotHubName" value ="[iothub name]" />
    <add key="ConnectionString" value="HostName=[iothub name].azure-devices.net;SharedAccessKeyName=iothubowner;SharedAccessKey=[shared access key]" />
    <add key ="ExitOnConnectionClosed" value="True" />
    <add key ="RemoveDevicesAfterMinutes" value="5" />
  </appSettings>
```

*Note: This Bridge connects to TTN apps, added to https://staging.thethingsnetwork.org/applications*

## Bridge output

The bridge should support both uplink and downlink:

![alt tag](img/Gateway.png)

*Note: downlink to the devices is supported by TTN gateways and some package forwarders (like https://github.com/bokse001/dual_chan_pkt_fwd).* 

## Microsoft Azure IoT Hub Explorer - Uplink

In the next picture, uplink is shown:

![alt tag](img/IotHubExplorer-uplink.png)

## Microsoft Azure IoT Hub Explorer - Downlink

In the next picture, downlink is shown:

![alt tag](img/IotHubExplorer-downlink.png)

## Downlink

This Bridge is capable of sending downlink messages. These are command for a specific device, coming from the Azure IoT Hub. 

When a device sends a message to bridge, a deviceclient is connected to the IoT Hub. This client is then listening for commands to be send back. In the AppSettings, you can specify how long this deviceclient will exist, using the *RemoveDevicesAfterMinutes* setting.


## MQTT Connection closed

It can happen that the MQTT connection is closed, for unknown reasons. If that happens, an event handler is executed. Using the *ExitOnConnectionClosed* setting, you can specify what will happen. 

I suggest to run this bridge as an Azure Web Job and close the application if the connection is closed. The Web Job behavior is to restart the job again after a certain amount of seconds (normally 60 seconds). You can overrule the time using the setting *WEBJOBS_RESTART_TIME* in the portal (not in the app.config).    