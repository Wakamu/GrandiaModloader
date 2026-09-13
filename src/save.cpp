#include "save.h"

#include "catalog.h"
#include "clr_host.h"
#include "game.h"
#include "hook_util.h"
#include "log.h"
#include "party.h"
#include "virt_file.h"

#include <Windows.h>

#include <cstdio>
#include <cstring>

using FwriteFn = std::size_t(__cdecl*)(const void*, std::size_t, std::size_t, std::FILE*);
using FreadFn = std::size_t(__cdecl*)(void*, std::size_t, std::size_t, std::FILE*);
using FseekFn = int(__cdecl*)(std::FILE*, long, int);
using FopenFn = std::FILE*(__cdecl*)(const char*, const char*);
using FcloseFn = int(__cdecl*)(std::FILE*);

namespace grandia_mod {

constexpr std::uintptr_t kSaveFsmEntryRva = 0x2300u;
constexpr std::uintptr_t kSaveFsmResumeRva = 0x2309u;
constexpr std::uintptr_t kSaveFwriteCallRva = 0x254Eu;
// After fwrite + fclose + add esp, 14h. The detour owns fclose so the GMOD
// trailer can be appended to the sealed 0xE80 slot file.
constexpr std::uintptr_t kSaveFwriteResumeRva = 0x255Au;
constexpr std::size_t kSaveFwritePatchSize = 12;
constexpr std::uintptr_t kSaveFopenCallRva = 0x2649u;
constexpr std::uintptr_t kSaveFopenResumeRva = 0x264Fu;
constexpr std::uintptr_t kSaveFreadCallRva = 0x2673u;
constexpr std::uintptr_t kSaveFreadResumeRva = 0x2679u;
constexpr std::uintptr_t kSaveConfirmUiRva = 0x657DAu;
constexpr std::uintptr_t kSaveConfirmUiContinueRva = 0x657E1u;
constexpr std::uintptr_t kSaveConfirmUiResumeRva = 0x66731u;

constexpr std::size_t kCallIatPatchSize = 6;
constexpr std::size_t kFsmEntryPatchSize = 9;
constexpr std::size_t kConfirmUiPatchSize = 7;
constexpr std::size_t kConfirmUiTrampolineSize = 16;

constexpr std::uintptr_t kPreferredImageBase = 0x400000u;
constexpr std::uintptr_t VaToRva(std::uintptr_t preferred_va) {
    return preferred_va - kPreferredImageBase;
}

constexpr std::uintptr_t kRvaSaveUiPhase = VaToRva(0x6C301Cu);
constexpr std::uintptr_t kRvaSaveUiInProgress = VaToRva(0x6C2E04u);
constexpr std::uintptr_t kRvaSaveUiCursor = VaToRva(0x71D14Eu);
constexpr std::uintptr_t kRvaSaveSelectedSlot = VaToRva(0x6C301Eu);
constexpr std::uintptr_t kRvaSaveObject = VaToRva(0x718C40u);
constexpr std::uintptr_t kRvaSaveDirPrefix = VaToRva(0x6C1D00u);

constexpr unsigned kFsmOpConfirmLoad = 3;
constexpr unsigned kFsmOpSave = 4;
constexpr std::uint32_t kVanillaSaveSize = 0xE80u;
constexpr std::uint16_t kGmodVersionWrite = 2;
constexpr std::uint16_t kGmodVersionMin = 1;
constexpr std::uint16_t kGmodVersionMax = 2;

#pragma pack(push, 1)
struct GmodEnvelope {
    char magic[4];
    std::uint16_t version;
    std::uint16_t length;
};
#pragma pack(pop)
static_assert(sizeof(GmodEnvelope) == 8, "GMOD header");

template <typename T>
T* GamePtr(std::uintptr_t rva) {
    const std::uintptr_t base = ModuleBase();
    if (base == 0) {
        return nullptr;
    }
    return reinterpret_cast<T*>(base + rva);
}

void* g_fwrite_hook_site = nullptr;
void* g_fread_hook_site = nullptr;
void* g_fopen_hook_site = nullptr;
void* g_confirm_ui_hook_site = nullptr;
void* g_fsm_hook_site = nullptr;
std::uint8_t* g_confirm_ui_trampoline = nullptr;
std::uint8_t g_fwrite_hook_original[16]{};
std::uint8_t g_fread_hook_original[8]{};
std::uint8_t g_fopen_hook_original[8]{};
std::uint8_t g_confirm_ui_hook_original[8]{};
std::uint8_t g_fsm_hook_original[16]{};

FwriteFn g_crt_fwrite = nullptr;
FreadFn g_crt_fread = nullptr;
FseekFn g_crt_fseek = nullptr;
FopenFn g_iat_fopen = nullptr;
FcloseFn g_crt_fclose = nullptr;

std::uint8_t g_committed[kMaxSaveTrailer]{};
int g_committed_len = 0;
std::uint8_t g_pending[kMaxSaveTrailer]{};
int g_pending_len = 0;
std::uint8_t g_scratch[kMaxSaveTrailer]{};
SaveEventNative g_save_req{};
LoadEventNative g_load_req{};
bool g_pending_present = false;
bool g_confirm_load_armed = false;
bool g_load_denied = false;
bool g_peek_ok = false;

int GetSelectedSlot() {
    std::uint8_t slot = 0;
    if (!SafeReadByte(ModuleBase() + kRvaSaveSelectedSlot, &slot)) {
        return 0;
    }
    return slot;
}

void ClearPending() {
    std::memset(g_pending, 0, sizeof(g_pending));
    g_pending_len = 0;
    g_pending_present = false;
}

void ClearCommittedSaveExtra() {
    std::memset(g_committed, 0, sizeof(g_committed));
    g_committed_len = 0;
    ClearPending();
}

void RestoreSaveSelectUi() {
    auto* phase = GamePtr<std::uint8_t>(kRvaSaveUiPhase);
    auto* in_progress = GamePtr<std::uint8_t>(kRvaSaveUiInProgress);
    auto* cursor = GamePtr<std::uint8_t>(kRvaSaveUiCursor);
    auto* save_obj = GamePtr<std::uint8_t>(kRvaSaveObject);
    if (!phase || !in_progress || !cursor || !save_obj) {
        LogWarn("Save UI restore skipped — grandia base unknown");
        return;
    }
    __try {
        *phase = 0;
        *in_progress = 0;
        *cursor = 0;
        *reinterpret_cast<std::uint32_t*>(save_obj + 0xCu) = 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        LogWarn("Save UI restore faulted");
    }
}

bool BuildSelectedSlotPath(char* out, std::size_t out_len) {
    if (!out || out_len < 32) {
        return false;
    }
    unsigned slot = 0;
    unsigned profile = 0;
    const char* save_dir = nullptr;
    __try {
        auto* slot_ptr = GamePtr<std::uint8_t>(kRvaSaveSelectedSlot);
        auto* save_obj = GamePtr<std::uint8_t>(kRvaSaveObject);
        auto* dir_ptr = GamePtr<char>(kRvaSaveDirPrefix);
        if (!slot_ptr || !save_obj || !dir_ptr || !dir_ptr[0]) {
            return false;
        }
        slot = *slot_ptr;
        profile = *reinterpret_cast<std::uint32_t*>(save_obj + 0x8u);
        save_dir = dir_ptr;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
    if (profile > 999999u) {
        return false;
    }
    const int n = std::snprintf(out, out_len, "%s%u\\BISLPSP02124GRA-S%02u", save_dir, profile, slot);
    return n > 0 && static_cast<std::size_t>(n) < out_len;
}

bool IsVanillaSaveIo(std::size_t elem_size, std::size_t count) {
    return elem_size * count == kVanillaSaveSize;
}

bool PeekGmod(std::FILE* file, std::uint8_t* out, int* out_len) {
    if (out_len) {
        *out_len = 0;
    }
    if (!file || !out || !out_len || !g_crt_fread || !g_crt_fseek) {
        return false;
    }
    __try {
        if (g_crt_fseek(file, static_cast<long>(kVanillaSaveSize), SEEK_SET) != 0) {
            return false;
        }
        GmodEnvelope env{};
        if (g_crt_fread(&env, 1, sizeof(env), file) != sizeof(env)) {
            return false;
        }
        if (std::memcmp(env.magic, "GMOD", 4) != 0 || env.version < kGmodVersionMin ||
            env.version > kGmodVersionMax || env.length > kMaxSaveTrailer) {
            return false;
        }
        if (env.length == 0) {
            return true;
        }
        if (g_crt_fread(out, 1, env.length, file) != env.length) {
            return false;
        }
        *out_len = env.length;
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

void CopyToNative(SaveEventNative* req, const std::uint8_t* src, int len) {
    req->slot = GetSelectedSlot();
    req->trailer_len = 0;
    std::memset(req->trailer, 0, sizeof(req->trailer));
    if (src && len > 0) {
        if (len > kMaxSaveTrailer) {
            len = kMaxSaveTrailer;
        }
        std::memcpy(req->trailer, src, static_cast<std::size_t>(len));
        req->trailer_len = len;
    }
}

void CopyToLoadNative(LoadEventNative* req, int phase, const std::uint8_t* src, int len) {
    req->slot = GetSelectedSlot();
    req->phase = phase;
    req->allow = 1;
    req->trailer_len = 0;
    std::memset(req->trailer, 0, sizeof(req->trailer));
    if (src && len > 0) {
        if (len > kMaxSaveTrailer) {
            len = kMaxSaveTrailer;
        }
        std::memcpy(req->trailer, src, static_cast<std::size_t>(len));
        req->trailer_len = len;
    }
}

bool RaiseOnLoad(int phase, const std::uint8_t* src, int len) {
    CopyToLoadNative(&g_load_req, phase, src, len);
    if (RuntimeOnLoad(&g_load_req) != 0) {
        return true;
    }
    return g_load_req.allow != 0;
}

void EvaluateConfirmLoadAtOp3() {
    g_load_denied = false;
    g_confirm_load_armed = false;
    g_peek_ok = false;
    ClearPending();

    char path[MAX_PATH]{};
    int trailer_len = 0;
    bool have_file = false;

    if (BuildSelectedSlotPath(path, sizeof(path)) && g_iat_fopen && g_crt_fclose) {
        std::FILE* file = g_iat_fopen(path, "rb");
        if (file) {
            have_file = true;
            PeekGmod(file, g_scratch, &trailer_len);
            g_crt_fclose(file);
        }
    } else {
        LogWarn("OnLoad peek: could not build slot path — allowing");
    }

    if (!RaiseOnLoad(0, have_file ? g_scratch : nullptr, trailer_len)) {
        g_load_denied = true;
        g_confirm_load_armed = false;
        return;
    }

    if (have_file) {
        std::memcpy(g_pending, g_scratch, static_cast<std::size_t>(trailer_len));
        g_pending_len = trailer_len;
        g_pending_present = true;
    }
    g_confirm_load_armed = true;
    g_peek_ok = true;
    g_load_denied = false;
}

bool VanillaFwriteLooksComplete(std::size_t size, std::size_t count, std::size_t result) {
    if (!IsVanillaSaveIo(size, count)) {
        return false;
    }
    if (result == kVanillaSaveSize || result == count) {
        return true;
    }
    return count == 1 && result == 1 && size == kVanillaSaveSize;
}

void FillGmodEnvelope(GmodEnvelope* env, int len) {
    env->magic[0] = 'G';
    env->magic[1] = 'M';
    env->magic[2] = 'O';
    env->magic[3] = 'D';
    env->version = kGmodVersionWrite;
    env->length = static_cast<std::uint16_t>(len);
}

bool WriteGmodToPath(const char* path, const std::uint8_t* payload, int len) {
    if (!path || !path[0] || len < 0) {
        return false;
    }
    HANDLE file = CreateFileA(path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                              OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        LogWarn("OnSave: CreateFile failed err=%u path=%s", GetLastError(), path);
        return false;
    }
    LARGE_INTEGER pos{};
    pos.QuadPart = static_cast<LONGLONG>(kVanillaSaveSize);
    if (!SetFilePointerEx(file, pos, nullptr, FILE_BEGIN)) {
        CloseHandle(file);
        return false;
    }
    GmodEnvelope env{};
    FillGmodEnvelope(&env, len);
    DWORD wrote = 0;
    bool ok = WriteFile(file, &env, sizeof(env), &wrote, nullptr) && wrote == sizeof(env);
    if (ok && len > 0) {
        ok = WriteFile(file, payload, static_cast<DWORD>(len), &wrote, nullptr) &&
             wrote == static_cast<DWORD>(len);
    }
    if (ok) {
        LARGE_INTEGER end{};
        end.QuadPart = static_cast<LONGLONG>(kVanillaSaveSize + sizeof(env) +
                                             static_cast<unsigned>(len));
        ok = SetFilePointerEx(file, end, nullptr, FILE_BEGIN) && SetEndOfFile(file);
    }
    CloseHandle(file);
    return ok;
}

bool WriteGmodViaFopenAb(const char* path, const std::uint8_t* payload, int len) {
    if (!path || !g_iat_fopen || !g_crt_fwrite || !g_crt_fclose) {
        return false;
    }
    std::FILE* file = g_iat_fopen(path, "ab");
    if (!file) {
        return false;
    }
    GmodEnvelope env{};
    FillGmodEnvelope(&env, len);
    bool ok = g_crt_fwrite(&env, 1, sizeof(env), file) == sizeof(env);
    if (ok && len > 0) {
        ok = g_crt_fwrite(payload, 1, static_cast<std::size_t>(len), file) ==
             static_cast<std::size_t>(len);
    }
    g_crt_fclose(file);
    return ok;
}

void PersistTrailer(std::FILE* file) {
    RestoreFieldPartyForSave();
    CopyToNative(&g_save_req, g_committed, g_committed_len);
    if (RuntimeOnSave(&g_save_req) != 0) {
        LogWarn("OnSave CLR failed — writing last committed extra");
        CopyToNative(&g_save_req, g_committed, g_committed_len);
    }
    int len = g_save_req.trailer_len;
    if (len < 0) {
        len = 0;
    }
    if (len > kMaxSaveTrailer) {
        len = kMaxSaveTrailer;
    }

    if (file && g_crt_fwrite) {
        GmodEnvelope env{};
        FillGmodEnvelope(&env, len);
        const bool wrote_hdr = g_crt_fwrite(&env, 1, sizeof(env), file) == sizeof(env);
        const bool wrote_body =
            len <= 0 || g_crt_fwrite(g_save_req.trailer, 1, static_cast<std::size_t>(len), file) ==
                            static_cast<std::size_t>(len);
        if (!wrote_hdr || !wrote_body) {
            LogWarn("OnSave: FILE* GMOD fwrite failed");
        }
    }
    if (file && g_crt_fclose) {
        g_crt_fclose(file);
    }

    char path[MAX_PATH]{};
    if (!BuildSelectedSlotPath(path, sizeof(path))) {
        LogWarn("OnSave: could not build slot path");
        return;
    }

    bool ok = false;
    __try {
        ok = WriteGmodToPath(path, g_save_req.trailer, len);
        if (!ok) {
            const DWORD err = GetLastError();
            LogWarn("OnSave: Win32 write failed err=%u path=%s — trying fopen ab", err, path);
            ok = WriteGmodViaFopenAb(path, g_save_req.trailer, len);
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        LogWarn("OnSave trailer write faulted");
        ok = false;
    }
    if (!ok) {
        LogWarn("OnSave: GMOD not written path=%s", path);
        return;
    }
    std::memcpy(g_committed, g_save_req.trailer, static_cast<std::size_t>(len));
    g_committed_len = len;

}

void CommitApplied() {
    ResetLiveSavePointers();
    const std::uint8_t* src = g_pending_present ? g_pending : nullptr;
    const int len = g_pending_present ? g_pending_len : 0;
    RaiseOnLoad(1, src, len);
    RaiseCharactersFromLoad();
    if (g_pending_present) {
        std::memcpy(g_committed, g_pending, static_cast<std::size_t>(g_pending_len));
        g_committed_len = g_pending_len;
    } else {
        std::memset(g_committed, 0, sizeof(g_committed));
        g_committed_len = 0;
    }
    ClearPending();
}

}  // namespace grandia_mod

extern "C" {
volatile void* g_mod_save_fwrite_fn = nullptr;
volatile void* g_mod_save_fread_fn = nullptr;
volatile void* g_mod_save_fopen_fn = nullptr;
volatile void* g_mod_save_fwrite_resume = nullptr;
volatile void* g_mod_save_fread_resume = nullptr;
volatile void* g_mod_save_fopen_resume = nullptr;
volatile void* g_mod_save_confirm_ui_trampoline = nullptr;
volatile void* g_mod_save_confirm_ui_resume = nullptr;
volatile void* g_mod_save_fsm_resume = nullptr;

volatile void* g_mod_save_io_ptr = nullptr;
volatile std::size_t g_mod_save_io_size = 0;
volatile std::size_t g_mod_save_io_count = 0;
volatile void* g_mod_save_io_file = nullptr;
volatile std::size_t g_mod_save_io_result = 0;

volatile unsigned g_mod_save_fsm_state = 0;
volatile unsigned g_mod_save_load_denied = 0;
volatile unsigned g_mod_confirm_ui_skip = 0;
}

extern "C" void ModOnSaveFsmEnter() {
    const unsigned state = g_mod_save_fsm_state;
    if (state == grandia_mod::kFsmOpConfirmLoad) {
        grandia_mod::EvaluateConfirmLoadAtOp3();
        g_mod_save_load_denied = grandia_mod::g_load_denied ? 1u : 0u;
    } else if (state == grandia_mod::kFsmOpSave) {
        grandia_mod::g_confirm_load_armed = false;
        grandia_mod::g_load_denied = false;
        grandia_mod::g_peek_ok = false;
        g_mod_save_load_denied = 0;
    }
}

extern "C" void ModOnSaveConfirmUiDecide() {
    g_mod_confirm_ui_skip = 0;
    if (g_mod_save_load_denied == 0) {
        return;
    }
    grandia_mod::g_load_denied = false;
    g_mod_save_load_denied = 0;
    grandia_mod::g_confirm_load_armed = false;
    grandia_mod::g_peek_ok = false;
    auto* save_obj = grandia_mod::GamePtr<std::uint8_t>(grandia_mod::kRvaSaveObject);
    if (save_obj) {
        __try {
            *reinterpret_cast<std::uint32_t*>(save_obj + 0xCu) = 0;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            grandia_mod::LogWarn("OnLoad deny: could not clear load arm");
        }
    }
    grandia_mod::RestoreSaveSelectUi();
    g_mod_confirm_ui_skip = 1;
    grandia_mod::LogInfo("OnLoad denied — skipped Loading UI");
}

extern "C" void ModOnSaveFopenGate() {
    auto* file = reinterpret_cast<std::FILE*>(const_cast<void*>(g_mod_save_io_file));
    if (!file || !grandia_mod::g_confirm_load_armed) {
        return;
    }
    if (grandia_mod::g_load_denied) {
        if (grandia_mod::g_crt_fclose) {
            grandia_mod::g_crt_fclose(file);
        }
        g_mod_save_io_file = nullptr;
        grandia_mod::g_confirm_load_armed = false;
        grandia_mod::RestoreSaveSelectUi();
        return;
    }
    if (grandia_mod::g_peek_ok) {
        if (grandia_mod::g_crt_fseek) {
            grandia_mod::g_crt_fseek(file, 0, SEEK_SET);
        }
        return;
    }
    int trailer_len = 0;
    grandia_mod::PeekGmod(file, grandia_mod::g_scratch, &trailer_len);
    if (!grandia_mod::RaiseOnLoad(0, grandia_mod::g_scratch, trailer_len)) {
        if (grandia_mod::g_crt_fclose) {
            grandia_mod::g_crt_fclose(file);
        }
        g_mod_save_io_file = nullptr;
        grandia_mod::g_confirm_load_armed = false;
        grandia_mod::g_load_denied = false;
        g_mod_save_load_denied = 0;
        grandia_mod::RestoreSaveSelectUi();

        return;
    }
    if (grandia_mod::g_crt_fseek) {
        grandia_mod::g_crt_fseek(file, 0, SEEK_SET);
    }
}

extern "C" void ModOnSaveFwriteComplete() {
    auto* file = reinterpret_cast<std::FILE*>(const_cast<void*>(g_mod_save_io_file));
    g_mod_save_io_file = nullptr;
    if (!grandia_mod::VanillaFwriteLooksComplete(g_mod_save_io_size, g_mod_save_io_count,
                                                 g_mod_save_io_result)) {
        if (file && grandia_mod::g_crt_fclose) {
            grandia_mod::g_crt_fclose(file);
        }
        grandia_mod::LogWarn(
            "OnSave fwrite incomplete size=%u count=%u result=%u — skipping trailer",
            static_cast<unsigned>(g_mod_save_io_size),
            static_cast<unsigned>(g_mod_save_io_count),
            static_cast<unsigned>(g_mod_save_io_result));
        return;
    }
    grandia_mod::PersistTrailer(file);
}

extern "C" void ModOnSaveFreadComplete() {
    auto* file = reinterpret_cast<std::FILE*>(const_cast<void*>(g_mod_save_io_file));
    if (!grandia_mod::IsVanillaSaveIo(g_mod_save_io_size, g_mod_save_io_count)) {
        return;
    }
    if (g_mod_save_io_result != grandia_mod::kVanillaSaveSize) {
        return;
    }
    if (!file) {
        return;
    }
    grandia_mod::ClearPending();
    if (grandia_mod::PeekGmod(file, grandia_mod::g_pending, &grandia_mod::g_pending_len)) {
        grandia_mod::g_pending_present = true;
    }
    if (grandia_mod::g_crt_fseek) {
        grandia_mod::g_crt_fseek(file, static_cast<long>(grandia_mod::kVanillaSaveSize), SEEK_SET);
    }
    if (grandia_mod::g_confirm_load_armed) {
        grandia_mod::CommitApplied();
        grandia_mod::g_confirm_load_armed = false;
    }
}

#if defined(_M_IX86)

extern "C" __declspec(naked) void ModSaveFsmDetour() {
    __asm {
        movzx eax, byte ptr [ecx + 22h]
        mov dword ptr [g_mod_save_fsm_state], eax

        pushad
        mov eax, esp
        and esp, 0FFFFFFF0h
        sub esp, 16
        mov dword ptr [esp], eax
        call ModOnSaveFsmEnter
        mov esp, dword ptr [esp]
        popad

        push ebp
        mov ebp, esp
        sub esp, 274h
        jmp dword ptr [g_mod_save_fsm_resume]
    }
}

extern "C" __declspec(naked) void ModSaveFwriteDetour() {
    __asm {
        mov eax, dword ptr [esp]
        mov dword ptr [g_mod_save_io_ptr], eax
        mov eax, dword ptr [esp + 4]
        mov dword ptr [g_mod_save_io_size], eax
        mov eax, dword ptr [esp + 8]
        mov dword ptr [g_mod_save_io_count], eax
        mov eax, dword ptr [esp + 0Ch]
        mov dword ptr [g_mod_save_io_file], eax

        call dword ptr [g_mod_save_fwrite_fn]
        mov dword ptr [g_mod_save_io_result], eax

        pushad
        mov eax, esp
        and esp, 0FFFFFFF0h
        sub esp, 16
        mov dword ptr [esp], eax
        call ModOnSaveFwriteComplete
        mov esp, dword ptr [esp]
        popad

        add esp, 10h
        mov eax, dword ptr [g_mod_save_io_result]
        jmp dword ptr [g_mod_save_fwrite_resume]
    }
}

extern "C" __declspec(naked) void ModSaveFreadDetour() {
    __asm {
        mov eax, dword ptr [esp]
        mov dword ptr [g_mod_save_io_ptr], eax
        mov eax, dword ptr [esp + 4]
        mov dword ptr [g_mod_save_io_size], eax
        mov eax, dword ptr [esp + 8]
        mov dword ptr [g_mod_save_io_count], eax
        mov eax, dword ptr [esp + 0Ch]
        mov dword ptr [g_mod_save_io_file], eax

        call dword ptr [g_mod_save_fread_fn]
        mov dword ptr [g_mod_save_io_result], eax

        pushad
        mov eax, esp
        and esp, 0FFFFFFF0h
        sub esp, 16
        mov dword ptr [esp], eax
        call ModOnSaveFreadComplete
        mov esp, dword ptr [esp]
        popad

        mov eax, dword ptr [g_mod_save_io_result]
        jmp dword ptr [g_mod_save_fread_resume]
    }
}

extern "C" __declspec(naked) void ModSaveFopenDetour() {
    __asm {
        call dword ptr [g_mod_save_fopen_fn]
        mov dword ptr [g_mod_save_io_file], eax

        pushad
        mov eax, esp
        and esp, 0FFFFFFF0h
        sub esp, 16
        mov dword ptr [esp], eax
        call ModOnSaveFopenGate
        mov esp, dword ptr [esp]
        popad

        mov eax, dword ptr [g_mod_save_io_file]
        jmp dword ptr [g_mod_save_fopen_resume]
    }
}

extern "C" __declspec(naked) void ModSaveConfirmUiDetour() {
    __asm {
        pushad
        mov eax, esp
        and esp, 0FFFFFFF0h
        sub esp, 16
        mov dword ptr [esp], eax
        call ModOnSaveConfirmUiDecide
        mov esp, dword ptr [esp]
        popad

        cmp dword ptr [g_mod_confirm_ui_skip], 0
        jne deny

        jmp dword ptr [g_mod_save_confirm_ui_trampoline]

    deny:
        mov ebx, 0Ah
        jmp dword ptr [g_mod_save_confirm_ui_resume]
    }
}

#endif

namespace grandia_mod {

bool InstallSaveHooks() {
#if !defined(_M_IX86)
    LogWarn("OnSave/OnLoad require 32-bit build");
    return false;
#else
    const std::uintptr_t base = ModuleBase();
    if (base == 0) {
        LogWarn("OnSave/OnLoad: grandia base unknown");
        return false;
    }

    std::memset(g_committed, 0, sizeof(g_committed));
    g_committed_len = 0;
    ClearPending();
    g_confirm_load_armed = false;
    g_load_denied = false;
    g_peek_ok = false;
    g_mod_save_load_denied = 0;
    g_mod_confirm_ui_skip = 0;

    auto* fwrite_site = reinterpret_cast<std::uint8_t*>(base + kSaveFwriteCallRva);
    auto* fread_site = reinterpret_cast<std::uint8_t*>(base + kSaveFreadCallRva);
    auto* fopen_site = reinterpret_cast<std::uint8_t*>(base + kSaveFopenCallRva);
    auto* confirm_ui_site = reinterpret_cast<std::uint8_t*>(base + kSaveConfirmUiRva);
    auto* fsm_site = reinterpret_cast<std::uint8_t*>(base + kSaveFsmEntryRva);

    if (!IsExecutableAddress(fwrite_site) || !IsExecutableAddress(fread_site) ||
        !IsExecutableAddress(fopen_site) || !IsExecutableAddress(confirm_ui_site) ||
        !IsExecutableAddress(fsm_site)) {
        LogWarn("OnSave/OnLoad: save sites not executable");
        return false;
    }
    if (fwrite_site[0] != 0xFF || fwrite_site[1] != 0x15 || fread_site[0] != 0xFF ||
        fread_site[1] != 0x15 || fopen_site[0] != 0xFF || fopen_site[1] != 0x15) {
        LogWarn("OnSave/OnLoad: fwrite/fread/fopen call-site mismatch");
        return false;
    }
    if (fwrite_site[6] != 0x56 || fwrite_site[7] != 0xFF || fwrite_site[8] != 0xD7 ||
        fwrite_site[9] != 0x83 || fwrite_site[10] != 0xC4 || fwrite_site[11] != 0x14) {
        LogWarn("OnSave/OnLoad: fwrite fclose sequence mismatch at +0x2554");
        return false;
    }
    if (confirm_ui_site[0] != 0xC6 || confirm_ui_site[1] != 0x05 || confirm_ui_site[6] != 0x02) {
        LogWarn("OnSave/OnLoad: confirm-UI site mismatch at +0x%X",
                static_cast<unsigned>(kSaveConfirmUiRva));
        return false;
    }
    if (fsm_site[0] != 0x55 || fsm_site[1] != 0x8B || fsm_site[2] != 0xEC) {
        LogWarn("OnSave/OnLoad: FSM entry bytes mismatch at +0x2300");
        return false;
    }

    CrtStdioFns crt{};
    if (VirtFileOrigStdio(&crt)) {

    } else {
        const char* dlls[] = {"ucrtbase.dll", "api-ms-win-crt-stdio-l1-1-0.dll", "msvcrt.dll"};
        for (auto* name : dlls) {
            HMODULE mod = GetModuleHandleA(name);
            if (!mod) {
                continue;
            }
            crt.fopen = reinterpret_cast<FopenFn>(GetProcAddress(mod, "fopen"));
            crt.fclose = reinterpret_cast<FcloseFn>(GetProcAddress(mod, "fclose"));
            crt.fread = reinterpret_cast<FreadFn>(GetProcAddress(mod, "fread"));
            crt.fwrite = reinterpret_cast<FwriteFn>(GetProcAddress(mod, "fwrite"));
            crt.fseek = reinterpret_cast<FseekFn>(GetProcAddress(mod, "fseek"));
            if (crt.fopen && crt.fclose && crt.fread && crt.fwrite && crt.fseek) {

                break;
            }
            crt = {};
        }
    }
    g_crt_fwrite = crt.fwrite;
    g_crt_fread = crt.fread;
    g_iat_fopen = crt.fopen;
    g_crt_fseek = crt.fseek;
    g_crt_fclose = crt.fclose;
    if (!g_crt_fwrite || !g_crt_fread || !g_iat_fopen || !g_crt_fseek || !g_crt_fclose) {
        LogWarn("OnSave/OnLoad: CRT fopen/fread/fwrite/fseek/fclose unresolved");
        return false;
    }

    g_confirm_ui_trampoline = reinterpret_cast<std::uint8_t*>(
        VirtualAlloc(nullptr, kConfirmUiTrampolineSize, MEM_COMMIT | MEM_RESERVE,
                     PAGE_EXECUTE_READWRITE));
    if (!g_confirm_ui_trampoline) {
        LogWarn("OnSave/OnLoad: confirm-UI trampoline alloc failed");
        return false;
    }
    std::memcpy(g_confirm_ui_trampoline, confirm_ui_site, kConfirmUiPatchSize);
    {
        auto* cont = reinterpret_cast<std::uint8_t*>(base + kSaveConfirmUiContinueRva);
        g_confirm_ui_trampoline[kConfirmUiPatchSize] = 0xE9;
        const auto rel = static_cast<std::int32_t>(
            cont - (g_confirm_ui_trampoline + kConfirmUiPatchSize + 5));
        std::memcpy(g_confirm_ui_trampoline + kConfirmUiPatchSize + 1, &rel, sizeof(rel));
    }

    g_mod_save_fwrite_fn = reinterpret_cast<void*>(g_crt_fwrite);
    g_mod_save_fread_fn = reinterpret_cast<void*>(g_crt_fread);
    g_mod_save_fopen_fn = reinterpret_cast<void*>(g_iat_fopen);
    g_mod_save_fwrite_resume = reinterpret_cast<void*>(base + kSaveFwriteResumeRva);
    g_mod_save_fread_resume = reinterpret_cast<void*>(base + kSaveFreadResumeRva);
    g_mod_save_fopen_resume = reinterpret_cast<void*>(base + kSaveFopenResumeRva);
    g_mod_save_confirm_ui_trampoline = g_confirm_ui_trampoline;
    g_mod_save_confirm_ui_resume = reinterpret_cast<void*>(base + kSaveConfirmUiResumeRva);
    g_mod_save_fsm_resume = reinterpret_cast<void*>(base + kSaveFsmResumeRva);

    auto rollback_all = [&]() {
        if (g_fread_hook_site) {
            RestoreBytes(g_fread_hook_site, g_fread_hook_original, kCallIatPatchSize);
            g_fread_hook_site = nullptr;
        }
        if (g_fopen_hook_site) {
            RestoreBytes(g_fopen_hook_site, g_fopen_hook_original, kCallIatPatchSize);
            g_fopen_hook_site = nullptr;
        }
        if (g_confirm_ui_hook_site) {
            RestoreBytes(g_confirm_ui_hook_site, g_confirm_ui_hook_original, kConfirmUiPatchSize);
            g_confirm_ui_hook_site = nullptr;
        }
        if (g_fwrite_hook_site) {
            RestoreBytes(g_fwrite_hook_site, g_fwrite_hook_original, kSaveFwritePatchSize);
            g_fwrite_hook_site = nullptr;
        }
        if (g_fsm_hook_site) {
            RestoreBytes(g_fsm_hook_site, g_fsm_hook_original, kFsmEntryPatchSize);
            g_fsm_hook_site = nullptr;
        }
        if (g_confirm_ui_trampoline) {
            VirtualFree(g_confirm_ui_trampoline, 0, MEM_RELEASE);
            g_confirm_ui_trampoline = nullptr;
            g_mod_save_confirm_ui_trampoline = nullptr;
        }
    };

    if (!WriteJump(fsm_site, reinterpret_cast<void*>(&ModSaveFsmDetour), g_fsm_hook_original,
                   kFsmEntryPatchSize)) {
        LogWarn("OnSave/OnLoad: FSM hook failed");
        rollback_all();
        return false;
    }
    g_fsm_hook_site = fsm_site;

    if (!WriteJump(fwrite_site, reinterpret_cast<void*>(&ModSaveFwriteDetour), g_fwrite_hook_original,
                   kSaveFwritePatchSize)) {
        rollback_all();
        LogWarn("OnSave/OnLoad: fwrite hook failed");
        return false;
    }
    g_fwrite_hook_site = fwrite_site;

    if (!WriteJump(confirm_ui_site, reinterpret_cast<void*>(&ModSaveConfirmUiDetour),
                   g_confirm_ui_hook_original, kConfirmUiPatchSize)) {
        rollback_all();
        LogWarn("OnSave/OnLoad: confirm-UI hook failed");
        return false;
    }
    g_confirm_ui_hook_site = confirm_ui_site;

    if (!WriteJump(fopen_site, reinterpret_cast<void*>(&ModSaveFopenDetour), g_fopen_hook_original,
                   kCallIatPatchSize)) {
        rollback_all();
        LogWarn("OnSave/OnLoad: fopen hook failed");
        return false;
    }
    g_fopen_hook_site = fopen_site;

    if (!WriteJump(fread_site, reinterpret_cast<void*>(&ModSaveFreadDetour), g_fread_hook_original,
                   kCallIatPatchSize)) {
        rollback_all();
        LogWarn("OnSave/OnLoad: fread hook failed");
        return false;
    }
    g_fread_hook_site = fread_site;

    return true;
#endif
}

void RemoveSaveHooks() {
#if defined(_M_IX86)
    if (g_fread_hook_site) {
        RestoreBytes(g_fread_hook_site, g_fread_hook_original, kCallIatPatchSize);
        g_fread_hook_site = nullptr;
    }
    if (g_fopen_hook_site) {
        RestoreBytes(g_fopen_hook_site, g_fopen_hook_original, kCallIatPatchSize);
        g_fopen_hook_site = nullptr;
    }
    if (g_confirm_ui_hook_site) {
        RestoreBytes(g_confirm_ui_hook_site, g_confirm_ui_hook_original, kConfirmUiPatchSize);
        g_confirm_ui_hook_site = nullptr;
    }
    if (g_fwrite_hook_site) {
        RestoreBytes(g_fwrite_hook_site, g_fwrite_hook_original, kSaveFwritePatchSize);
        g_fwrite_hook_site = nullptr;
    }
    if (g_fsm_hook_site) {
        RestoreBytes(g_fsm_hook_site, g_fsm_hook_original, kFsmEntryPatchSize);
        g_fsm_hook_site = nullptr;
    }
    if (g_confirm_ui_trampoline) {
        VirtualFree(g_confirm_ui_trampoline, 0, MEM_RELEASE);
        g_confirm_ui_trampoline = nullptr;
        g_mod_save_confirm_ui_trampoline = nullptr;
    }
#endif
}

}  // namespace grandia_mod
