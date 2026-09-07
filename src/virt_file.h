#pragma once

#include <cstddef>
#include <cstdint>
#include <cstdio>

namespace grandia_mod {

bool InstallVirtFileHooks();
void RemoveVirtFileHooks();
void VirtFileSetOrigFopen(FILE*(__cdecl* fopen_fn)(const char*, const char*));

// nullptr = not an embedded map; caller should use the real fopen.
FILE* VirtFileOpen(const char* path, const char* mode);
int VirtFileStat(const char* path, void* stat_buf);
void VirtFileSetText1(const std::uint8_t* data, std::size_t len);
bool VirtFileHasText1();

extern "C" int ModSetText1(const void* data, int len);

}  // namespace grandia_mod
