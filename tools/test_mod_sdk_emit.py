"""Assert field_patch output from the hub BGM cue has script 0x3000 catalog 0x01."""
from __future__ import annotations

import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(r"C:\Users\User\Projects\Grandipelago\Grandipelago\tools")))

from field_script_asm import format_script
from field_script_disasm import parse_directory
from field_script_ir import CameraArgs, parse_file_to_ir


def main() -> int:
    cache = REPO / "overlay" / "_emit_smoke"
    scn = cache / "TEXT" / "EN" / "CC15.SCN"
    ofs = cache / "TEXT" / "EN" / "CC15.OFS"
    if not scn.is_file() or not ofs.is_file():
        print("run: dotnet run --project tools/EmitSmoke/EmitSmoke.csproj -c Release")
        return 1
    bank = scn.read_bytes()
    directory = ofs.read_bytes()
    sf = parse_file_to_ir("CC15", "scn", bank, directory, parse_directory(directory, len(bank)))
    enter = next(s for s in sf.scripts if s.script_id == 0x3000)
    print(format_script(enter))
    cams = [op.args for op in enter.ops if hasattr(op, "args") and isinstance(op.args, CameraArgs)]
    assert [(c.op, c.scene_id) for c in cams] == [("overlay", 0x01), ("deactivate_overlay", 0x01)]
    print("ok CC15.SCN script 0x3000 is Parm catalog 0x01")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
