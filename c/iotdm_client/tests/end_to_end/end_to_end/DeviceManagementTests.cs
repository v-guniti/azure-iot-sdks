using EndToEndTests.Helpers;
using Microsoft.Azure.Devices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace EndToEndTests
{
    [TestClass]
    public class DeviceManagementTests
    {
        const int oneMinute = 1000 * 60;
        static readonly string connectionString = Environment.GetEnvironmentVariable("IOTHUB_DM_CONNECTION_STRING");

        DeviceIdentity device_;
        ClientEventHandler events_;
        Client client_;

        [TestInitialize]
        public void BeforeEachTest()
        {
            device_ = new DeviceIdentity(connectionString);
            events_ = new ClientEventHandler();
            client_ = new Client(device_.ConnectionString(), events_);
        }

        [TestCleanup]
        public void AfterEachTest()
        {
            client_.Stop();
            device_.Remove();
        }

        [TestMethod, Timeout(3 * oneMinute)]
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

            // first, wait for the client to respond to expected Observe requests
            bool sentAllNotifies = false;
            while (!sentAllNotifies)
            {
                Thread.Sleep(1000);

                var actual = events_.store.Keys.Where(key => key.StartsWith("notify.")).Select(key => key.Split('.')[1]).OrderBy(key => key);
                var expected = expectedResources.Keys.OrderBy(val => val);

                sentAllNotifies = expected.SequenceEqual(actual);
            }

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
