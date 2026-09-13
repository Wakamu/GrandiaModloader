using System.Text;
using Grandia.Sdk;
using Xunit;

namespace Grandia.Sdk.Tests;

public class GameSaveDataTests
{
    private sealed class Progress
    {
        public int Chapter { get; set; }
        public string Name { get; set; } = "";
    }

    [Fact]
    public void Two_mods_keep_separate_keys()
    {
        var bag = new GameSaveData();
        Assert.True(bag.Set("HubBgm.track", 3));
        Assert.True(bag.Set("Other.flags", new Progress { Chapter = 2, Name = "Sue" }));

        Assert.Equal(3, bag.Get("HubBgm.track", 0));
        Assert.True(bag.TryGet<Progress>("Other.flags", out var p));
        Assert.Equal(2, p.Chapter);
        Assert.Equal("Sue", p.Name);
        Assert.Equal(2, bag.Count);
    }

    [Fact]
    public void Remove_and_null_set_drop_a_key()
    {
        var bag = new GameSaveData();
        bag.Set("A.x", 1);
        bag.Set("B.y", 2);
        Assert.True(bag.Remove("A.x"));
        Assert.False(bag.Contains("A.x"));
        Assert.True(bag.Set("B.y", null));
        Assert.False(bag.Contains("B.y"));
        Assert.Equal(0, bag.Count);
    }

    [Fact]
    public void Rejects_empty_dollar_and_oversize_keys()
    {
        var bag = new GameSaveData();
        Assert.False(bag.Set("", 1));
        Assert.False(bag.Set("$secret", 1));
        Assert.False(bag.Set(new string('k', GameSaveData.MaxKeyLength + 1), 1));
        Assert.Equal(0, bag.Count);
    }

    [Fact]
    public void Overflow_rejects_and_keeps_previous()
    {
        var bag = new GameSaveData();
        Assert.True(bag.Set("ok", 1));
        Assert.False(bag.Set("huge", new string('x', GameSaveData.MaxBytes)));
        Assert.Equal(1, bag.Get("ok", 0));
        Assert.False(bag.Contains("huge"));
    }

    [Fact]
    public void Json_roundtrip_preserves_values()
    {
        var bag = new GameSaveData();
        bag.Set("n", 42);
        bag.Set("s", "hi");
        bag.Set("b", true);
        var again = GameSaveData.Parse(bag.ExportUtf8());
        Assert.Equal(42, again.Get("n", 0));
        Assert.Equal("hi", again.Get("s", ""));
        Assert.True(again.Get("b", false));
    }

    [Fact]
    public void Legacy_binary_trailer_does_not_wipe_other_keys()
    {
        var bag = new GameSaveData();
        bag.Set("Keep.me", 9);
        var json = bag.ExportUtf8();
        bag.AbsorbSaveTrailer(json, [0x01, 0x02, 0x03]);
        Assert.Equal(9, bag.Get("Keep.me", 0));
        Assert.True(bag.Contains("Keep.me"));
    }

    [Fact]
    public void Trailer_json_overwrite_replaces_store()
    {
        var bag = new GameSaveData();
        bag.Set("Old.a", 1);
        var before = bag.ExportUtf8();
        var after = Encoding.UTF8.GetBytes("""{"New.b":7}""");
        bag.AbsorbSaveTrailer(before, after);
        Assert.False(bag.Contains("Old.a"));
        Assert.Equal(7, bag.Get("New.b", 0));
    }

    [Fact]
    public void Unchanged_trailer_keeps_live_sets()
    {
        var bag = new GameSaveData();
        bag.Set("A.x", 1);
        var snap = bag.ExportUtf8();
        bag.Set("B.y", 2);
        bag.AbsorbSaveTrailer(snap, snap);
        Assert.Equal(1, bag.Get("A.x", 0));
        Assert.Equal(2, bag.Get("B.y", 0));
    }

    [Fact]
    public void Vanilla_empty_trailer_clears_on_replace()
    {
        var bag = new GameSaveData();
        bag.Set("Old.a", 1);
        bag.ReplaceFromBytes([]);
        Assert.Equal(0, bag.Count);
    }

    [Fact]
    public void Clone_is_detached()
    {
        var bag = new GameSaveData();
        bag.Set("A.x", 1);
        var copy = bag.Clone();
        bag.Set("A.x", 2);
        Assert.Equal(1, copy.Get("A.x", 0));
        Assert.Equal(2, bag.Get("A.x", 0));
    }

    [Fact]
    public void LoadEvent_parses_snapshot()
    {
        var bag = new GameSaveData();
        bag.Set("Mod.k", 5);
        var ev = new LoadEvent(2, LoadPhase.ConfirmPeek, bag.ExportUtf8());
        Assert.Equal(5, ev.Data.Get("Mod.k", 0));
        Assert.Equal(2, ev.Slot);
    }

    [Fact]
    public void Set_accepts_anonymous_object()
    {
        var bag = new GameSaveData();
        var obj = new { Yellow = "xD", Testing = new { Where = "Here" } };
        Assert.True(bag.Set("Redux", obj));
        Assert.True(bag.Contains("Redux"));
        var json = Encoding.UTF8.GetString(bag.ExportUtf8());
        Assert.Contains("Redux", json, StringComparison.Ordinal);
        Assert.Contains("Yellow", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Game_SaveData_is_the_shared_instance()
    {
        Game.SaveData.Clear();
        Game.SaveData.Set("T.tmp", 1);
        Assert.True(ReferenceEquals(Game.SaveData, Game.SaveData));
        Assert.Equal(1, Game.SaveData.Get("T.tmp", 0));
        Game.SaveData.Clear();
    }
}
