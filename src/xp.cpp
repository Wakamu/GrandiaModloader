#include "xp.h"

#include "hook_util.h"
#include "log.h"

#include <Windows.h>

#include <atomic>
#include <cstdint>
#include <cstring>

#if defined(_M_IX86)
extern "C" {
void* g_mod_magic_xp_resume = nullptr;
void* g_mod_skill_xp_resume = nullptr;
void* g_mod_level_xp_fight_resume = nullptr;

unsigned ModGetMagicXpMultiplier();
unsigned ModGetSkillXpMultiplier();
unsigned ModGetLevelXpMultiplier();
void ModMagicXpDetour();
void ModSkillXpDetour();
void ModLevelXpFightDetour();
}
#endif

namespace grandia_mod {
namespace {

constexpr int kKindMagic = 0;
constexpr int kKindSkill = 1;
constexpr int kKindLevel = 2;
constexpr unsigned kMultMin = 1;
constexpr unsigned kMultMax = 100;

constexpr std::uintptr_t kMagicXpAddRva = 0xA4E96u;
constexpr std::size_t kMagicXpStolen = 10;
constexpr std::size_t kMagicXpPatch = 5;
constexpr std::uint8_t kMagicXpBytes[] = {0x8B, 0x45, 0xF8, 0x66, 0x01, 0x07, 0x66, 0x01, 0x47, 0x0A};

constexpr std::uintptr_t kSkillXpAddRva = 0xA4FA7u;
constexpr std::size_t kSkillXpStolen = 22;
constexpr std::size_t kSkillXpPatch = 5;
constexpr std::uint8_t kSkillXpBytes[] = {0x05, 0xBF, 0x00, 0x00, 0x00, 0x66, 0x01, 0x34, 0x43,
                                         0x8D, 0x04, 0x43, 0x66, 0x01, 0xB3, 0x8E, 0x01, 0x00, 0x00,
                                         0x89, 0x45, 0xF0};

constexpr std::uintptr_t kLevelXpFightRva = 0x1387C3u;
constexpr std::size_t kLevelXpFightStolen = 6;
constexpr std::size_t kLevelXpFightPatch = 5;
constexpr std::uint8_t kLevelXpFightBytes[] = {0x01, 0x88, 0x88, 0x00, 0x00, 0x00};

std::atomic<unsigned> g_magic_xp_mult{1};
std::atomic<unsigned> g_skill_xp_mult{1};
std::atomic<unsigned> g_level_xp_mult{1};

void* g_magic_site = nullptr;
void* g_skill_site = nullptr;
void* g_level_fight_site = nullptr;

unsigned ClampMultiplier(int multiplier) {
    if (multiplier < static_cast<int>(kMultMin)) {
        return kMultMin;
    }
    if (multiplier > static_cast<int>(kMultMax)) {
        return kMultMax;
    }
    return static_cast<unsigned>(multiplier);
}

void NopTail(void* site, std::size_t patch_size, std::size_t stolen_size) {
    if (!site || stolen_size <= patch_size) {
        return;
    }
    DWORD old = 0;
    if (!VirtualProtect(site, stolen_size, PAGE_EXECUTE_READWRITE, &old)) {
        return;
    }
    auto* bytes = reinterpret_cast<std::uint8_t*>(site);
    for (std::size_t i = patch_size; i < stolen_size; ++i) {
        bytes[i] = 0x90;
    }
    VirtualProtect(site, stolen_size, old, &old);
    FlushInstructionCache(GetCurrentProcess(), site, stolen_size);
}

}  // namespace

int XpGet(int kind) {
    switch (kind) {
        case kKindSkill:
            return static_cast<int>(g_skill_xp_mult.load());
        case kKindLevel:
            return static_cast<int>(g_level_xp_mult.load());
        default:
            return static_cast<int>(g_magic_xp_mult.load());
    }
}

int XpSet(int kind, int multiplier) {
    const unsigned v = ClampMultiplier(multiplier);
    switch (kind) {
        case kKindSkill:
            g_skill_xp_mult.store(v);
            break;
        case kKindLevel:
            g_level_xp_mult.store(v);
            break;
        default:
            g_magic_xp_mult.store(v);
            kind = kKindMagic;
            break;
    }
    return static_cast<int>(v);
}

bool InstallXpHooks() {
#if !defined(_M_IX86)
    return false;
#else
    if (g_magic_site && g_skill_site && g_level_fight_site) {
        return true;
    }
    const auto base = ModuleBase();
    if (base == 0) {
        return false;
    }

    auto* magic_site = reinterpret_cast<void*>(base + kMagicXpAddRva);
    auto* skill_site = reinterpret_cast<void*>(base + kSkillXpAddRva);
    auto* level_fight = reinterpret_cast<void*>(base + kLevelXpFightRva);

    if (!IsExecutableAddress(magic_site) ||
        !BytesMatch(reinterpret_cast<const std::uint8_t*>(magic_site), kMagicXpBytes,
                    sizeof(kMagicXpBytes))) {
        LogWarn("Magic XP bytes mismatch at +0x%X", static_cast<unsigned>(kMagicXpAddRva));
        return false;
    }
    if (!IsExecutableAddress(skill_site) ||
        !BytesMatch(reinterpret_cast<const std::uint8_t*>(skill_site), kSkillXpBytes,
                    sizeof(kSkillXpBytes))) {
        LogWarn("Skill XP bytes mismatch at +0x%X", static_cast<unsigned>(kSkillXpAddRva));
        return false;
    }
    if (!IsExecutableAddress(level_fight) ||
        !BytesMatch(reinterpret_cast<const std::uint8_t*>(level_fight), kLevelXpFightBytes,
                    sizeof(kLevelXpFightBytes))) {
        LogWarn("Level XP fight-add bytes mismatch at +0x%X",
                static_cast<unsigned>(kLevelXpFightRva));
        return false;
    }

    g_mod_magic_xp_resume = reinterpret_cast<void*>(base + kMagicXpAddRva + kMagicXpStolen);
    g_mod_skill_xp_resume = reinterpret_cast<void*>(base + kSkillXpAddRva + kSkillXpStolen);
    g_mod_level_xp_fight_resume =
        reinterpret_cast<void*>(base + kLevelXpFightRva + kLevelXpFightStolen);

    if (!WriteJump(magic_site, reinterpret_cast<void*>(&ModMagicXpDetour), nullptr, kMagicXpPatch) ||
        !WriteJump(skill_site, reinterpret_cast<void*>(&ModSkillXpDetour), nullptr, kSkillXpPatch) ||
        !WriteJump(level_fight, reinterpret_cast<void*>(&ModLevelXpFightDetour), nullptr,
                   kLevelXpFightPatch)) {
        RestoreBytes(magic_site, kMagicXpBytes, kMagicXpStolen);
        RestoreBytes(skill_site, kSkillXpBytes, kSkillXpStolen);
        RestoreBytes(level_fight, kLevelXpFightBytes, kLevelXpFightStolen);
        g_mod_magic_xp_resume = nullptr;
        g_mod_skill_xp_resume = nullptr;
        g_mod_level_xp_fight_resume = nullptr;
        LogWarn("Failed to patch one or more XP multiplier sites");
        return false;
    }
    NopTail(magic_site, kMagicXpPatch, kMagicXpStolen);
    NopTail(skill_site, kSkillXpPatch, kSkillXpStolen);
    NopTail(level_fight, kLevelXpFightPatch, kLevelXpFightStolen);

    g_magic_site = magic_site;
    g_skill_site = skill_site;
    g_level_fight_site = level_fight;

    return true;
#endif
}

void RemoveXpHooks() {
#if defined(_M_IX86)
    if (g_magic_site) {
        RestoreBytes(g_magic_site, kMagicXpBytes, kMagicXpStolen);
        g_magic_site = nullptr;
    }
    if (g_skill_site) {
        RestoreBytes(g_skill_site, kSkillXpBytes, kSkillXpStolen);
        g_skill_site = nullptr;
    }
    if (g_level_fight_site) {
        RestoreBytes(g_level_fight_site, kLevelXpFightBytes, kLevelXpFightStolen);
        g_level_fight_site = nullptr;
    }
    g_mod_magic_xp_resume = nullptr;
    g_mod_skill_xp_resume = nullptr;
    g_mod_level_xp_fight_resume = nullptr;
#endif
}

}  // namespace grandia_mod

