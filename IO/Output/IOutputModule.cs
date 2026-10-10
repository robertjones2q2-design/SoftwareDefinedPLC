using SoftwareDefinedPlc.IO;
using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.IO.Output
{
    public interface IOutputModule : IIoModule
    {
        /// <summary>Drive field devices from the output image.</summary>
        void WriteFrom(IDataMemory outputImage);
    }
}