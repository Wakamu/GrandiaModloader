#include "clr_host.h"

#include "game.h"
#include "log.h"

#include <Windows.h>

#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

namespace grandia_mod {
namespace {

using char_t = wchar_t;

enum hostfxr_delegate_type {
    hdt_com_activation = 0,
    hdt_load_in_memory_assembly = 1,
    hdt_winrt_activation = 2,
    hdt_com_register = 3,
    hdt_com_unregister = 4,
    hdt_load_assembly_and_get_function_pointer = 5,
    hdt_get_function_pointer = 6,
};

using hostfxr_handle = void*;

struct hostfxr_initialize_parameters {
    size_t size;
    const char_t* host_path;
    const char_t* dotnet_root;
};

using hostfxr_initialize_for_runtime_config_fn = int32_t(__cdecl*)(
    const char_t* runtime_config_path, const hostfxr_initialize_parameters* parameters,
    hostfxr_handle* host_context_handle);
using hostfxr_get_runtime_delegate_fn = int32_t(__cdecl*)(hostfxr_handle host_context_handle,
                                                          hostfxr_delegate_type type, void** delegate);
using hostfxr_close_fn = int32_t(__cdecl*)(hostfxr_handle host_context_handle);

// coreclr_delegates.h: CORECLR_DELEGATE_CALLTYPE is __stdcall on Win32.
using load_assembly_and_get_function_pointer_fn = int(__stdcall*)(
    const char_t* assembly_path, const char_t* type_name, const char_t* method_name,
    const char_t* delegate_type_name, void* reserved, void** delegate);
using hostfxr_error_writer_fn = void(__cdecl*)(const char_t* message);
using hostfxr_set_error_writer_fn = hostfxr_error_writer_fn(__cdecl*)(hostfxr_error_writer_fn writer);

#pragma pack(push, 1)
struct MapOpenNative {
    char stem[16];
    std::uint16_t from;
    std::uint16_t to;
    std::int32_t spawn;
    char cache_dir[260];
    std::int32_t dirty;
};
#pragma pack(pop)

using InitFn = int(__cdecl*)(const char* mods_json_utf8);
using OnMapOpenFn = int(__cdecl*)(MapOpenNative* req);
using OnMapPatchInfoFn = int(__cdecl*)(MapPatchInfoNative* req);
using OnScriptLookupFn = int(__cdecl*)(ScriptLookupNative* req);
using OnCallHookFn = int(__cdecl*)(CallHookNative* req);
using OnEventFlagFn = int(__cdecl*)(EventFlagNative* req);
using OnItemAssignUiFn = int(__cdecl*)(ItemAssignNative* req);
using OnFieldGoldAddFn = int(__cdecl*)(FieldGoldNative* req);
using OnWorldMapConfirmFn = int(__cdecl*)(WorldMapConfirmNative* req);
using OnWorldMapLoadFn = int(__cdecl*)(WorldMapLoadNative* req);
using OnMapTravelFn = int(__cdecl*)(MapTravelNative* req);
using OnSaveFn = int(__cdecl*)(SaveEventNative* req);
using OnLoadFn = int(__cdecl*)(LoadEventNative* req);
using OnBattleLoadFn = int(__cdecl*)(BattleLoadNative* req);
using OnBattleSetupFn = int(__cdecl*)(BattleLoadNative* req);
using OnMenuOpenFn = int(__cdecl*)(MenuOpenNative* req);
using OnEnemyLoadedFn = int(__cdecl*)(EnemyLoadedNative* req);
using OnShopOpenFn = int(__cdecl*)(ShopOpenNative* req);
using OnTickFn = int(__cdecl*)(TickNative* req);
using OnTitleScreenFn = int(__cdecl*)();
using OnCharacterFn = int(__cdecl*)(CharacterNative* req);
using OnItemFn = int(__cdecl*)(ItemNative* req);
using OnMagicFn = int(__cdecl*)(MagicNative* req);
using OnDialogueFn = int(__cdecl*)(DialogueNative* req);
using BindHostFn = int(__cdecl*)(HostApiNative* api);
using GetMapFileFn = int(__cdecl*)(MapFileNative* req);

HMODULE g_hostfxr = nullptr;
hostfxr_handle g_ctx = nullptr;
hostfxr_close_fn g_close = nullptr;
InitFn g_init = nullptr;
OnMapOpenFn g_on_map_open = nullptr;
OnMapPatchInfoFn g_on_map_patch_info = nullptr;
OnScriptLookupFn g_on_script_lookup = nullptr;
OnCallHookFn g_on_call_hook = nullptr;
OnEventFlagFn g_on_event_flag = nullptr;
OnItemAssignUiFn g_on_item_assign = nullptr;
OnFieldGoldAddFn g_on_field_gold = nullptr;
OnWorldMapConfirmFn g_on_world_map = nullptr;
OnWorldMapLoadFn g_on_world_map_load = nullptr;
OnMapTravelFn g_on_map_travel = nullptr;
OnSaveFn g_on_save = nullptr;
OnLoadFn g_on_load = nullptr;
OnBattleLoadFn g_on_battle_load = nullptr;
OnBattleSetupFn g_on_battle_setup = nullptr;
OnMenuOpenFn g_on_menu_open = nullptr;
OnEnemyLoadedFn g_on_enemy_loaded = nullptr;
OnShopOpenFn g_on_shop_open = nullptr;
OnTickFn g_on_tick = nullptr;
OnTitleScreenFn g_on_title_screen = nullptr;
OnCharacterFn g_on_character = nullptr;
OnItemFn g_on_item = nullptr;
OnMagicFn g_on_magic = nullptr;
OnDialogueFn g_on_dialogue = nullptr;
BindHostFn g_bind_host = nullptr;
GetMapFileFn g_get_map_file = nullptr;
bool g_ready = false;

std::wstring Widen(const std::string& s) {
    if (s.empty()) {
        return {};
    }
    const int n = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, nullptr, 0);
    std::wstring out(static_cast<size_t>(n > 0 ? n - 1 : 0), L'\0');
    if (n > 1) {
        MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, out.data(), n);
    }
    return out;
}

