using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.Communications
{
    public interface ICommunicationInterface
    {
        string Protocol { get; }          // "Modbus TCP", "PROFINET", ...
        bool IsConnected { get; }
        void Service(IDataMemory memory); // called during housekeeping
    }
}