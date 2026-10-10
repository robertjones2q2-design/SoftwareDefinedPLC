using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.Communications
{
    public abstract class CommunicationInterface : ICommunicationInterface
    {
        public abstract string Protocol { get; }
        public bool IsConnected { get; protected set; }
        public abstract void Service(IDataMemory memory);
    }
}