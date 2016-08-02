# BTHumTempReader
Very simple Bluetooth LE application using Windows 10 UWP APIs. Interfaces with BeeWi BBW200 temperature &amp; humidity sensor over BLE.

Demonstration of using the new Bluetooth LE APIs in Windows 10 UWP. This application registers a Bluetooth LE watcher for listening BLE advertisements from BBW200 BLE device. No pairing is necessary. Parsing of the advertisement payload data is more like an educated guess since there is no documentation available.

Functionality in this version:
- Listens BLE advertisements in the foreground when application is started
- Displays temperature and humidity values from the latest advertisement received

Problems solved:
- In the code everything was supposed to be OK, watcher started and listener registered and still I didn't receive any advertisements compared to the Microsoft demo. Finally I realized you need to declare the application to be using Bluetooth. This is done in Package.appxmanifest by adding Bluetooth to the Capabilities.

Resources used:
- [Microsoft's official Windows 10 samples] (https://github.com/Microsoft/Windows-universal-samples/tree/master/Samples/BluetoothAdvertisement)
- [MSDN documentation] (https://msdn.microsoft.com/en-us/library/windows/apps/xaml/windows.devices.bluetooth.advertisement.bluetoothleadvertisementwatcher.aspx)
- [//build/ 2016 BLE session] (https://channel9.msdn.com/Events/Build/2015/3-739)
- [Raspberry Pi community work for the BBW200-A1 sensor] (https://www.raspberrypi.org/forums/viewtopic.php?t=139848&p=970411)
- [BeeWi BBW200 product page] (http://www.bee-wi.com/bbw200,us,4,BBW200-A1.cfm) (bought mine from Amazon.co.uk)
