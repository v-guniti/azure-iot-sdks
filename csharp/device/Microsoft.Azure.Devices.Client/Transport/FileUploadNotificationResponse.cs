using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.Devices.Client.Transport
{
    [DataContract]
    class FileUploadNotificationResponse
    {
        [DataMember]
        public bool isSuccess { get; set; }

        [DataMember]
        public int statusCode { get; set; }

        [DataMember]
        public string statusDescription { get; set; }
    }
}
