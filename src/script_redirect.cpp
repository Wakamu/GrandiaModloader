#include "script_redirect.h"

#include "clr_host.h"
#include "dialogue.h"
#include "hook_util.h"
#include "log.h"
#include "map_apply.h"

#include <Windows.h>

#include <cstdint>
#include <cstring>

namespace grandia_mod {
namespace {

constexpr std::uintptr_t kScriptLookupRva = 0x6F0B0u;
constexpr std::size_t kScriptLookupPatch = 6u;
constexpr std::uintptr_t kScriptLookupResumeRva = 0x6F0B6u;
constexpr std::uintptr_t kCallHookRva = 0x53560u;
constexpr std::size_t kCallHookStolen = 11u;
constexpr std::uintptr_t kHookDispatchRva = 0x53830u;

void* g_script_site = nullptr;
std::uint8_t g_script_original[8]{};
void* g_hook_site = nullptr;
std::uint8_t g_hook_original[16]{};
void* g_hook_tramp_mem = nullptr;

}  // namespace

#if defined(_M_IX86)
extern "C" void* g_mod_script_lookup_resume = nullptr;
extern "C" void* g_mod_call_hook_tramp = nullptr;

extern "C" std::uint32_t __cdecl ModTryScriptRedirect(std::uint32_t script_id) {
    std::uint32_t ip = 0;
    const int rc = RuntimeOnScriptLookup(CurrentMapStem(), static_cast<std::uint16_t>(script_id), &ip);
    if (rc < 0) {
        return 0xFFFFFFFFu;
    }
    if (rc > 0 && ip != 0) {
        NoteScriptArm(static_cast<std::uint16_t>(script_id), ip);
        return ip;
    }
    NoteScriptArm(static_cast<std::uint16_t>(script_id), 0);
    return 0;
}

extern "C" int __cdecl ModTryCallHook(int table, std::uint32_t hook_id) {
    std::uint32_t row = 0;
    const int rc =
        RuntimeOnCallHook(CurrentMapStem(), table, static_cast<std::uint16_t>(hook_id), &row);
    if (rc < 0) {
        return 1;
    }
    if (rc <= 0 || row == 0) {
        return 0;
    }
    using DispatchFn = void(__fastcall*)(int, void*);
    auto* fn = reinterpret_cast<DispatchFn>(ModuleBase() + kHookDispatchRva);
    if (!fn) {
        return 0;
    }
    fn(table, reinterpret_cast<void*>(static_cast<std::uintptr_t>(row)));
    return 1;
}

extern "C" __declspec(naked) void ModScriptLookupDetour() {
    // PUSHAD layout (low→high): EDI ESI EBP ESP EBX EDX ECX EAX
    __asm {
        pushad
        push dword ptr [esp + 0x18]
        call ModTryScriptRedirect
        add esp, 4
        test eax, eax
        jz script_vanilla
        mov dword ptr [esp + 0x1C], eax
        popad
        ret
    script_vanilla:
        popad
        push ebp
        mov ebp, esp
        sub esp, 8
        jmp dword ptr [g_mod_script_lookup_resume]
    }
}

extern "C" __declspec(naked) void ModCallHookDetour() {
    // cdecl ModTryCallHook(table=ecx, hook_id=edx)
    __asm {
        pushad
        push dword ptr [esp + 0x14]
        push dword ptr [esp + 0x1C]
        call ModTryCallHook
        add esp, 8
        test eax, eax
        jz hook_vanilla
        popad
        xor eax, eax
        ret
    hook_vanilla:
        popad
        jmp dword ptr [g_mod_call_hook_tramp]
    }
}
#endif

bool InstallScriptRedirectHooks() {
#if !defined(_M_IX86)
    return false;
#else
    const auto base = ModuleBase();
    if (base == 0) {
        return false;
    }

    auto* script_site = reinterpret_cast<std::uint8_t*>(base + kScriptLookupRva);
    const std::uint8_t script_expect[] = {0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x08};
    if (!IsExecutableAddress(script_site) ||
        !BytesMatch(script_site, script_expect, kScriptLookupPatch)) {
        LogWarn("script redirect +0x6F0B0 site mismatch");
        return false;
    }
    g_mod_script_lookup_resume = reinterpret_cast<void*>(base + kScriptLookupResumeRva);
    if (!WriteJump(script_site, reinterpret_cast<void*>(&ModScriptLookupDetour), g_script_original,
                   kScriptLookupPatch)) {
        LogWarn("script redirect +0x6F0B0 hook failed");
        return false;
    }
    g_script_site = script_site;

    auto* hook_site = reinterpret_cast<std::uint8_t*>(base + kCallHookRva);
    const std::uint8_t hook_expect[] = {0x55, 0x8B, 0xEC, 0x51};
    if (!IsExecutableAddress(hook_site) || !BytesMatch(hook_site, hook_expect, 4)) {
        LogWarn("call_hook redirect +0x53560 site mismatch");
        return true;
    }
    g_hook_tramp_mem = MakeTrampoline(hook_site, kCallHookStolen,
                                      static_cast<std::uint8_t*>(hook_site) + kCallHookStolen);
    if (!g_hook_tramp_mem) {
        LogWarn("call_hook trampoline alloc failed");
        return true;
    }
    g_mod_call_hook_tramp = g_hook_tramp_mem;
    if (!WriteJump(hook_site, reinterpret_cast<void*>(&ModCallHookDetour), g_hook_original, 5)) {
        VirtualFree(g_hook_tramp_mem, 0, MEM_RELEASE);
        g_hook_tramp_mem = nullptr;
        g_mod_call_hook_tramp = nullptr;
        LogWarn("call_hook redirect +0x53560 hook failed");
        return true;
    }
    g_hook_site = hook_site;

    return true;
#endif
}

void RemoveScriptRedirectHooks() {
    if (g_script_site) {
        RestoreBytes(g_script_site, g_script_original, kScriptLookupPatch);
        g_script_site = nullptr;
    }
    if (g_hook_site) {
        RestoreBytes(g_hook_site, g_hook_original, 5);
        g_hook_site = nullptr;
    }
    if (g_hook_tramp_mem) {
        VirtualFree(g_hook_tramp_mem, 0, MEM_RELEASE);
        g_hook_tramp_mem = nullptr;
    }
}

}  // namespace grandia_mod