std::string Narrow(const std::wstring& s) {
    if (s.empty()) {
        return {};
    }
    const int n = WideCharToMultiByte(CP_UTF8, 0, s.c_str(), -1, nullptr, 0, nullptr, nullptr);
    std::string out(static_cast<size_t>(n > 0 ? n - 1 : 0), '\0');
    if (n > 1) {
        WideCharToMultiByte(CP_UTF8, 0, s.c_str(), -1, out.data(), n, nullptr, nullptr);
    }
    return out;
}

std::string ModuleDirectory() {
    char buf[MAX_PATH]{};
    HMODULE self = nullptr;
    if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                                GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                            reinterpret_cast<LPCSTR>(&ModuleDirectory), &self)) {
        return {};
    }
    if (!GetModuleFileNameA(self, buf, MAX_PATH)) {
        return {};
    }
    std::string path = buf;
    const auto slash = path.find_last_of("\\/");
    return slash == std::string::npos ? "." : path.substr(0, slash);
}

bool FileExistsW(const std::wstring& path) {
    const DWORD attrs = GetFileAttributesW(path.c_str());
    return attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

bool DirExistsW(const std::wstring& path) {
    const DWORD attrs = GetFileAttributesW(path.c_str());
    return attrs != INVALID_FILE_ATTRIBUTES && (attrs & FILE_ATTRIBUTE_DIRECTORY) != 0;
}

int CmpDottedVersion(const wchar_t* a, const wchar_t* b) {
    while (*a || *b) {
        unsigned va = 0;
        unsigned vb = 0;
        while (*a >= L'0' && *a <= L'9') {
            va = va * 10 + static_cast<unsigned>(*a++ - L'0');
        }
        while (*b >= L'0' && *b <= L'9') {
            vb = vb * 10 + static_cast<unsigned>(*b++ - L'0');
        }
        if (va != vb) {
            return va < vb ? -1 : 1;
        }
        if (*a == L'.') {
            ++a;
        }
        if (*b == L'.') {
            ++b;
        }
        if ((*a < L'0' || *a > L'9') && (*b < L'0' || *b > L'9')) {
            break;
        }
    }
    return 0;
}

std::wstring LatestFxr(const std::wstring& fxr_root) {
    if (!DirExistsW(fxr_root)) {
        return {};
    }
    const std::wstring spec = fxr_root + L"\\*";
    WIN32_FIND_DATAW fd{};
    HANDLE find = FindFirstFileW(spec.c_str(), &fd);
    if (find == INVALID_HANDLE_VALUE) {
        return {};
    }
    std::wstring best;
    std::wstring best_ver;
    do {
        if ((fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0) {
            continue;
        }
        if (fd.cFileName[0] == L'.') {
            continue;
        }
        // 8.x or 9.x hostfxr; 9.x can still host net8.
        if (fd.cFileName[0] != L'8' && fd.cFileName[0] != L'9') {
            continue;
        }
        std::wstring cand = fxr_root + L"\\" + fd.cFileName + L"\\hostfxr.dll";
        if (!FileExistsW(cand)) {
            continue;
        }
        if (best.empty() || CmpDottedVersion(fd.cFileName, best_ver.c_str()) > 0) {
            best = cand;
            best_ver = fd.cFileName;
        }
    } while (FindNextFileW(find, &fd));
    FindClose(find);
    return best;
}

std::vector<std::wstring> HostFxrCandidates() {
    std::vector<std::wstring> out;
    auto push = [&](const std::wstring& path) {
        if (path.empty()) {
            return;
        }
        for (const auto& existing : out) {
            if (existing == path) {
                return;
            }
        }
        if (FileExistsW(path)) {
            out.push_back(path);
        }
    };

    push(Widen(ModuleDirectory() + "\\hostfxr.dll"));

    wchar_t env[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"DOTNET_ROOT_X86", env, MAX_PATH) > 0) {
        push(LatestFxr(std::wstring(env) + L"\\host\\fxr"));
    }

    // 32-bit process: prefer the x86 install. Skip Program Files\dotnet (x64).
    push(LatestFxr(L"C:\\Program Files (x86)\\dotnet\\host\\fxr"));

    return out;
}

std::wstring FindX86DotnetRoot() {
    wchar_t env[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"DOTNET_ROOT_X86", env, MAX_PATH) > 0) {
        const std::wstring root(env);
        if (DirExistsW(root + L"\\shared\\Microsoft.NETCore.App")) {
            return root;
        }
    }
    const std::wstring fallback = L"C:\\Program Files (x86)\\dotnet";
    if (DirExistsW(fallback + L"\\shared\\Microsoft.NETCore.App")) {
        return fallback;
    }
    return {};
}

