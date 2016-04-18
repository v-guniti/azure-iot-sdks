using Microsoft.Azure.Devices;
using System;
using System.Threading.Tasks;

namespace EndToEndTests.Helpers
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
            Remove();
        }

        public void Remove()
        {
            if (device_ == null) return;

            try
            {
                registry_.RemoveDeviceAsync(device_).Wait();
                Console.WriteLine("Unregistered device: {0}", device_.Id);
                device_ = null;
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
            if (device_ == null) return;

            Task<Device> deviceTask = registry_.GetDeviceAsync(device_.Id);
            deviceTask.Wait();

            device_ = deviceTask.Result;
        }

        public string GetSystemProperty(string name)
        {
            if (device_ == null) return String.Empty;

            if (device_.SystemProperties.ContainsKey(name))
            {
                return device_.SystemProperties[name].Value.ToString();
            }

            Console.WriteLine("{0} not found in SystemProperties", name);
            return String.Empty;
        }
    }
}
