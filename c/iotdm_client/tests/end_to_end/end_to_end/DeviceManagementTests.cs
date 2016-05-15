using EndToEndTests.Helpers;
using Microsoft.Azure.Devices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EndToEndTests
{
    [TestClass]
    public class DeviceManagementTests
    {
        const int oneMinute = 1000 * 60;
        static readonly string connectionString = Environment.GetEnvironmentVariable("IOTHUB_DM_CONNECTION_STRING");
        static readonly string simpleSamplePath = Environment.GetEnvironmentVariable("IOTHUB_DM_SIMPLESAMPLE_PATH");

        DeviceIdentity device_;
        ClientEventHandler events_;
        Client client_;

        [TestInitialize]
        public void BeforeEachTest()
        {
            device_ = new DeviceIdentity(connectionString);
            events_ = new ClientEventHandler();
            client_ = new Client(device_.ConnectionString(), simpleSamplePath, events_);

            events_.clientIsRegistered.WaitOne();
            Console.WriteLine("Client registered");
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
                { "/1/0/1",       DevicePropertyNames.RegistrationLifetime },
                { "/1/0/2",       DevicePropertyNames.DefaultMinPeriod },
                { "/1/0/3",       DevicePropertyNames.DefaultMaxPeriod },
                { "/3/0/0",       DevicePropertyNames.Manufacturer },
                { "/3/0/1",       DevicePropertyNames.ModelNumber },
                { "/3/0/2",       DevicePropertyNames.SerialNumber },
                { "/3/0/3",       DevicePropertyNames.FirmwareVersion },
                { "/3/0/9",       DevicePropertyNames.BatteryLevel },
                { "/3/0/10",      DevicePropertyNames.MemoryFree },
                { "/3/0/13",      DevicePropertyNames.CurrentTime },
                { "/3/0/14",      DevicePropertyNames.UtcOffset },
                { "/3/0/15",      DevicePropertyNames.Timezone },
                { "/3/0/17",      DevicePropertyNames.DeviceDescription },
                { "/3/0/18",      DevicePropertyNames.HardwareVersion },
                { "/3/0/20",      DevicePropertyNames.BatteryStatus },
                { "/3/0/21",      DevicePropertyNames.MemoryTotal },
                { "/5/0/3",       DevicePropertyNames.FirmwareUpdateState },
                { "/5/0/5",       DevicePropertyNames.FirmwareUpdateResult },
                { "/5/0/6",       DevicePropertyNames.FirmwarePackageName },
                { "/5/0/7",       DevicePropertyNames.FirmwarePackageVersion },
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

            Console.WriteLine("Client sent all Notify messages");

            // next, compare client values to server values
            Thread.Sleep(5000);
            device_.Refresh();

            foreach(var res in expectedResources)
            {
                Assert.AreEqual(events_.store["notify." + res.Key], device_.GetSystemProperty(res.Value));
            }

            Console.WriteLine("Service received all Notify messages");
        }

        [TestMethod, Timeout(3 * oneMinute)]
        public void IotHubCanChangeAResourceValueOnTheDevice()
        {
            const string expectedTimezone = "-10:00"; // US/Hawaii timezone

            // invoke the write job
            var jobClient = JobClient.CreateFromConnectionString(connectionString);
            var jobId = Guid.NewGuid().ToString();

            Task<JobResponse> job = jobClient.ScheduleDevicePropertyWriteAsync(jobId, device_.Id(), DevicePropertyNames.Timezone, expectedTimezone);
            job.Wait();
            Console.WriteLine("Write job scheduled");
            JobResponse response = job.Result;

            // wait for the job to complete
            while (response.Status < JobStatus.Completed)
            {
                Thread.Sleep(2000);
                job = jobClient.GetJobAsync(jobId);
                job.Wait();
                response = job.Result;
            }

            Assert.AreEqual(JobStatus.Completed, response.Status);
            Console.WriteLine("Write job completed");

            // confirm that the client received the write command
            string deviceTimezone = "write.Device_Timezone";

            bool writeMessageConfirmed = false;
            while (!writeMessageConfirmed)
            {
                Thread.Sleep(2000);
                if (events_.store.ContainsKey(deviceTimezone) &&
                    String.Equals(expectedTimezone, events_.store[deviceTimezone]))
                {
                    Console.WriteLine("Client received write command");
                    writeMessageConfirmed = true;
                }
            }

            // invoke the read job
            jobId = Guid.NewGuid().ToString();

            job = jobClient.ScheduleDevicePropertyReadAsync(jobId, device_.Id(), DevicePropertyNames.Timezone);
            job.Wait();
            Console.WriteLine("Read job scheduled");
            response = job.Result;

            // wait for the job to complete
            while (response.Status < JobStatus.Completed)
            {
                Thread.Sleep(2000);
                job = jobClient.GetJobAsync(jobId);
                job.Wait();
                response = job.Result;
            }

            Assert.AreEqual(JobStatus.Completed, response.Status);
            Console.WriteLine("Read job completed");

            // confirm that the device twin has the new property value
            device_.Refresh();

            Assert.AreEqual(expectedTimezone, device_.GetSystemProperty(DevicePropertyNames.Timezone));
        }

        [TestMethod, Timeout(3 * oneMinute)]
        public void IotHubCanRebootTheDevice()
        {
            // invoke the write job
            var jobClient = JobClient.CreateFromConnectionString(connectionString);
            var jobId = Guid.NewGuid().ToString();

            Task<JobResponse> job = jobClient.ScheduleRebootDeviceAsync(jobId, device_.Id());
            job.Wait();
            Console.WriteLine("Reboot job scheduled");
            JobResponse response = job.Result;

            // wait for the job to complete
            while (response.Status < JobStatus.Completed)
            {
                Thread.Sleep(2000);
                job = jobClient.GetJobAsync(jobId);
                job.Wait();
                response = job.Result;
            }

            Assert.AreEqual(JobStatus.Completed, response.Status);
            Console.WriteLine("Reboot job completed");

            // confirm that the client received the reboot command
            string deviceReboot = "exec.Device_Reboot";

            bool execMessageConfirmed = false;
            while (!execMessageConfirmed)
            {
                Thread.Sleep(2000);
                if (events_.store.ContainsKey(deviceReboot))
                {
                    Console.WriteLine("Client received reboot command");
                    execMessageConfirmed = true;
                }
            }
        }

        [TestMethod, Timeout(5 * oneMinute)]
        public void IotHubCanUpdateDeviceFirmware()
        {
            // invoke the firmware update job
            var jobClient = JobClient.CreateFromConnectionString(connectionString);
            var jobId = Guid.NewGuid().ToString();

            Task<JobResponse> job = jobClient.ScheduleFirmwareUpdateAsync(jobId, device_.Id(), "http://www.bing.com", new TimeSpan(1, 0, 0));
            job.Wait();
            Console.WriteLine("Firmware update job scheduled");
            JobResponse response = job.Result;

            // wait for the job to complete
            while (response.Status < JobStatus.Completed)
            {
                Thread.Sleep(2000);
                job = jobClient.GetJobAsync(jobId);
                job.Wait();
                response = job.Result;
            }

            Assert.AreEqual(JobStatus.Completed, response.Status);
            Console.WriteLine("Firmware update job completed");

            // confirm that the client received the firmware update command and set the result
            string deviceFirmwareUpdate = "exec.FirmwareUpdate_Update";
            string deviceNotifyResult = "notify./5/0/5";

            bool messagesConfirmed = false;
            while (!messagesConfirmed)
            {
                Thread.Sleep(2000);
                bool receivedCommand = events_.store.ContainsKey(deviceFirmwareUpdate);
                bool notifiedResult = events_.store.ContainsKey(deviceNotifyResult);

                if (receivedCommand)
                {
                    Console.WriteLine("Client received firmware update command");
                }

                if (notifiedResult)
                {
                    Console.WriteLine("Client set firmware update result");
                }

                messagesConfirmed = receivedCommand && notifiedResult;
            }
        }
    }
}
