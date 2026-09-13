#include "dialogue.h"

#include "clr_host.h"
#include "hook_util.h"
#include "log.h"
#include "map_apply.h"

#include <Windows.h>

#include <cstdint>
#include <cstring>
#include <vector>

#if defined(_M_IX86)
extern "C" {
void* g_mod_dialogue_resume = nullptr;
void* g_mod_dialogue_continue = nullptr;
volatile std::uint32_t g_mod_dialogue_new_edi = 0;
volatile std::uint32_t g_mod_dialogue_new_esi = 0;
volatile std::uint32_t g_mod_dialogue_skip = 0;
void ModDialogueDispatchDetour();
void ModDialogueEpilogueDetour();
}
#endif

namespace grandia_mod {
namespace {

constexpr std::uintptr_t kCmdDispatchRva = 0x6F174u;
constexpr std::uintptr_t kCmdJumpTableRva = 0x6F654u;
constexpr std::uintptr_t kCmdContinueRva = 0x6F367u;
constexpr std::uintptr_t kType1EpilogueRva = 0x6F5B8u;
constexpr std::uintptr_t kType8EpilogueRva = 0x6F63Fu;
constexpr std::uintptr_t kWorkingIpRva = 0x31438Cu;
constexpr std::uintptr_t kModeByteRva = 0x318BF1u;
constexpr std::uintptr_t kScnPtrRva = 0x31CD30u;
constexpr std::uintptr_t kOfsPtrRva = 0x31CD4Cu;
constexpr std::size_t kDispatchPatch = 7;
constexpr std::size_t kEpiloguePatch = 7;
constexpr std::uint8_t kDispatchPrefix[3] = {0xFF, 0x24, 0x85};
constexpr std::uint8_t kContinuePrefix[2] = {0x8A, 0x3D};
constexpr std::uint8_t kEpilogueExpect[7] = {0x5F, 0x5E, 0x5B, 0x8B, 0xE5, 0x5D, 0xC3};

void* g_dispatch_site = nullptr;
void* g_dispatch_tramp = nullptr;
std::uint8_t g_dispatch_original[8]{};
void* g_type1_site = nullptr;
std::uint8_t g_type1_original[8]{};
void* g_type8_site = nullptr;
std::uint8_t g_type8_original[8]{};

std::uint16_t g_script_id = 0;
std::uint32_t g_script_ip = 0;
std::uint32_t g_restore_ip = 0;
std::vector<void*> g_payloads;

void FreePayloads() {
    for (void* p : g_payloads) {
        if (p) {
            VirtualFree(p, 0, MEM_RELEASE);
        }
    }
    g_payloads.clear();
}

void CopyStem(char* dest, const char* src) {
    if (!dest) {
        return;
    }
    dest[0] = 0;
    if (!src) {
        return;
    }
    std::size_t i = 0;
    for (; src[i] && i + 1 < 16; ++i) {
        dest[i] = src[i];
    }
    dest[i] = 0;
}

bool PayloadReadable(std::uintptr_t addr, unsigned len) {
    if (addr < 0x10000u || len == 0 || len > kDialogueMaxPayload) {
        return false;
    }
    std::uint8_t probe = 0;
    return SafeReadByte(addr, &probe) && SafeReadByte(addr + len - 1, &probe);
}

bool ReadU16(std::uintptr_t addr, std::uint16_t* out) {
    std::uint8_t lo = 0;
    std::uint8_t hi = 0;
    if (!SafeReadByte(addr, &lo) || !SafeReadByte(addr + 1, &hi)) {
        return false;
    }
    *out = static_cast<std::uint16_t>(lo | (hi << 8));
    return true;
}

unsigned Type6Extra(std::uint16_t word, std::uint16_t payload_word) {
    const unsigned hi = static_cast<unsigned>(payload_word) >> 14;
    if (hi == 0) {
        return 0;
    }
    unsigned n = (word & 0xFu) + 1u;
    if (hi == 1 && (n & 1u) != 0) {
        n += 1;
    }
    return n << (hi - 1);
}

unsigned OpSize(std::uint32_t ip, std::uint16_t word) {
    const int nibble = static_cast<int>(word >> 12);
    if (nibble == 0) {
        return 2;
    }
    const int type = nibble - 1;
    if (type == 1 || type == 8) {
        return 2u + (word & 0x0FFFu);
    }
    if (type == 3) {
        return (word & 0x800u) ? 6u : 4u;
    }
    if (type == 4 || type == 7) {
        return 4;
    }
    if (type == 5) {
        return 6;
    }
    if (type == 6) {
        std::uint16_t pw = 0;
        if (!ReadU16(ip + 2, &pw)) {
            return 2;
        }
        return 4u + Type6Extra(word, pw);
    }
    return 2;
}

std::uint32_t ResolveScriptStart(std::uint16_t script_id) {
    if (script_id == 0 || script_id == 0xFFFFu) {
        return 0;
    }
    const auto base = ModuleBase();
    if (base == 0) {
        return 0;
    }
    std::uint32_t scn = 0;
    std::uint32_t ofs = 0;
    if (!SafeReadU32(base + kScnPtrRva, &scn) || !SafeReadU32(base + kOfsPtrRva, &ofs)) {
        return 0;
    }
    if (scn < 0x10000u || ofs < 0x10000u) {
        return 0;
    }
    for (int i = 0; i < 4096; ++i) {
        std::uint16_t id = 0;
        std::uint16_t off = 0;
        const auto row = ofs + static_cast<std::uint32_t>(i) * 4u;
        if (!ReadU16(row, &id) || !ReadU16(row + 2, &off)) {
            return 0;
        }
        if (id == 0xFFFFu) {
            return 0;
        }
        if (id == script_id) {
            return scn + off;
        }
    }
    return 0;
}

int DialogueOrdinal(std::uint32_t start, std::uint32_t payload_ip) {
    if (start == 0 || payload_ip < start + 2) {
        return -1;
    }
    const auto op = payload_ip - 2;
    auto ip = start;
    int n = 0;
    for (int guard = 0; guard < 4096 && ip + 2 <= op; ++guard) {
        std::uint16_t word = 0;
        if (!ReadU16(ip, &word)) {
            return -1;
        }
        const int type = static_cast<int>(word >> 12) - 1;
        if (type == 1 || type == 8) {
            ++n;
        }
        const unsigned size = OpSize(ip, word);
        if (size == 0) {
            return -1;
        }
        ip += size;
    }
    return n;
}

}  // namespace

void NoteScriptArm(std::uint16_t script_id, std::uint32_t ip) {
    g_script_id = script_id;
    g_script_ip = ip != 0 ? ip : ResolveScriptStart(script_id);
}

void RaiseDialogueDispatch(unsigned type_idx, unsigned word, unsigned ip_edi) {
#if defined(_M_IX86)
    g_mod_dialogue_new_edi = 0;
    g_mod_dialogue_new_esi = 0;
    g_mod_dialogue_skip = 0;
#endif
    if (type_idx != 1u && type_idx != 8u) {
        return;
    }
    const unsigned src_len = word & 0x0FFFu;
    if (!PayloadReadable(ip_edi, src_len)) {
        return;
    }

    DialogueNative req{};
    CopyStem(req.stem, CurrentMapStem());
    req.script_id = g_script_id;
    if (g_script_ip == 0) {
        g_script_ip = ResolveScriptStart(g_script_id);
    }
    req.op_index = DialogueOrdinal(g_script_ip, ip_edi);
    req.kind = static_cast<std::int32_t>(type_idx);
    req.src = ip_edi;
    req.src_len = static_cast<std::int32_t>(src_len);
    req.dest_len = 0;
    req.skip = 0;
    if (RuntimeOnDialogue(&req) != 0) {
        return;
    }
    if (req.skip != 0) {
        g_restore_ip = 0;
        const auto base = ModuleBase();
        if (base != 0) {
            SafeWriteU32(base + kWorkingIpRva, ip_edi + src_len);
        }
#if defined(_M_IX86)
        if (g_mod_dialogue_continue != nullptr) {
            g_mod_dialogue_skip = 1;
        } else {
            LogWarn("OnDialogue Skip ignored (decoder continue +0x%X unavailable)",
                    static_cast<unsigned>(kCmdContinueRva));
        }
#endif
        return;
    }
    if (req.dest_len <= 0) {
        return;
    }
    auto dest_len = static_cast<unsigned>(req.dest_len);
    if (dest_len == 0 || dest_len > 0x0FFFu) {
        return;
    }
    auto* heap = VirtualAlloc(nullptr, dest_len + 16u, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!heap) {
        return;
    }
    std::memcpy(heap, req.dest, dest_len);
    g_payloads.push_back(heap);
    g_restore_ip = ip_edi + src_len;
    const auto heap_ip = static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(heap));
    const auto base = ModuleBase();
    if (base != 0) {
        SafeWriteU32(base + kWorkingIpRva, heap_ip);
    }
#if defined(_M_IX86)
    g_mod_dialogue_new_edi = heap_ip;
    g_mod_dialogue_new_esi = (word & 0xF000u) | (dest_len & 0x0FFFu);
#endif
}

