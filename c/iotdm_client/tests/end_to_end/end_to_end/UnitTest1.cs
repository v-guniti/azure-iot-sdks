using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Concurrent;
using System.Threading;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Azure.Devices;
using System.Linq;
using System.Collections.Generic;

namespace EndToEndTests
{
    class DeviceIdentity
    {
        Device device_;
        RegistryManager registry_;
        private readonly string deviceConnectionString_;

        public DeviceIdentity(string connectionString)
        {
            var id = "SimpleSample_" + Guid.NewGuid().ToString();

            // Add a new device to IoT Hub
            registry_ = RegistryManager.CreateFromConnectionString(connectionString);
            Task<Device> newDevice = registry_.AddDeviceAsync(new Device(id));
            newDevice.Wait();
            device_ = newDevice.Result;

            Console.WriteLine("Registered device: {0}", device_.Id);

            // create a device connection string
            string hostName = connectionString.Split(';')[0];
            deviceConnectionString_ = String.Format("{0};DeviceId={1};SharedAccessKey={2}",
                hostName, device_.Id, device_.Authentication.SymmetricKey.PrimaryKey);
        }

        ~DeviceIdentity()
        {
            try
            {
                registry_.RemoveDeviceAsync(device_).Wait();
                Console.WriteLine("*Unregistered device: {0}", device_.Id);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        public string ConnectionString()
        {
            return deviceConnectionString_;
        }

        public void Refresh()
        {
            Task<Device> deviceTask = registry_.GetDeviceAsync(device_.Id);
            deviceTask.Wait();

            device_ = deviceTask.Result;
        }

        public string GetSystemProperty(string name)
        {
            if (device_.SystemProperties.ContainsKey(name))
            {
                return device_.SystemProperties[name].Value.ToString();
            }

            Console.WriteLine("{0} not found in SystemProperties", name);
            return String.Empty;
        }
    }

    class RegistrationFailedEventArgs : EventArgs
    {
        public readonly string Message;

        public RegistrationFailedEventArgs(string message)
        {
            Message = message;
        }
    }

    class ExecutedResourceEventArgs : EventArgs
    {
        public readonly string Name;

        public ExecutedResourceEventArgs(string name)
        {
            Name = name;
        }
    }

    class ProcessedOutputEventArgs : EventArgs
    {
        public readonly string Name;
        public readonly string Value;

        public ProcessedOutputEventArgs(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    class ClientRegistrationFailedException : UnitTestAssertException
    {
        public ClientRegistrationFailedException(string message) : base(message) {}
    }

    interface IClientEvents
    {
        void HandleRegistered(object sender, EventArgs e);
        void HandleRegistrationFailed(object sender, RegistrationFailedEventArgs e);
        void HandleResourceRead(object sender, ProcessedOutputEventArgs e);
        void HandleResourceWritten(object sender, ProcessedOutputEventArgs e);
        void HandleResourceExecuted(object sender, ExecutedResourceEventArgs e);
        void HandleClientSentNotifyMessage(object sender, ProcessedOutputEventArgs e);
    }

    delegate void RegistrationFailedEventHandler(object sender, RegistrationFailedEventArgs e);
    delegate void ProcessedOutputEventHandler(object sender, ProcessedOutputEventArgs e);
    delegate void ExecutedResourceEventHandler(object sender, ExecutedResourceEventArgs e);

    class Client
    {
        const string clientPath = "..\\..\\..\\..\\..\\..\\..\\..\\cmake-azure-iot-sdks\\iotdm_client\\samples\\iotdm_simple_sample\\Debug\\iotdm_simple_sample.exe";
        // C:\Users\damonb\projects\azure-iot-sdks\c\iotdm_client\tests\end_to_end\end_to_end\bin\Debug\end_to_end.dll

        readonly string connectionString_;
        IClientEvents events_;
        Process process_;

        public Client(string deviceConnectionString, IClientEvents clientEvents)
        {
            connectionString_ = deviceConnectionString;
            events_ = clientEvents;

            // wanted to ensure events are registered BEFORE process started; this seemed the simplest way
            this.RegisteredEvent += events_.HandleRegistered;
            this.RegistrationFailed += events_.HandleRegistrationFailed;
            this.ResourceReadEvent += events_.HandleResourceRead;
            this.ResourceWrittenEvent += events_.HandleResourceWritten;
            this.ResourceExecutedEvent += events_.HandleResourceExecuted;
            this.ClientSentNotifyMessageEvent += events_.HandleClientSentNotifyMessage;

            // start client process
            var startInfo = new ProcessStartInfo()
            {
                FileName = clientPath,
                CreateNoWindow = true,
                UseShellExecute = false,
                Arguments = connectionString_,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            // can throw file not found exception
            process_ = Process.Start(startInfo);
            if (process_ != null)
            {
                process_.EnableRaisingEvents = true;
                process_.OutputDataReceived += new DataReceivedEventHandler(OnReceivedStdout);
                process_.ErrorDataReceived += new DataReceivedEventHandler(OnReceivedStderr);

                process_.BeginOutputReadLine();
                process_.BeginErrorReadLine();
            }
        }

        ~Client()
        {
            try
            {
                process_.Kill();
                process_.WaitForExit();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            // would this happen automatically anyway?
            this.RegisteredEvent -= events_.HandleRegistered;
            this.RegistrationFailed -= events_.HandleRegistrationFailed;
            this.ResourceReadEvent -= events_.HandleResourceRead;
            this.ResourceWrittenEvent -= events_.HandleResourceWritten;
            this.ResourceExecutedEvent -= events_.HandleResourceExecuted;
            this.ClientSentNotifyMessageEvent -= events_.HandleClientSentNotifyMessage;
        }

        void OnReceivedStdout(object caller, DataReceivedEventArgs eArgs)
        {
            if (!String.IsNullOrEmpty(eArgs.Data) && eArgs.Data.StartsWith("Info: "))
            {
                if (eArgs.Data.Contains("REGISTERED"))
                {
                    OnRegisteredEvent(new EventArgs());
                }
                else
                {
                    string[] data = eArgs.Data.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (data.Length > 2)
                    {
                        // 'Info: returning <value> for <name>'
                        if (data[1].Equals("returning"))
                        {
                            string name = data[data.Length - 1];
                            string value = data[2].Trim(new char[] { '[', ']' });
                            OnResourceReadEvent(new ProcessedOutputEventArgs(name, value));
                        }
                        // 'Info: <name> being set to <value>'
                        else if (data[1].Equals("being") && data[2].Equals("set"))
                        {
                            string name = data[1];
                            string value = data[data.Length - 1];
                            OnResourceWrittenEvent(new ProcessedOutputEventArgs(name, value));
                        }
                        // 'Info: inside execute <???>'
                        else if (data[1].Equals("inside") && data[2].Equals("execute"))
                        {
                            string name = data[data.Length - 1];
                            OnResourceExecutedEvent(new ExecutedResourceEventArgs(name));
                        }
                    }
                }
            }
        }

        void OnReceivedStderr(object caller, DataReceivedEventArgs eArgs)
        {
            if (!String.IsNullOrEmpty(eArgs.Data))
            {
                if (eArgs.Data.StartsWith("Error:") && eArgs.Data.Contains("Registration FAILED"))
                {
                    Console.WriteLine("*Device failed to connect to DM channel");
                    OnRegistrationFailed(new RegistrationFailedEventArgs(eArgs.Data));
                }
                // 'Observe Update[/<object>/<instance>/<resource>]: <value-size> <value>'
                else if (eArgs.Data.StartsWith("Observe Update"))
                {
                    string[] data = eArgs.Data.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    string uri = data[1].Substring(7, data[1].Length - 9);
                    int size = Int32.Parse(data[2]);
                    string value = data[3].Substring(0, size);

                    if (uri == "/3/0/13")
                    {
                        // service returns DateTime.ToString(), so convert LWM2M time to the same format
                        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                        value = epoch.AddSeconds(Int32.Parse(value)).ToString();
                    }

                    OnClientSentNotifyMessageEvent(new ProcessedOutputEventArgs(uri, value));
                }
            }
        }

        /*public*/
        event EventHandler RegisteredEvent;
        /*public*/
        event RegistrationFailedEventHandler RegistrationFailed;
        /*public*/
        event ProcessedOutputEventHandler ResourceReadEvent;
        /*public*/
        event ProcessedOutputEventHandler ResourceWrittenEvent;
        /*public*/
        event ExecutedResourceEventHandler ResourceExecutedEvent;
        /*public*/
        event ProcessedOutputEventHandler ClientSentNotifyMessageEvent;

        protected void OnRegisteredEvent(EventArgs e)
        {
            RegisteredEvent(this, e);
        }

        protected void OnRegistrationFailed(RegistrationFailedEventArgs e)
        {
            RegistrationFailed(this, e);
        }

        protected void OnResourceReadEvent(ProcessedOutputEventArgs e)
        {
            ResourceReadEvent(this, e);
        }

        protected void OnResourceWrittenEvent(ProcessedOutputEventArgs e)
        {
            ResourceWrittenEvent(this, e);
        }

        protected void OnResourceExecutedEvent(ExecutedResourceEventArgs e)
        {
            ResourceExecutedEvent(this, e);
        }

        protected void OnClientSentNotifyMessageEvent(ProcessedOutputEventArgs e)
        {
            ClientSentNotifyMessageEvent(this, e);
        }
    }

    class ClientEvents : IClientEvents
    {
        // Event                Key             Notes
        // Registered                           sent once per client instance
        // RegistrationError                    sent once per client instance in the event of an error
        // Executed             [exec.name]     sent when a job requests that a resource be executed
        // Read                 [read.name]     sent when the client reads a resource (due to observe or read job)
        // Written              [write.name]    sent when the client updates a resource (due to underlying resource change or write job)
        // Notified             [notify.name]   sent for each observed property, at a frequency specified by write attributes

        public ManualResetEvent clientIsRegistered = new ManualResetEvent(false);
        public ConcurrentDictionary<string, string> store = new ConcurrentDictionary<string, string>();

        public void HandleRegistered(object sender, EventArgs e)
        {
            Assert.IsFalse(clientIsRegistered.WaitOne(0), "Client already registered with service");
            clientIsRegistered.Set();
        }

        public void HandleRegistrationFailed(object sender, RegistrationFailedEventArgs e)
        {
            throw new ClientRegistrationFailedException(e.Message);
        }

        public void HandleResourceRead(object sender, ProcessedOutputEventArgs e)
        {
            StoreEvent("read", e);
        }

        public void HandleResourceWritten(object sender, ProcessedOutputEventArgs e)
        {
            StoreEvent("write", e);
        }

        public void HandleResourceExecuted(object sender, ExecutedResourceEventArgs e)
        {
            var key = "exec." + e.Name;
            store[key] = String.Empty;
        }

        public void HandleClientSentNotifyMessage(object sender, ProcessedOutputEventArgs e)
        {
            StoreEvent("notify", e);
        }

        private void StoreEvent(string keyType, ProcessedOutputEventArgs e)
        {
            var key = keyType + "." + e.Name;
            store[key] = e.Value;
        }
    }

    [TestClass]
    public class DeviceManagementTests
    {
        static readonly string connectionString = Environment.GetEnvironmentVariable("IOTHUB_DM_CONNECTION_STRING");

        DeviceIdentity device_;
        ClientEvents events_;
        Client client_;

        public DeviceManagementTests()
        {
            device_ = new DeviceIdentity(connectionString);
            events_ = new ClientEvents();
            client_ = new Client(device_.ConnectionString(), events_);
        }

        [TestMethod, Timeout(600000)]
        public void IotHubAutomaticallyObservesAllReadableResources()
        {
            Dictionary<string, string> expectedResources = new Dictionary<string, string>()
            {
                { "/1/0/1",       SystemPropertyNames.RegistrationLifetime },
                { "/1/0/2",       SystemPropertyNames.DefaultMinPeriod },
                { "/1/0/3",       SystemPropertyNames.DefaultMaxPeriod },
                { "/3/0/0",       SystemPropertyNames.Manufacturer },
                { "/3/0/1",       SystemPropertyNames.ModelNumber },
                { "/3/0/2",       SystemPropertyNames.SerialNumber },
                { "/3/0/3",       SystemPropertyNames.FirmwareVersion },
                { "/3/0/9",       SystemPropertyNames.BatteryLevel },
                { "/3/0/10",      SystemPropertyNames.MemoryFree },
                { "/3/0/13",      SystemPropertyNames.CurrentTime },
                { "/3/0/14",      SystemPropertyNames.UtcOffset },
                { "/3/0/15",      SystemPropertyNames.Timezone },
                { "/3/0/17",      SystemPropertyNames.DeviceDescription },
                { "/3/0/18",      SystemPropertyNames.HardwareVersion },
                { "/3/0/20",      SystemPropertyNames.BatteryStatus },
                { "/3/0/21",      SystemPropertyNames.MemoryTotal },
                { "/5/0/3",       SystemPropertyNames.FirmwareUpdateState },
                { "/5/0/5",       SystemPropertyNames.FirmwareUpdateResult },
                { "/5/0/6",       SystemPropertyNames.FirmwarePackageName },
                { "/5/0/7",       SystemPropertyNames.FirmwarePackageVersion },
                { "/10241/0/1",   "ConfigurationName" },
                { "/10241/0/2",   "ConfigurationValue" }
            };

            // first, wait for the client to receive/respond to expected Observe requests
            bool sentAllNotifies;
            do
            {
                var actual = events_.store.Keys.Where(key => key.StartsWith("notify.")).Select(key => key.Split('.')[1]).OrderBy(key => key);
                var expected = expectedResources.Keys.OrderBy(val => val);

                sentAllNotifies = expected.SequenceEqual(actual);
                Thread.Sleep(1000);
            }
            while (!sentAllNotifies);

            // next, compare client values to server values
            Thread.Sleep(10000);

            device_.Refresh();

            foreach(var res in expectedResources)
            {
                Assert.AreEqual(events_.store["notify." + res.Key], device_.GetSystemProperty(res.Value));
            }
        }
    }
}