void PinX86Dotnet(const std::wstring& root) {
    if (root.empty()) {
        return;
    }
    // x64 DOTNET_ROOT + multilevel lookup loads a 64-bit coreclr into grandia.exe and AVs.
    SetEnvironmentVariableW(L"DOTNET_ROOT", root.c_str());
    SetEnvironmentVariableW(L"DOTNET_ROOT_X86", root.c_str());
    SetEnvironmentVariableW(L"DOTNET_MULTILEVEL_LOOKUP", L"0");
}

void __cdecl HostfxrLog(const char_t* message) {
    if (!message) {
        return;
    }
    LogWarn("hostfxr: %s", Narrow(message).c_str());
}

bool HostfxrOk(int rc) {
    return rc == 0 || rc == 1 || rc == 2;
}

HMODULE TryLoadHostFxr(std::wstring* loaded_path) {
    for (const auto& path : HostFxrCandidates()) {
        HMODULE mod = LoadLibraryW(path.c_str());
        if (mod) {
            if (loaded_path) {
                *loaded_path = path;
            }
            return mod;
        }
        LogWarn("hostfxr LoadLibrary failed (%lu) %s", GetLastError(), Narrow(path).c_str());
    }
    return nullptr;
}

void CopyUtf8(char* dest, size_t dest_len, const char* src) {
    if (!dest || dest_len == 0) {
        return;
    }
    dest[0] = 0;
    if (!src) {
        return;
    }
    size_t i = 0;
    for (; src[i] && i + 1 < dest_len; ++i) {
        dest[i] = src[i];
    }
    dest[i] = 0;
}