void RaiseDialogueContinue() {
    if (g_restore_ip == 0) {
        return;
    }
    const auto base = ModuleBase();
    if (base != 0) {
        SafeWriteU32(base + kWorkingIpRva, g_restore_ip);
    }
    g_restore_ip = 0;
}

bool InstallDialogueHook() {
#if !defined(_M_IX86)
    return false;
#else
    const auto base = ModuleBase();
    if (base == 0) {
        return false;
    }
    auto* site = reinterpret_cast<std::uint8_t*>(base + kCmdDispatchRva);
    if (!IsExecutableAddress(site) || site[0] != kDispatchPrefix[0] || site[1] != kDispatchPrefix[1] ||
        site[2] != kDispatchPrefix[2]) {
        LogWarn("OnDialogue dispatch +0x%X site mismatch", static_cast<unsigned>(kCmdDispatchRva));
        return false;
    }
    const auto disp = *reinterpret_cast<std::uint32_t*>(site + 3);
    if (disp != static_cast<std::uint32_t>(base + kCmdJumpTableRva)) {
        LogWarn("OnDialogue jump table mismatch");
        return false;
    }
    g_dispatch_tramp = MakeTrampoline(site, kDispatchPatch, site + kDispatchPatch);
    if (!g_dispatch_tramp) {
        LogWarn("OnDialogue dispatch trampoline failed");
        return false;
    }
    g_mod_dialogue_resume = g_dispatch_tramp;
    auto* cont = reinterpret_cast<std::uint8_t*>(base + kCmdContinueRva);
    const auto mode_va = static_cast<std::uint32_t>(base + kModeByteRva);
    if (IsExecutableAddress(cont) && cont[0] == kContinuePrefix[0] && cont[1] == kContinuePrefix[1] &&
        *reinterpret_cast<std::uint32_t*>(cont + 2) == mode_va) {
        g_mod_dialogue_continue = cont;
    } else {
        g_mod_dialogue_continue = nullptr;
        LogWarn("OnDialogue Skip continue +0x%X site mismatch", static_cast<unsigned>(kCmdContinueRva));
    }
    if (!WriteJump(site, reinterpret_cast<void*>(&ModDialogueDispatchDetour), g_dispatch_original,
                   kDispatchPatch)) {
        VirtualFree(g_dispatch_tramp, 0, MEM_RELEASE);
        g_dispatch_tramp = nullptr;
        g_mod_dialogue_resume = nullptr;
        g_mod_dialogue_continue = nullptr;
        LogWarn("OnDialogue dispatch hook failed");
        return false;
    }
    g_dispatch_site = site;

    auto install_epilogue = [&](std::uintptr_t rva, void** site_out, std::uint8_t* original) -> bool {
        auto* epi = reinterpret_cast<std::uint8_t*>(base + rva);
        if (!IsExecutableAddress(epi) || !BytesMatch(epi, kEpilogueExpect, kEpiloguePatch)) {
            LogWarn("OnDialogue epilogue +0x%X site mismatch", static_cast<unsigned>(rva));
            return false;
        }
        if (!WriteJump(epi, reinterpret_cast<void*>(&ModDialogueEpilogueDetour), original,
                       kEpiloguePatch)) {
            LogWarn("OnDialogue epilogue +0x%X hook failed", static_cast<unsigned>(rva));
            return false;
        }
        *site_out = epi;
        return true;
    };
    if (!install_epilogue(kType1EpilogueRva, &g_type1_site, g_type1_original) ||
        !install_epilogue(kType8EpilogueRva, &g_type8_site, g_type8_original)) {
        RemoveDialogueHook();
        return false;
    }

    return true;
#endif
}

