using System;
using System.Collections.Generic;
using System.Linq;
 
namespace SoftwareDefinedPlc.LogicPrograms
{
    public interface ILogicProgram
    {
        string Name { get; }
        ProgramLanguage Language { get; }
        void Execute(IDataMemory inputImage, IDataMemory outputImage, IDataMemory internalMemory);
    }
}