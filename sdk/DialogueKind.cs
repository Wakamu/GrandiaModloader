namespace Grandia.Sdk;

/// <summary>Field-script dialogue opcode kind.</summary>
public enum DialogueKind
{
    /// <summary>Type-1 portrait / spoken box (opcode nibble 2).</summary>
    Type1 = 1,

    /// <summary>Type-8 UI / action box (opcode nibble 9).</summary>
    Type8 = 8,
}