extern "C" int ModXpGet(int kind) {
    return grandia_mod::XpGet(kind);
}

extern "C" int ModXpSet(int kind, int multiplier) {
    return grandia_mod::XpSet(kind, multiplier);
}

#if defined(_M_IX86)

extern "C" unsigned ModGetMagicXpMultiplier() {
    return static_cast<unsigned>(grandia_mod::XpGet(0));
}

extern "C" unsigned ModGetSkillXpMultiplier() {
    return static_cast<unsigned>(grandia_mod::XpGet(1));
}

extern "C" unsigned ModGetLevelXpMultiplier() {
    return static_cast<unsigned>(grandia_mod::XpGet(2));
}

extern "C" __declspec(naked) void ModMagicXpDetour() {
    __asm {
        mov eax, dword ptr [ebp - 8]
        push eax
        call ModGetMagicXpMultiplier
        mov ecx, eax
        pop eax
        cmp ecx, 1
        jbe magic_add
        imul eax, ecx
        cmp eax, 0FFFFh
        jbe magic_add
        mov eax, 0FFFFh
    magic_add:
        add word ptr [edi], ax
        add word ptr [edi + 0Ah], ax
        jmp dword ptr [g_mod_magic_xp_resume]
    }
}

extern "C" __declspec(naked) void ModSkillXpDetour() {
    __asm {
        add eax, 0BFh
        push eax
        movzx eax, si
        push eax
        call ModGetSkillXpMultiplier
        mov ecx, eax
        pop eax
        cmp ecx, 1
        jbe skill_ready
        imul eax, ecx
        cmp eax, 0FFFFh
        jbe skill_ready
        mov eax, 0FFFFh
    skill_ready:
        mov esi, eax
        pop eax
        add word ptr [ebx + eax*2], si
        lea eax, [ebx + eax*2]
        add word ptr [ebx + 18Eh], si
        mov dword ptr [ebp - 10h], eax
        jmp dword ptr [g_mod_skill_xp_resume]
    }
}

extern "C" __declspec(naked) void ModLevelXpFightDetour() {
    __asm {
        push eax
        push ecx
        call ModGetLevelXpMultiplier
        mov edx, eax
        pop ecx
        pop eax
        cmp edx, 1
        jbe level_fight_add
        imul ecx, edx
    level_fight_add:
        add dword ptr [eax + 88h], ecx
        jmp dword ptr [g_mod_level_xp_fight_resume]
    }
}

#endif
