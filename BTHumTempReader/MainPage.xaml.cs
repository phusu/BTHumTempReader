using System;
using System.IO;
using System.Text;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

using Windows.Storage.Streams;
using Windows.Devices.Bluetooth.Advertisement;

using Amazon;
using Amazon.KinesisFirehose;
using Amazon.KinesisFirehose.Model;
using Amazon.Runtime;

using System.Reflection;

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

        // AWS Kinesis Firehose client
        private IAmazonKinesisFirehose _client;

        // AWS Kinesis Firehose delivery stream name, i.e. where to send the data
        private const string AWS_DELIVERY_STREAM = "bbw200sensordata";

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

            // Create and initialize connection to cloud (Amazon Kinesis Firehose)
            InitializeAWSKinesisFirehose();
        }

        /// <summary>
        /// Initializes the connection to AWS Kinesis Firehose.
        /// </summary>
        private void InitializeAWSKinesisFirehose()
        {
            string accessKey = "foo";
            string secretKey = "bar";
            string filename = "BTHumTempReader.awskeys.txt"; // Need to add current namespace in front of the real filename

            // Read the needed keys from assembly
            // Keys reside in a separate .txt file for better security, not added as literals to source code!
            // As you cannot interact with host filesystem in Windows 10 UWP apps (other than few specific locations),
            // the file is included in the assembly unit
            // File format:
            // 1st line: accesskey
            // 2nd line: secretkey
            // Trick from: http://www.c-sharpcorner.com/UploadFile/4088a7/reading-winrt-component-embedded-resource-file-in-javascript/
            try
            {
                using (var stream = typeof(MainPage).GetTypeInfo().Assembly.GetManifestResourceStream(filename))
                {
                    using (var reader = new StreamReader(stream))
                    {
                        accessKey = reader.ReadLine();
                        secretKey = reader.ReadLine();
                    }
                }
            }
            catch (Exception e)
            {
                // Notify the user
                statusText.Text = "Error in loading secret keys for cloud connection: " + e.ToString();
                return;
            }

            AWSCredentials credentials = new BasicAWSCredentials(accessKey, secretKey);

            // Create the Kinesis Firehose Client
            _client = new AmazonKinesisFirehoseClient(credentials, RegionEndpoint.EUWest1);
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

            // Dispose the Firehose client
            if (_client != null)
                _client.Dispose();
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
            uint battery = 0;
            
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

                // Make sure we have enough data
                if (data.Length < 11)
                    return;

                // Parse data: temperature consists of bytes 3 & 4 in the payload, humidity is only byte 6, battery in byte 11
                // Parsing and interpreting is pretty experimental, because the payload data is not documented anywhere
                // However this seems to give correct results
                byte[] humBytes = { 0, data[5] };
                byte[] tempBytes = { data[3], data[2] };
                byte[] batteryBytes = { 0, data[10] };

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(humBytes);
                    Array.Reverse(tempBytes);
                    Array.Reverse(batteryBytes);
                }

                temperature = (BitConverter.ToUInt16(tempBytes, 0)) / 10.0;
                humidity = BitConverter.ToUInt16(humBytes, 0);
                battery = BitConverter.ToUInt16(batteryBytes, 0);
            }

            bool useCloud = false;

            // Serialize UI update to the main UI thread
            await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                // Display these information on the list
                tempBox.Text = string.Format("{0} \u00B0C", temperature);
                humBox.Text = string.Format("{0}%", humidity);
                batteryLabel.Text = string.Format("Battery level: {0}%", battery);

                useCloud = useCloudCheckBox.IsChecked.Value;
            });

            // Send data to AWS Kinesis Firehose if so desired by the user
            if (useCloud)
            {
                SendMessageToAWSFirehose(temperature, humidity, battery);
            }
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

        /// <summary>
        /// Sends sensor data to AWS Kinesis Firehose.
        /// </summary>
        /// <param name="temp">Temperature value</param>
        /// <param name="hum">Humidity value</param>
        /// <param name="bat">Battery level</param>
        private async void SendMessageToAWSFirehose(double temp, uint hum, uint bat)
        {
            // Check that the client is not null (might be if initialization failed)
            if (_client != null)
            {
                PutRecordRequest req = new PutRecordRequest();
                req.DeliveryStreamName = AWS_DELIVERY_STREAM;

                // Create the message
                // Message format: ISO timestamp, temperature, humidity, battery
                String data = String.Format("{0},{1},{2},{3}\n", DateTime.UtcNow.ToString("u"), temp, hum, bat);

                try
                {
                    var record = new Record
                    {
                        Data = new MemoryStream(UTF8Encoding.UTF8.GetBytes(data))
                    };

                    req.Record = record;

                    await _client.PutRecordAsync(req);
                }
                catch (Exception e)
                {
                    // Notify the user
                    await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        statusText.Text = string.Format("Error sending data to cloud: {0}", e.ToString());
                    });
                }
            }
        }
    }
}