__declspec(noinline) bool LoadRuntime() {
    if (g_ready) {
        return true;
    }

    LogInfo("CLR: locating x86 .NET");
    const std::wstring dotnet_root = FindX86DotnetRoot();
    if (dotnet_root.empty()) {
        LogWarn("CLR: no x86 dotnet root (install .NET 8 x86 runtime)");
        return false;
    }
    PinX86Dotnet(dotnet_root);
    LogInfo("CLR: DOTNET_ROOT=%s", Narrow(dotnet_root).c_str());

    const std::string dll_dir = ModuleDirectory();
    std::wstring hostfxr_path;
    LogInfo("CLR: LoadLibrary hostfxr");
    g_hostfxr = TryLoadHostFxr(&hostfxr_path);
    if (!g_hostfxr) {
        LogWarn("hostfxr.dll not found (need x86 .NET 8/9 hostfxr)");
        return false;
    }
    LogInfo("CLR: hostfxr=%s", Narrow(hostfxr_path).c_str());

    auto init_cfg = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
        GetProcAddress(g_hostfxr, "hostfxr_initialize_for_runtime_config"));
    auto get_del = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(
        GetProcAddress(g_hostfxr, "hostfxr_get_runtime_delegate"));
    g_close = reinterpret_cast<hostfxr_close_fn>(GetProcAddress(g_hostfxr, "hostfxr_close"));
    auto set_writer = reinterpret_cast<hostfxr_set_error_writer_fn>(
        GetProcAddress(g_hostfxr, "hostfxr_set_error_writer"));
    if (!init_cfg || !get_del || !g_close) {
        LogWarn("hostfxr exports missing");
        return false;
    }
    if (set_writer) {
        set_writer(&HostfxrLog);
    }

    const std::wstring runtime_config = Widen(dll_dir + "\\Grandia.Runtime.runtimeconfig.json");
    const std::wstring assembly = Widen(dll_dir + "\\Grandia.Runtime.dll");
    const std::wstring host_path = Widen(dll_dir + "\\GrandiaMod.dll");
    if (!FileExistsW(runtime_config) || !FileExistsW(assembly)) {
        LogWarn("Grandia.Runtime.dll / .runtimeconfig.json missing next to GrandiaMod.dll");
        return false;
    }
    LogInfo("CLR: runtimeconfig=%s", Narrow(runtime_config).c_str());

    hostfxr_initialize_parameters params{};
    params.size = sizeof(params);
    params.host_path = host_path.c_str();
    params.dotnet_root = dotnet_root.c_str();

    LogInfo("CLR: hostfxr_initialize_for_runtime_config");
    const int rc = init_cfg(runtime_config.c_str(), &params, &g_ctx);
    if (!HostfxrOk(rc) || !g_ctx) {
        LogWarn("hostfxr_initialize_for_runtime_config failed (%d)", rc);
        return false;
    }
    LogInfo("CLR: host context ready (rc=%d)", rc);

    void* load_fn = nullptr;
    const int drc =
        get_del(g_ctx, hdt_load_assembly_and_get_function_pointer, &load_fn);
    if (!HostfxrOk(drc) || !load_fn) {
        LogWarn("hostfxr_get_runtime_delegate failed (%d)", drc);
        return false;
    }

    auto load = reinterpret_cast<load_assembly_and_get_function_pointer_fn>(load_fn);
    const char_t* unmanaged_only = reinterpret_cast<const char_t*>(-1);

    LogInfo("CLR: load Grandia.Runtime entry points");
    void* init_ptr = nullptr;
    void* open_ptr = nullptr;
    void* patch_ptr = nullptr;
    void* flag_ptr = nullptr;
    void* assign_ptr = nullptr;
    void* gold_ptr = nullptr;
    void* wm_ptr = nullptr;
    void* save_ptr = nullptr;
    void* load_ptr = nullptr;
    void* battle_ptr = nullptr;
    void* setup_ptr = nullptr;
    void* menu_ptr = nullptr;
    void* enemy_ptr = nullptr;
    void* shop_ptr = nullptr;
    void* wm_load_ptr = nullptr;
    void* script_ptr = nullptr;
    void* hook_ptr = nullptr;
    void* travel_ptr = nullptr;
    void* tick_ptr = nullptr;
    void* title_ptr = nullptr;
    void* character_ptr = nullptr;
    void* item_ptr = nullptr;
    void* magic_ptr = nullptr;
    void* dialogue_ptr = nullptr;
    void* bind_ptr = nullptr;
    void* map_file_ptr = nullptr;
    const int irc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                         L"Init", unmanaged_only, nullptr, &init_ptr);
    const int orc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                         L"OnMapOpen", unmanaged_only, nullptr, &open_ptr);
    const int prc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                         L"OnMapPatchInfo", unmanaged_only, nullptr, &patch_ptr);
    const int frc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                         L"OnEventFlag", unmanaged_only, nullptr, &flag_ptr);
    const int arc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                         L"OnItemAssignUi", unmanaged_only, nullptr, &assign_ptr);
    const int grc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                         L"OnFieldGoldAdd", unmanaged_only, nullptr, &gold_ptr);
    const int wrc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                         L"OnWorldMapConfirm", unmanaged_only, nullptr, &wm_ptr);
    const int src = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                         L"OnSave", unmanaged_only, nullptr, &save_ptr);
    const int lrc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                         L"OnLoad", unmanaged_only, nullptr, &load_ptr);
    const int brc_battle = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                                L"OnBattleLoad", unmanaged_only, nullptr, &battle_ptr);
    const int brc_setup = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                               L"OnBattleSetup", unmanaged_only, nullptr, &setup_ptr);
    const int mrc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                         L"OnMenuOpen", unmanaged_only, nullptr, &menu_ptr);
    const int erc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                         L"OnEnemyLoaded", unmanaged_only, nullptr, &enemy_ptr);
    const int shrc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                          L"OnShopOpen", unmanaged_only, nullptr, &shop_ptr);
    const int wlrc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                         L"OnWorldMapLoad", unmanaged_only, nullptr, &wm_load_ptr);
    const int scrc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                          L"OnScriptLookup", unmanaged_only, nullptr, &script_ptr);
    const int hkrc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                          L"OnCallHook", unmanaged_only, nullptr, &hook_ptr);
    const int tvrc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                          L"OnMapTravel", unmanaged_only, nullptr, &travel_ptr);
    const int tkrc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                         L"OnTick", unmanaged_only, nullptr, &tick_ptr);
    const int tsrc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                          L"OnTitleScreen", unmanaged_only, nullptr, &title_ptr);
    const int chrc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                          L"OnCharacter", unmanaged_only, nullptr, &character_ptr);
    const int itrc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                          L"OnItem", unmanaged_only, nullptr, &item_ptr);
    const int mgrc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                          L"OnMagic", unmanaged_only, nullptr, &magic_ptr);
    const int dlrc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                          L"OnDialogue", unmanaged_only, nullptr, &dialogue_ptr);
    const int brc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                         L"BindHost", unmanaged_only, nullptr, &bind_ptr);
    const int mfrc = load(assembly.c_str(), L"Grandia.Runtime.NativeEntry, Grandia.Runtime",
                          L"GetMapFile", unmanaged_only, nullptr, &map_file_ptr);
    if (irc != 0 || orc != 0 || prc != 0 || frc != 0 || arc != 0 || grc != 0 || wrc != 0 || src != 0 ||
        lrc != 0 || brc_battle != 0 || brc_setup != 0 || mrc != 0 || erc != 0 || shrc != 0 ||
        wlrc != 0 || scrc != 0 || hkrc != 0 || tvrc != 0 || tkrc != 0 || tsrc != 0 || chrc != 0 ||
        itrc != 0 || mgrc != 0 || dlrc != 0 || brc != 0 ||
        mfrc != 0 ||
        !init_ptr ||
        !open_ptr || !patch_ptr || !flag_ptr || !assign_ptr || !gold_ptr || !wm_ptr || !save_ptr ||
        !load_ptr || !battle_ptr || !setup_ptr || !menu_ptr || !enemy_ptr || !shop_ptr ||
        !wm_load_ptr || !script_ptr || !hook_ptr || !travel_ptr || !tick_ptr || !title_ptr ||
        !character_ptr || !item_ptr || !magic_ptr || !dialogue_ptr ||
        !bind_ptr ||
        !map_file_ptr) {
        LogWarn(
            "load Grandia.Runtime entry points failed (init=%d open=%d patch=%d flag=%d assign=%d gold=%d wm=%d save=%d load=%d battle=%d setup=%d menu=%d enemy=%d shop=%d wmload=%d bind=%d)",
            irc, orc, prc, frc, arc, grc, wrc, src, lrc, brc_battle, brc_setup, mrc, erc, shrc,
            wlrc, brc);
        return false;
    }

    g_init = reinterpret_cast<InitFn>(init_ptr);
    g_on_map_open = reinterpret_cast<OnMapOpenFn>(open_ptr);
    g_on_map_patch_info = reinterpret_cast<OnMapPatchInfoFn>(patch_ptr);
    g_on_event_flag = reinterpret_cast<OnEventFlagFn>(flag_ptr);
    g_on_item_assign = reinterpret_cast<OnItemAssignUiFn>(assign_ptr);
    g_on_field_gold = reinterpret_cast<OnFieldGoldAddFn>(gold_ptr);
    g_on_world_map = reinterpret_cast<OnWorldMapConfirmFn>(wm_ptr);
    g_on_save = reinterpret_cast<OnSaveFn>(save_ptr);
    g_on_load = reinterpret_cast<OnLoadFn>(load_ptr);
    g_on_battle_load = reinterpret_cast<OnBattleLoadFn>(battle_ptr);
    g_on_battle_setup = reinterpret_cast<OnBattleSetupFn>(setup_ptr);
    g_on_menu_open = reinterpret_cast<OnMenuOpenFn>(menu_ptr);
    g_on_enemy_loaded = reinterpret_cast<OnEnemyLoadedFn>(enemy_ptr);
    g_on_shop_open = reinterpret_cast<OnShopOpenFn>(shop_ptr);
    g_on_world_map_load = reinterpret_cast<OnWorldMapLoadFn>(wm_load_ptr);
    g_on_script_lookup = reinterpret_cast<OnScriptLookupFn>(script_ptr);
    g_on_call_hook = reinterpret_cast<OnCallHookFn>(hook_ptr);
    g_on_map_travel = reinterpret_cast<OnMapTravelFn>(travel_ptr);
    g_on_tick = reinterpret_cast<OnTickFn>(tick_ptr);
    g_on_title_screen = reinterpret_cast<OnTitleScreenFn>(title_ptr);
    g_on_character = reinterpret_cast<OnCharacterFn>(character_ptr);
    g_on_item = reinterpret_cast<OnItemFn>(item_ptr);
    g_on_magic = reinterpret_cast<OnMagicFn>(magic_ptr);
    g_on_dialogue = reinterpret_cast<OnDialogueFn>(dialogue_ptr);
    g_bind_host = reinterpret_cast<BindHostFn>(bind_ptr);
    g_get_map_file = reinterpret_cast<GetMapFileFn>(map_file_ptr);

    const std::string mods_json = dll_dir + "\\mods.json";
    LogInfo("CLR: Init(%s)", mods_json.c_str());
    if (g_init(mods_json.c_str()) != 0) {
        LogWarn("Grandia.Runtime Init failed (mods.json next to the DLL)");
        g_init = nullptr;
        g_on_map_open = nullptr;
        g_on_map_patch_info = nullptr;
        g_on_script_lookup = nullptr;
        g_on_call_hook = nullptr;
        g_on_event_flag = nullptr;
        g_on_item_assign = nullptr;
        g_on_field_gold = nullptr;
        g_on_world_map = nullptr;
        g_on_save = nullptr;
        g_on_load = nullptr;
        g_on_battle_load = nullptr;
        g_on_battle_setup = nullptr;
        g_on_menu_open = nullptr;
        g_on_enemy_loaded = nullptr;
        g_on_shop_open = nullptr;
        g_on_world_map_load = nullptr;
        g_on_map_travel = nullptr;
        g_on_tick = nullptr;
        g_on_title_screen = nullptr;
        g_on_character = nullptr;
        g_on_item = nullptr;
        g_on_magic = nullptr;
        g_on_dialogue = nullptr;
        g_bind_host = nullptr;
        g_get_map_file = nullptr;
        return false;
    }

    HostApiNative api{};
    FillHostApi(&api);
    if (g_bind_host(&api) != 0) {
        LogWarn("Grandia.Runtime BindHost failed — Game.Stash/Gold/Flags/Party unavailable");
        g_bind_host = nullptr;
    }

    g_ready = true;
    LogInfo("CLR host ready (%s)", Narrow(hostfxr_path).c_str());
    return true;
}

}  // namespace

