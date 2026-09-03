namespace Grandia.Sdk;

public sealed class Hook
{
    public Hook(int id) => Id = id;

    public int Id { get; }

    /// <summary>Full assembler <c>hook …</c> line, without the replace/add prefix.</summary>
    public string? Line { get; set; }

    public bool Dirty { get; set; }

    /// <summary>True = table-2 <c>add</c>; false = <c>replace id=</c>.</summary>
    public bool Append { get; set; }
}
