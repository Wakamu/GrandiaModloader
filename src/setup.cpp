#include "setup.h"

#include "hook_util.h"
#include "log.h"

#include <Windows.h>

#include <cstdint>

namespace grandia_mod {
namespace {

LONG CALLBACK CrashLogVeh(EXCEPTION_POINTERS* info) {
    if (!info || !info->ExceptionRecord) {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    const DWORD code = info->ExceptionRecord->ExceptionCode;
    if (code != 0xC0000005 && code != 0xC000001D && code != 0xC00000FD) {
        return EXCEPTION_CONTINUE_SEARCH;
    }

    // First-chance AVs from SafeRead / other SEH in this DLL. Logging them
    // fopen+fflush's GrandiaMod.log on the hot path and stalls the game.
    MEMORY_BASIC_INFORMATION fault_mbi{};
    MEMORY_BASIC_INFORMATION self_mbi{};
    const void* fault_at = info->ExceptionRecord->ExceptionAddress;
    if (VirtualQuery(fault_at, &fault_mbi, sizeof(fault_mbi)) != 0 &&
        VirtualQuery(reinterpret_cast<void*>(&CrashLogVeh), &self_mbi, sizeof(self_mbi)) != 0 &&
        fault_mbi.AllocationBase != nullptr &&
        fault_mbi.AllocationBase == self_mbi.AllocationBase) {
        return EXCEPTION_CONTINUE_SEARCH;
    }

    static volatile LONG s_av_logs = 0;
    const LONG seen = InterlockedIncrement(&s_av_logs);
    if (seen == 16) {
        LogWarn("AV logger silenced after 16 reports");
    }
    if (seen > 16) {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    const auto base = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
    const auto at = reinterpret_cast<std::uintptr_t>(info->ExceptionRecord->ExceptionAddress);
    const auto* ctx = info->ContextRecord;
    const ULONG_PTR fault = info->ExceptionRecord->NumberParameters >= 2
                                ? info->ExceptionRecord->ExceptionInformation[1]
                                : 0;
    std::uintptr_t ret = 0;
    std::uintptr_t frame = ctx ? ctx->Ebp : 0;
    std::uint32_t ret32 = 0;
    if (SafeReadU32(frame + 4, &ret32)) {
        ret = ret32;
    }
    LogWarn("AV at %p rva=+0x%X code=%08X fault=%08X eax=%08X ecx=%08X edx=%08X esi=%08X edi=%08X ebp=%08X ret=+0x%X",
            info->ExceptionRecord->ExceptionAddress,
            static_cast<unsigned>(at >= base ? at - base : 0),
            static_cast<unsigned>(code), static_cast<unsigned>(fault),
            ctx ? ctx->Eax : 0, ctx ? ctx->Ecx : 0, ctx ? ctx->Edx : 0,
            ctx ? ctx->Esi : 0, ctx ? ctx->Edi : 0, ctx ? ctx->Ebp : 0,
            static_cast<unsigned>(CallerRva(ret)));
    unsigned stk[5]{};
    unsigned n = 0;
    for (; n < 5; ++n) {
        std::uint32_t next = 0;
        std::uint32_t caller = 0;
        if (!SafeReadU32(frame, &next) || !SafeReadU32(frame + 4, &caller) || next < 0x10000u) {
            break;
        }
        stk[n] = static_cast<unsigned>(CallerRva(caller));
        frame = next;
    }
    if (n > 0) {
        LogWarn("AV stack +0x%X +0x%X +0x%X +0x%X +0x%X", stk[0], stk[1], stk[2], stk[3], stk[4]);
    }
    return EXCEPTION_CONTINUE_SEARCH;
}

}  // namespace

bool InstallSetupHooks() {
    AddVectoredExceptionHandler(1, &CrashLogVeh);
    return true;
}

void RemoveSetupHooks() {}

}  // namespace grandia_mod