bool InstallClrHost() {
    LogInfo("CLR: InstallClrHost");
    __try {
        return LoadRuntime();
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        LogWarn("CLR host crashed during init (exception=0x%08X)", GetExceptionCode());
        return false;
    }
}

void RemoveClrHost() {
    // Do not hostfxr_close from DllMain (loader lock). Process teardown owns the runtime.
    g_ready = false;
    g_init = nullptr;
    g_on_map_open = nullptr;
    g_on_map_patch_info = nullptr;
    g_on_script_lookup = nullptr;
    g_on_call_hook = nullptr;
    g_on_event_flag = nullptr;
    g_on_item_assign = nullptr;
    g_on_field_gold = nullptr;
    g_on_world_map = nullptr;
    g_on_save = nullptr;
    g_on_load = nullptr;
    g_on_battle_load = nullptr;
    g_on_battle_setup = nullptr;
    g_on_menu_open = nullptr;
    g_on_enemy_loaded = nullptr;
    g_on_shop_open = nullptr;
    g_on_world_map_load = nullptr;
    g_on_map_travel = nullptr;
    g_on_tick = nullptr;
    g_on_title_screen = nullptr;
    g_on_character = nullptr;
    g_on_item = nullptr;
    g_on_magic = nullptr;
    g_on_dialogue = nullptr;
    g_bind_host = nullptr;
    g_get_map_file = nullptr;
    g_ctx = nullptr;
    g_close = nullptr;
}