void RemoveDialogueHook() {
    if (g_dispatch_site) {
        RestoreBytes(g_dispatch_site, g_dispatch_original, kDispatchPatch);
        g_dispatch_site = nullptr;
    }
    if (g_type1_site) {
        RestoreBytes(g_type1_site, g_type1_original, kEpiloguePatch);
        g_type1_site = nullptr;
    }
    if (g_type8_site) {
        RestoreBytes(g_type8_site, g_type8_original, kEpiloguePatch);
        g_type8_site = nullptr;
    }
    if (g_dispatch_tramp) {
        VirtualFree(g_dispatch_tramp, 0, MEM_RELEASE);
        g_dispatch_tramp = nullptr;
    }
#if defined(_M_IX86)
    g_mod_dialogue_resume = nullptr;
    g_mod_dialogue_continue = nullptr;
    g_mod_dialogue_skip = 0;
#endif
    FreePayloads();
}

}  // namespace grandia_mod

#if defined(_M_IX86)
extern "C" void ModOnDialogueDispatch(unsigned type_idx, unsigned word, unsigned ip_edi) {
    grandia_mod::RaiseDialogueDispatch(type_idx, word, ip_edi);
}

extern "C" void ModOnDialogueContinue() {
    grandia_mod::RaiseDialogueContinue();
}

