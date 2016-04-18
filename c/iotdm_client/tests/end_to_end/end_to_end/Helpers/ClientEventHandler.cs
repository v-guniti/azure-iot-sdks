using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace EndToEndTests.Helpers
{
    class ClientRegistrationFailedException : UnitTestAssertException
    {
        public ClientRegistrationFailedException(string message) : base(message) { }
    }

    class ClientEventHandler : IClientEvents
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
}