bool ClrHostReady() {
    return g_ready;
}

int RuntimeOnMapOpen(const char* stem, std::uint16_t from, std::uint16_t to, int spawn,
                     const char* cache_dir) {
    if (!g_ready || !g_on_map_open || !stem) {
        return 0;
    }

    MapOpenNative req{};
    CopyUtf8(req.stem, sizeof(req.stem), stem);
    req.from = from;
    req.to = to;
    req.spawn = spawn;
    CopyUtf8(req.cache_dir, sizeof(req.cache_dir), cache_dir ? cache_dir : "");
    req.dirty = 0;

    const int rc = g_on_map_open(&req);
    if (rc != 0) {
        return -1;
    }
    return req.dirty ? 1 : 0;
}

int RuntimeMapPatchInfo(const char* stem, MapPatchInfoNative* info) {
    if (!info) {
        return -1;
    }
    std::memset(info, 0, sizeof(*info));
    if (!g_ready || !g_on_map_patch_info || !stem || !*stem) {
        return 0;
    }
    CopyUtf8(info->stem, sizeof(info->stem), stem);
    return g_on_map_patch_info(info);
}

int RuntimeGetMapFile(const char* stem, int kind, const void** ptr, int* len) {
    if (ptr) {
        *ptr = nullptr;
    }
    if (len) {
        *len = 0;
    }
    if (!g_ready || !g_get_map_file || !stem || !*stem) {
        return 0;
    }
    MapFileNative req{};
    CopyUtf8(req.stem, sizeof(req.stem), stem);
    req.kind = kind;
    if (g_get_map_file(&req) != 1 || req.ptr == 0 || req.len <= 0) {
        return 0;
    }
    if (ptr) {
        *ptr = reinterpret_cast<const void*>(static_cast<std::uintptr_t>(req.ptr));
    }
    if (len) {
        *len = req.len;
    }
    return 1;
}