extern "C" __declspec(naked) void ModDialogueDispatchDetour() {
    __asm {
        pushad
        mov eax, dword ptr [esp + 28]
        mov ecx, dword ptr [esp + 4]
        mov edx, dword ptr [esp]
        push edx
        push ecx
        push eax
        call ModOnDialogueDispatch
        add esp, 12
        popad
        cmp dword ptr [g_mod_dialogue_skip], 0
        je dialogue_no_skip
        cmp dword ptr [g_mod_dialogue_continue], 0
        je dialogue_no_skip
        jmp dword ptr [g_mod_dialogue_continue]
    dialogue_no_skip:
        cmp dword ptr [g_mod_dialogue_new_edi], 0
        je dialogue_keep_edi
        mov edi, dword ptr [g_mod_dialogue_new_edi]
    dialogue_keep_edi:
        cmp dword ptr [g_mod_dialogue_new_esi], 0
        je dialogue_keep_esi
        mov esi, dword ptr [g_mod_dialogue_new_esi]
        mov ecx, dword ptr [g_mod_dialogue_new_esi]
    dialogue_keep_esi:
        jmp dword ptr [g_mod_dialogue_resume]
    }
}

extern "C" __declspec(naked) void ModDialogueEpilogueDetour() {
    // Type-1/8 ret from the decoder (pop edi/esi/ebx; leave; ret).
    // Restore SCN IP after the handler wrote working_ip = heap + dest_len.
    __asm {
        pushad
        call ModOnDialogueContinue
        popad
        pop edi
        pop esi
        pop ebx
        mov esp, ebp
        pop ebp
        ret
    }
}
#endif
