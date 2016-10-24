# TTN Azure Gateway

## Introduction

This is a TTN bridge for Microsoft Azure IoT Hub, written in C#. It supports both uplink and downlink. New devices are added to the Iot Hub automatically. Devices are checked for being disabled.

Just download the sourcecode and fill in the following application settings:

  <appSettings>
    <add key="BrokerHostName" value="staging.thethingsnetwork.org" />
    <add key="Username" value="[TTN App EUI]" />
    <add key="Password" value="[TTN App Access Key]" />
    <add key="DeviceKeyKind" value="Primary" />
    <add key="Topic" value="#" />
    <add key="IotHubName" value ="[iothub name]" />
    <add key="ConnectionString" value="HostName=[iothub name].azure-devices.net;SharedAccessKeyName=iothubowner;SharedAccessKey=[shared access key]" />
  </appSettings>

*Note: This Gateway connects to TTN apps, added to https://staging.thethingsnetwork.org/applications*

## Gateway output

The gateway should support both uplink and downlink

![alt tag](img/Gateway.png)

*Note: downlink is only supported by real TTN gateways, not package forwarders.* 

## Microsoft Azure IoT Hub Explorer - Uplink

In the next picture, uplink is shown:

![alt tag](img/IotHubExplorer-uplink.png)

## Microsoft Azure IoT Hub Explorer - Downlink

In the next picture, downlink is shown:

![alt tag](img/IotHubExplorer-downlink.png)