#include "field_run.h"

#include "dialogue.h"
#include "hook_util.h"
#include "log.h"

#include <Windows.h>

#include <cstdint>
#include <cstring>
#include <vector>

extern "C" int ModGameStatus();
extern "C" int ModMenuIsOpen();

namespace grandia_mod {
namespace {

constexpr std::uintptr_t kArmScriptRva = 0x57550u;
constexpr std::uintptr_t kCallHookRva = 0x53560u;
constexpr std::uintptr_t kDispatchHookRva = 0x53830u;
constexpr std::uintptr_t kTokenRva = 0x314380u;
constexpr std::uintptr_t kStateRva = 0x314382u;
constexpr std::uintptr_t kArmByteRva = 0x314384u;
constexpr std::uintptr_t kPadWordRva = 0x314385u;
constexpr std::uintptr_t kLockByteRva = 0x314387u;
constexpr std::uintptr_t kIpRva = 0x314388u;
constexpr std::uintptr_t kClearByteRva = 0x314390u;
constexpr std::uintptr_t kYieldWordRva = 0x318BF2u;
constexpr std::uintptr_t kActorPtrRva = 0x318BDCu;
constexpr std::uintptr_t kBusyByteRva = 0x31CD38u;
constexpr std::uintptr_t kOfsPtrRva = 0x31CD4Cu;
constexpr std::uintptr_t kBusyFlagsRva = 0x319464u;
constexpr int kKindScriptId = 0;
constexpr int kKindScriptBytes = 1;
constexpr int kKindHookId = 2;
constexpr int kKindHookRow = 3;
constexpr int kHookRowSize = 20;
constexpr int kMaxScriptBytes = 0x40000;

volatile int g_pending = 0;
volatile int g_kind = 0;
volatile int g_id = 0;
volatile int g_table = 1;
std::vector<std::uint8_t> g_bytes;
void* g_live = nullptr;

void FreeLive() {
    if (g_live) {
        VirtualFree(g_live, 0, MEM_RELEASE);
        g_live = nullptr;
    }
}

void* KeepBytes(const std::vector<std::uint8_t>& src) {
    FreeLive();
    if (src.empty()) {
        return nullptr;
    }
    auto* mem = VirtualAlloc(nullptr, src.size(), MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!mem) {
        return nullptr;
    }
    std::memcpy(mem, src.data(), src.size());
    g_live = mem;
    return mem;
}

bool WriteU16(std::uintptr_t address, std::uint16_t value) {
    return SafeWriteByte(address, static_cast<std::uint8_t>(value & 0xFFu)) &&
           SafeWriteByte(address + 1, static_cast<std::uint8_t>((value >> 8) & 0xFFu));
}

bool ReadU16(std::uintptr_t address, std::uint16_t* out) {
    std::uint8_t lo = 0;
    std::uint8_t hi = 0;
    if (!SafeReadByte(address, &lo) || !SafeReadByte(address + 1, &hi)) {
        return false;
    }
    *out = static_cast<std::uint16_t>(lo | (hi << 8));
    return true;
}

bool OnField() {
    return ModGameStatus() == 0;
}

bool FieldIdle(std::uintptr_t base) {
    std::uint8_t busy = 1;
    if (!SafeReadByte(base + kBusyByteRva, &busy) || busy != 0) {
        return false;
    }
    if (ModMenuIsOpen() != 0) {
        return false;
    }
    std::uint32_t ofs = 0;
    if (!SafeReadU32(base + kOfsPtrRva, &ofs) || ofs == 0xFFFFFFFFu || ofs < 0x10000u) {
        return false;
    }
    std::uint16_t flags = 0;
    if (ReadU16(base + kBusyFlagsRva, &flags) && (flags & 0x8000u) != 0) {
        return false;
    }
    std::uint16_t token = 0;
    if (!ReadU16(base + kTokenRva, &token) || token != 0xFFFFu) {
        return false;
    }
    return true;
}

void ArmCustomScript(std::uintptr_t base, std::uint16_t token, std::uint32_t ip) {
    std::uint32_t actor = 0;
    if (SafeReadU32(base + kActorPtrRva, &actor) && actor >= 0x10000u) {
        std::uint8_t lock = 0;
        if (SafeReadByte(actor + 0x614u, &lock)) {
            SafeWriteByte(actor + 0x614u, static_cast<std::uint8_t>(lock & 0x7Fu));
        }
    }
    WriteU16(base + kTokenRva, token);
    WriteU16(base + kYieldWordRva, 0xFFFF);
    WriteU16(base + kStateRva, 0xFF01);
    WriteU16(base + kPadWordRva, 0x00F0);
    SafeWriteByte(base + kArmByteRva, 0xFF);
    SafeWriteByte(base + kClearByteRva, 0);
    SafeWriteByte(base + kLockByteRva, 1);
    SafeWriteU32(base + kIpRva, ip);
    NoteScriptArm(token, ip);
}

void CallArmScript(std::uint16_t script_id) {
#if defined(_M_IX86)
    auto* fn = reinterpret_cast<void(__fastcall*)(int, int)>(ModuleBase() + kArmScriptRva);
    const int id = script_id;
    const int flags = 1;
    __try {
        fn(id, flags);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        LogWarn("Game.RunScript arm +0x57550 fault id=0x%X", script_id);
    }
#else
    (void)script_id;
#endif
}

void CallHookId(int table, std::uint16_t hook_id) {
#if defined(_M_IX86)
    auto* fn = reinterpret_cast<void(__fastcall*)(int, int)>(ModuleBase() + kCallHookRva);
    const int id = hook_id;
    __try {
        fn(table, id);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        LogWarn("Game.RunHook +0x53560 fault table=%d id=%u", table, hook_id);
    }
#else
    (void)table;
    (void)hook_id;
#endif
}

void CallHookRow(int table, void* row) {
#if defined(_M_IX86)
    auto* fn = reinterpret_cast<void(__fastcall*)(int, void*)>(ModuleBase() + kDispatchHookRva);
    __try {
        fn(table, row);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        LogWarn("Game.RunHook row +0x53830 fault table=%d", table);
    }
#else
    (void)table;
    (void)row;
#endif
}

}  // namespace

int QueueFieldRun(int kind, int id, int table, const void* bytes, int len) {
    const auto base = ModuleBase();
    if (base == 0 || !OnField()) {
        LogWarn("Game.Run* ignored (not on field) kind=%d id=%d", kind, id);
        return 0;
    }
    if (kind == kKindScriptBytes || kind == kKindHookRow) {
        if (!bytes || len <= 0 || len > kMaxScriptBytes) {
            return 0;
        }
        if (kind == kKindHookRow && len != kHookRowSize) {
            return 0;
        }
        g_bytes.assign(static_cast<const std::uint8_t*>(bytes),
                       static_cast<const std::uint8_t*>(bytes) + len);
    } else {
        g_bytes.clear();
        if (id <= 0 || id > 0xFFFF) {
            return 0;
        }
    }
    g_kind = kind;
    g_id = id & 0xFFFF;
    g_table = table == 2 ? 2 : 1;
    g_pending = 1;
    LogInfo("Game.Run* queued kind=%d id=0x%X table=%d bytes=%d", kind, g_id, g_table,
            static_cast<int>(g_bytes.size()));
    return 1;
}

void TryApplyPendingFieldRun() {
    if (!g_pending) {
        return;
    }
    const auto base = ModuleBase();
    if (base == 0 || !OnField() || !FieldIdle(base)) {
        return;
    }
    const int kind = g_kind;
    const int id = g_id;
    const int table = g_table;
    auto bytes = g_bytes;
    g_pending = 0;
    if (kind == kKindScriptId) {
        LogInfo("Game.RunScript id=0x%X", id);
        CallArmScript(static_cast<std::uint16_t>(id));
        return;
    }
    if (kind == kKindScriptBytes) {
        auto* ip = KeepBytes(bytes);
        if (!ip) {
            LogWarn("Game.RunScript bytes alloc failed");
            return;
        }
        LogInfo("Game.RunScript custom token=0x%X len=%d", id, static_cast<int>(bytes.size()));
        ArmCustomScript(base, static_cast<std::uint16_t>(id == 0 ? 0xFFFE : id),
                        static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(ip)));
        return;
    }
    if (kind == kKindHookId) {
        LogInfo("Game.RunHook id=%d table=%d", id, table);
        CallHookId(table, static_cast<std::uint16_t>(id));
        return;
    }
    if (kind == kKindHookRow) {
        auto* row = KeepBytes(bytes);
        if (!row) {
            LogWarn("Game.RunHook row alloc failed");
            return;
        }
        LogInfo("Game.RunHook custom table=%d", table);
        CallHookRow(table, row);
    }
}

}  // namespace grandia_mod
