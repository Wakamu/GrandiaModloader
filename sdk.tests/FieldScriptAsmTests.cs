using Xunit;

namespace Grandia.Sdk.Tests;

public class FieldScriptAsmTests
{
    [Fact]
    public void Disassemble_lifts_flag_gate_to_if_block()
    {
        const string flat = """
            script 0x0001
            flag_ctx_begin
            skip_if_clear 0x001A
            skip_if_set 0x001B
            flag_ctx_end
            jump L1
            yield
            label L1
            yield
            """;
        var bytes = FieldScriptAsm.Assemble(flat);
        var dump = FieldScriptAsm.Disassemble(bytes, 0x0001);
        Assert.Contains("if clear 0x001A and set 0x001B -> L0000 {", dump);
        Assert.Contains("  yield", dump);
        Assert.DoesNotContain("flag_ctx_begin", dump);
        Assert.DoesNotContain("skip_if_clear", dump);
        Assert.Equal(bytes, FieldScriptAsm.Assemble(dump));
    }

    [Fact]
    public void Disassemble_keeps_if_plus_jump_when_label_is_missing()
    {
        const string src = """
            script 0x0001
            flag_ctx_begin
            skip_if_clear 0x001A
            flag_ctx_end
            jump Missing skip=0x10
            yield
            """;
        var dump = FieldScriptAsm.Disassemble(FieldScriptAsm.Assemble(src), 0x0001);
        Assert.Contains("if clear 0x001A", dump);
        Assert.Contains("skip=0x10", dump);
        Assert.DoesNotContain("-> ", dump);
        Assert.Matches(@"jump X[0-9A-Fa-f]+ skip=0x10", dump);
    }

    [Fact]
    public void Disassemble_lifts_menu_and_pick_rungs()
    {
        const string src = """
            script 0x0001
            menu {
              say type1
                [menu]
                *Yes*
                *No*
              pick 0 -> L0
                yield
            }
            yield
            """;
        var bytes = FieldScriptAsm.Assemble(src);
        var dump = FieldScriptAsm.Disassemble(bytes, 0x0001);
        Assert.Contains("menu {", dump);
        Assert.Contains("pick 0 -> L0", dump);
        Assert.Equal(bytes, FieldScriptAsm.Assemble(dump));
    }

    [Fact]
    public void Disassemble_standalone_choice_rung_is_if_pick()
    {
        const string src = """
            script 0x0001
            flag_ctx_begin
            branch word=0x4C40 arg=0x4000 extra=0000
            flag_ctx_end
            jump L0
            yield
            label L0
            yield
            """;
        var dump = FieldScriptAsm.Disassemble(FieldScriptAsm.Assemble(src), 0x0001);
        Assert.Contains("if pick == 0", dump);
        Assert.Contains("  jump L0", dump);
        Assert.DoesNotContain("menu {", dump);
    }
}
