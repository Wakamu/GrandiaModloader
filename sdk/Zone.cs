namespace Grandia.Sdk;

public sealed class Zone
{
    public Zone(int dest)
    {
        Dest = dest;
    }

    public int Dest { get; }

    /// <summary>Full assembler <c>zone …</c> line, without the replace/add prefix.</summary>
    public string? Line { get; set; }

    public bool Dirty { get; set; }

    public bool Append { get; set; }
}
