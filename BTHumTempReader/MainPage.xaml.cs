using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using Windows.Storage.Streams;
using Windows.Devices.Bluetooth.Advertisement;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409
namespace BTHumTempReader
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        // The BLE watcher object
        private BluetoothLEAdvertisementWatcher watcher;

        // Constant for the manufacturer ID, in this case for the BBW200-A1 sensor
        private const ushort MANUFACTURER_ID = 0x0D;

        public MainPage()
        {
            this.InitializeComponent();

            // Create and initialize the BLE watcher
            watcher = new BluetoothLEAdvertisementWatcher();
            
            // Create a manufacturer filter to listen to only this specific BLE device
            // This is for Smart Temperature & Humidity Sensor BBW200-A1
            var manufacturerData = new BluetoothLEManufacturerData();
            manufacturerData.CompanyId = MANUFACTURER_ID;

            // Add the filter to the watcher to receive advertisements only from this manufacturer
            watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(manufacturerData);
        }


        /// <summary>
        /// Invoked when this page is about to be displayed in a Frame.
        ///
        /// We will enable/disable parts of the UI if the device doesn't support it.
        /// </summary>
        /// <param name="eventArgs">Event data that describes how this page was reached. The Parameter
        /// property is typically used to configure the page.</param>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            // Attach a handler to process the received advertisement. 
            // The watcher cannot be started without a Received handler attached
            watcher.Received += OnAdvertisementReceived;

            // Attach a handler to process watcher stopping due to various conditions,
            // such as the Bluetooth radio turning off or the Stop method was called
            watcher.Stopped += OnAdvertisementWatcherStopped;

            // Attach handlers for suspension to stop the watcher when the App is suspended.
            App.Current.Suspending += App_Suspending;
            App.Current.Resuming += App_Resuming;

            // Start the watcher
            watcher.Start();

            // Inform the user
            statusText.Text = "Status: Started.";
        }


        /// <summary>
        /// Invoked when application execution is being suspended.  Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// of memory still intact.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private void App_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            // Make sure to stop the watcher on suspend.
            watcher.Stop();
            // Always unregister the handlers to release the resources to prevent leaks.
            watcher.Received -= OnAdvertisementReceived;
        }

        /// <summary>
        /// Invoked when application execution is being resumed.
        /// </summary>
        /// <param name="sender">The source of the resume request.</param>
        /// <param name="e"></param>
        private void App_Resuming(object sender, object e)
        {
            watcher.Received += OnAdvertisementReceived;
        }

        /// <summary>
        /// Invoked as an event handler when an advertisement is received.
        /// </summary>
        /// <param name="watcher">Instance of watcher that triggered the event.</param>
        /// <param name="eventArgs">Event data containing information about the advertisement event.</param>
        private async void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementReceivedEventArgs eventArgs)
        {
            double temperature = 0.0;
            uint humidity = 0;
            
            // Check if there are any manufacturer-specific sections.
            var manufacturerSections = eventArgs.Advertisement.ManufacturerData;
            if (manufacturerSections.Count > 0)
            {
                // Only use the first one of the list
                BluetoothLEManufacturerData manufacturerData = manufacturerSections[0];
                byte[] data = new byte[manufacturerData.Data.Length];
                using (var reader = DataReader.FromBuffer(manufacturerData.Data))
                {
                    reader.ReadBytes(data);
                }

                // Parse data: temperature consists of bytes 3 & 4 in the payload, humidity is only byte 6
                // Parsing and interpreting is pretty experimental, because the payload data is not documented anywhere
                // However this seems to give correct results
                byte[] humBytes = { 0, data[5] };
                byte[] tempBytes = { data[3], data[2] };

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(humBytes);
                    Array.Reverse(tempBytes);
                }

                temperature = (BitConverter.ToUInt16(tempBytes, 0)) / 10.0;
                humidity = BitConverter.ToUInt16(humBytes, 0);
            }

            // Serialize UI update to the main UI thread
            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                // Display these information on the list
                //listBox.Items.Add(string.Format("Temperature: {0} \u00B0C, humidity {1}%", temperature, humidity));
                tempBox.Text = string.Format("{0} \u00B0C", temperature);
                humBox.Text = string.Format("{0}%", humidity);
            });
        }

        /// <summary>
        /// Invoked as an event handler when the watcher is stopped or aborted.
        /// </summary>
        /// <param name="watcher">Instance of watcher that triggered the event.</param>
        /// <param name="eventArgs">Event data containing information about why the watcher stopped or aborted.</param>
        private async void OnAdvertisementWatcherStopped(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementWatcherStoppedEventArgs eventArgs)
        {
            // Notify the user that the watcher was stopped, this can happen for example if Bluetooth radio is turned off
            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                statusText.Text = string.Format("Status: {0}", eventArgs.Error);
            });
        }
    }
}
