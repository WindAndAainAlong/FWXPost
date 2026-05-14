using System.Collections.Generic;

namespace PostProcessor.Core.IR;

public sealed class ToolpathProgram
{
    public ToolpathProgram(string programName, IReadOnlyList<IRBlock> blocks)
    {
        ProgramName = programName ?? string.Empty;
        Blocks = blocks ?? new List<IRBlock>();
    }

    public string ProgramName { get; }
    public IReadOnlyList<IRBlock> Blocks { get; }
}
