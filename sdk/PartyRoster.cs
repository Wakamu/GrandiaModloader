namespace Grandia.Sdk;

/// <summary>
/// Four party slots (index 0..3). Out-of-range writes are ignored.
/// Empty slots are packed toward the front when the host applies the roster.
/// </summary>
public sealed class PartyRoster
{
    public const int SlotCount = GameParty.SlotCount;

    private readonly int[] _ids = new int[SlotCount];

    public PartyRoster(int[]? party = null)
    {
        CopyFrom(party);
    }

    public int Length => SlotCount;

    public int this[int slot]
    {
        get => slot is >= 0 and < SlotCount ? _ids[slot] : 0;
        set
        {
            if (slot is >= 0 and < SlotCount)
            {
                _ids[slot] = value;
            }
        }
    }

    /// <summary>Replace slots 0..3. Extra values are ignored; missing slots become empty.</summary>
    public void Set(params int[] ids) => CopyFrom(ids);

    private void CopyFrom(int[]? party)
    {
        Array.Clear(_ids);
        if (party is null || party.Length == 0)
        {
            return;
        }

        var n = Math.Min(SlotCount, party.Length);
        Array.Copy(party, _ids, n);
    }
}
