#include "movie_skip.h"

#include "hook_util.h"
#include "log.h"

#include <Windows.h>

#include <cstdint>
#include <cstring>

namespace grandia_mod {
namespace {

// Movie_Play: test esi,esi / je +7 / test eax,0x800
// esi==0 skips the vanilla Start path on most cinematics (boot/debug only).
constexpr std::uintptr_t kMovieSkipAllowJeRva = 0x1DACC6u;
constexpr std::uint8_t kSkipAllowJeOriginal[2] = {0x74, 0x07};
constexpr std::uint8_t kSkipAllowJeNops[2] = {0x90, 0x90};
constexpr std::uint8_t kSkipTestEaxBit[] = {0xA9, 0x00, 0x08, 0x00, 0x00};
constexpr int kSkipAllowPat[] = {0x85, 0xF6, 0x74, 0x07, 0xA9, 0x00, 0x08, 0x00, 0x00};

void* g_allow_je_site = nullptr;
std::uint8_t g_allow_je_original[2]{};

std::uint8_t* ResolveAllowJe() {
    const auto base = ModuleBase();
    if (base == 0) {
        return nullptr;
    }
    auto* site = reinterpret_cast<std::uint8_t*>(base + kMovieSkipAllowJeRva);
    if (IsExecutableAddress(site) && BytesMatch(site, kSkipAllowJeOriginal, 2) &&
        site >= reinterpret_cast<std::uint8_t*>(base) + 2 && site[-2] == 0x85 && site[-1] == 0xF6 &&
        BytesMatch(site + 2, kSkipTestEaxBit, sizeof(kSkipTestEaxBit))) {
        return site;
    }
    const auto hit = ScanExecutable(GetModuleHandleW(nullptr), kSkipAllowPat, 9);
    if (hit == 0) {
        return nullptr;
    }
    return reinterpret_cast<std::uint8_t*>(hit + 2);
}

}  // namespace

bool InstallMovieSkipHook() {
    auto* site = ResolveAllowJe();
    if (!site) {
        LogWarn("OnMovieSkip +0x1DACC6 site mismatch (Start skip stays vanilla-gated)");
        return false;
    }
    std::memcpy(g_allow_je_original, site, 2);
    RestoreBytes(site, kSkipAllowJeNops, 2);
    g_allow_je_site = site;
    return true;
}

void RemoveMovieSkipHook() {
    if (g_allow_je_site) {
        RestoreBytes(g_allow_je_site, g_allow_je_original, 2);
        g_allow_je_site = nullptr;
    }
}

}  // namespace grandia_mod
