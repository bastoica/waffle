namespace TorchLiteInstrumenter
{
    using System.Collections.Generic;
    using Mono.Cecil;

    public interface IInstrumenter
    {
        bool Instrument(IEnumerable<MethodDefinition> methods);
    }

}