int RuntimeOnScriptLookup(const char* stem, std::uint16_t script_id, std::uint32_t* ip_out) {
    if (ip_out) {
        *ip_out = 0;
    }
    if (!g_ready || !g_on_script_lookup) {
        return 0;
    }
    ScriptLookupNative req{};
    CopyUtf8(req.stem, sizeof(req.stem), stem ? stem : "");
    req.script_id = script_id;
    if (g_on_script_lookup(&req) != 0) {
        return 0;
    }
    if (ip_out) {
        *ip_out = req.ip;
    }
    return req.redirect;
}

int RuntimeOnCallHook(const char* stem, int table, std::uint16_t hook_id, std::uint32_t* row_out) {
    if (row_out) {
        *row_out = 0;
    }
    if (!g_ready || !g_on_call_hook) {
        return 0;
    }
    CallHookNative req{};
    CopyUtf8(req.stem, sizeof(req.stem), stem ? stem : "");
    req.table = table;
    req.hook_id = hook_id;
    if (g_on_call_hook(&req) != 0) {
        return 0;
    }
    if (row_out) {
        *row_out = req.row;
    }
    return req.redirect;
}

int RuntimeOnEventFlag(EventFlagNative* req) {
    if (!g_ready || !g_on_event_flag || !req) {
        return -1;
    }
    return g_on_event_flag(req);
}

