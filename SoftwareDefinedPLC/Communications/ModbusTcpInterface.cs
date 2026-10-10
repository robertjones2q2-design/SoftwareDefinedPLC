using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.Communications
{
    public sealed class ModbusTcpInterface : CommunicationInterface
    {
        public override string Protocol => "Modbus TCP";
        public override void Service(IDataMemory memory) { /* handle pending requests */ }
    }
}