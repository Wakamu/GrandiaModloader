#include "log.h"

#include <Windows.h>

#include <cstdarg>
#include <cstdio>
#include <cstring>

namespace grandia_mod {

namespace {

char g_log_path[MAX_PATH] = "GrandiaMod.log";
CRITICAL_SECTION g_log_lock{};
bool g_log_ready = false;

void LogV(const char* level, const char* fmt, va_list args) {
    char message[1024];
    vsnprintf(message, sizeof(message), fmt, args);

    char line[1100];
    snprintf(line, sizeof(line), "[GrandiaMod][%s] %s\n", level, message);
    OutputDebugStringA(line);

    if (!g_log_ready) {
        return;
    }

    EnterCriticalSection(&g_log_lock);
    FILE* file = nullptr;
    if (fopen_s(&file, g_log_path, "a") == 0 && file) {
        fputs(line, file);
        fflush(file);
        fclose(file);
    }
    LeaveCriticalSection(&g_log_lock);
}

void WriteBootMarker(const char* text) {
    HANDLE file = CreateFileA(g_log_path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                              OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        return;
    }
    DWORD written = 0;
    WriteFile(file, text, static_cast<DWORD>(strlen(text)), &written, nullptr);
    WriteFile(file, "\r\n", 2, &written, nullptr);
    CloseHandle(file);
}

}  // namespace

void InitializeLogging() {
    InitializeCriticalSection(&g_log_lock);

    wchar_t exe_path[MAX_PATH]{};
    if (GetModuleFileNameW(nullptr, exe_path, MAX_PATH) > 0) {
        wchar_t* slash = wcsrchr(exe_path, L'\\');
        if (slash) {
            *(slash + 1) = L'\0';
            wcscat_s(exe_path, L"GrandiaMod.log");
            WideCharToMultiByte(CP_UTF8, 0, exe_path, -1, g_log_path, MAX_PATH, nullptr, nullptr);
        }
    }

    HANDLE truncated = CreateFileA(g_log_path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                                   CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (truncated != INVALID_HANDLE_VALUE) {
        CloseHandle(truncated);
    } else {
        FILE* wipe = nullptr;
        if (fopen_s(&wipe, g_log_path, "w") == 0 && wipe) {
            fclose(wipe);
        }
    }

    g_log_ready = true;
    WriteBootMarker("[GrandiaMod] DllMain: logging initialized");

}

void ShutdownLogging() {
    if (g_log_ready) {
        DeleteCriticalSection(&g_log_lock);
        g_log_ready = false;
    }
}

void LogInfo(const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    LogV("INFO", fmt, args);
    va_end(args);
}

void LogWarn(const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    LogV("WARN", fmt, args);
    va_end(args);
}

}  // namespace grandia_mod
