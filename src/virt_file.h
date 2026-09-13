#pragma once

#include <cstddef>
#include <cstdint>
#include <cstdio>

namespace grandia_mod {

struct CrtStdioFns {
    FILE*(__cdecl* fopen)(const char*, const char*) = nullptr;
    int(__cdecl* fclose)(FILE*) = nullptr;
    std::size_t(__cdecl* fread)(void*, std::size_t, std::size_t, FILE*) = nullptr;
    std::size_t(__cdecl* fwrite)(const void*, std::size_t, std::size_t, FILE*) = nullptr;
    int(__cdecl* fseek)(FILE*, long, int) = nullptr;
};

bool VirtFileOrigStdio(CrtStdioFns* out);

bool InstallVirtFileHooks();
void RemoveVirtFileHooks();
void VirtFileSetOrigFopen(FILE*(__cdecl* fopen_fn)(const char*, const char*));

// nullptr = not an embedded map; caller should use the real fopen.
FILE* VirtFileOpen(const char* path, const char* mode);
int VirtFileStat(const char* path, void* stat_buf);
void VirtFileSetText1(const std::uint8_t* data, std::size_t len);
bool VirtFileHasText1();
void VirtFileSetHd(const char* path, const std::uint8_t* data, std::size_t len);
void PrepareHdAsset(const char* path);

extern "C" int ModSetText1(const void* data, int len);

}  // namespace grandia_mod
