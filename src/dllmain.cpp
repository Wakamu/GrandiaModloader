#include "clr_host.h"
#include "events.h"
#include "game.h"
#include "log.h"
#include "overlay.h"
#include "setup.h"
#include "wm_picture.h"

#include <Windows.h>

namespace {

DWORD WINAPI MainThread(LPVOID) {
    __try {
        grandia_mod::LogInfo("MainThread started (pid=%lu)", GetCurrentProcessId());
        if (!grandia_mod::InstallOverlayHooks()) {
            grandia_mod::LogWarn("fopen / map-apply hooks not installed");
        }
        if (!grandia_mod::InstallWorldMapPictureHook()) {
            grandia_mod::LogWarn("world-map picture hook not installed yet");
        }
        if (!grandia_mod::InstallClrHost()) {
            grandia_mod::LogWarn("CLR runtime not installed (OnMapLoad will not run)");
        }
        grandia_mod::SignalRuntimeReady();
        if (grandia_mod::ClrHostReady()) {
            if (!grandia_mod::InstallGameHooks()) {
                grandia_mod::LogWarn("game hooks not installed (OnEventFlag / assign UI / field gold)");
            }
            grandia_mod::InstallGameServices();
        }
        if (!grandia_mod::InstallSetupHooks()) {
            grandia_mod::LogWarn("setup idle gate not installed");
        } else {
            grandia_mod::LogInfo("GrandiaMod ready");
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        grandia_mod::LogWarn("MainThread crashed during init (exception=0x%08X)", GetExceptionCode());
    }
    return 0;
}

}  // namespace

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) {
    switch (reason) {
        case DLL_PROCESS_ATTACH:
            DisableThreadLibraryCalls(module);
            grandia_mod::InitializeLogging();
            grandia_mod::LogInfo("DllMain PROCESS_ATTACH (module=0x%p)", module);
            if (!grandia_mod::InstallWorldMapPictureHook()) {
                grandia_mod::LogWarn("DllMain: world-map HD hooks not installed yet");
            }
            if (!CreateThread(nullptr, 4u * 1024u * 1024u, MainThread, nullptr,
                              STACK_SIZE_PARAM_IS_A_RESERVATION, nullptr)) {
                grandia_mod::LogWarn("CreateThread failed: %lu", GetLastError());
            }
            break;
        case DLL_PROCESS_DETACH:
            grandia_mod::RemoveSetupHooks();
            grandia_mod::RemoveGameHooks();
            grandia_mod::RemoveGameServices();
            grandia_mod::RemoveOverlayHooks();
            grandia_mod::RemoveClrHost();
            grandia_mod::LogInfo("DllMain PROCESS_DETACH");
            grandia_mod::ShutdownLogging();
            break;
        default:
            break;
    }
    return TRUE;
}
