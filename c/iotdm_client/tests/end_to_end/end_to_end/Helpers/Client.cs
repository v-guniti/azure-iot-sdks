using System;
using System.Diagnostics;

namespace EndToEndTests.Helpers
{
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
            Stop();
        }

        public void Stop()
        {
            if (process_ == null) return;

            RegisteredEvent -= events_.HandleRegistered;
            RegistrationFailed -= events_.HandleRegistrationFailed;
            ResourceReadEvent -= events_.HandleResourceRead;
            ResourceWrittenEvent -= events_.HandleResourceWritten;
            ResourceExecutedEvent -= events_.HandleResourceExecuted;
            ClientSentNotifyMessageEvent -= events_.HandleClientSentNotifyMessage;

            try
            {
                process_.Kill();
                process_.WaitForExit();
                process_ = null;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
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
                        else if (data[2].Equals("being") && data[3].Equals("set"))
                        {
                            string name = data[1];
                            string value = data[data.Length - 1].Trim(new char[] { '[', ']' });
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
}