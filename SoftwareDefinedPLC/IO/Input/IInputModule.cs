using SoftwareDefinedPlc.IO;
using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.IO.Input
{
    public interface IInputModule : IIoModule
    {
        /// <summary>Sample field devices and copy values into the input image.</summary>
        void ReadInto(IDataMemory inputImage);
    }
}