int RuntimeOnItemAssignUi(ItemAssignNative* req) {
    if (!g_ready || !g_on_item_assign || !req) {
        return -1;
    }
    return g_on_item_assign(req);
}

int RuntimeOnFieldGoldAdd(FieldGoldNative* req) {
    if (!g_ready || !g_on_field_gold || !req) {
        return -1;
    }
    return g_on_field_gold(req);
}

int RuntimeOnWorldMapConfirm(WorldMapConfirmNative* req) {
    if (!g_ready || !g_on_world_map || !req) {
        return -1;
    }
    return g_on_world_map(req);
}

int RuntimeOnMapTravel(MapTravelNative* req) {
    if (!g_ready || !g_on_map_travel || !req) {
        return -1;
    }
    return g_on_map_travel(req);
}

int RuntimeOnWorldMapLoad(WorldMapLoadNative* req) {
    if (!g_ready || !g_on_world_map_load || !req) {
        return -1;
    }
    return g_on_world_map_load(req);
}

int RuntimeOnSave(SaveEventNative* req) {
    if (!g_ready || !g_on_save || !req) {
        return -1;
    }
    return g_on_save(req);
}

int RuntimeOnLoad(LoadEventNative* req) {
    if (!g_ready || !g_on_load || !req) {
        return -1;
    }
    return g_on_load(req);
}

int RuntimeOnBattleLoad(BattleLoadNative* req) {
    if (!g_ready || !g_on_battle_load || !req) {
        return -1;
    }
    return g_on_battle_load(req);
}

int RuntimeOnBattleSetup(BattleLoadNative* req) {
    if (!g_ready || !g_on_battle_setup || !req) {
        return -1;
    }
    return g_on_battle_setup(req);
}

int RuntimeOnMenuOpen(MenuOpenNative* req) {
    if (!g_ready || !g_on_menu_open || !req) {
        return -1;
    }
    return g_on_menu_open(req);
}

int RuntimeOnEnemyLoaded(EnemyLoadedNative* req) {
    if (!g_ready || !g_on_enemy_loaded || !req) {
        return -1;
    }
    return g_on_enemy_loaded(req);
}

int RuntimeOnShopOpen(ShopOpenNative* req) {
    if (!g_ready || !g_on_shop_open || !req) {
        return -1;
    }
    return g_on_shop_open(req);
}

int RuntimeOnTick(TickNative* req) {
    if (!g_ready || !g_on_tick || !req) {
        return -1;
    }
    return g_on_tick(req);
}

int RuntimeOnTitleScreen() {
    if (!g_ready || !g_on_title_screen) {
        return -1;
    }
    return g_on_title_screen();
}

int RuntimeOnCharacter(CharacterNative* req) {
    if (!g_ready || !g_on_character || !req) {
        return -1;
    }
    return g_on_character(req);
}

int RuntimeOnItem(ItemNative* req) {
    if (!g_ready || !g_on_item || !req) {
        return -1;
    }
    return g_on_item(req);
}

int RuntimeOnMagic(MagicNative* req) {
    if (!g_ready || !g_on_magic || !req) {
        return -1;
    }
    return g_on_magic(req);
}

int RuntimeOnDialogue(DialogueNative* req) {
    if (!g_ready || !g_on_dialogue || !req) {
        return -1;
    }
    return g_on_dialogue(req);
}

}  // namespace grandia_mod
