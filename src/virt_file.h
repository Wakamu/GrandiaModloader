#pragma once

#include <cstdio>

namespace grandia_mod {

bool InstallVirtFileHooks();
void RemoveVirtFileHooks();
void VirtFileSetOrigFopen(FILE*(__cdecl* fopen_fn)(const char*, const char*));

// nullptr = not an embedded map; caller should use the real fopen.
FILE* VirtFileOpen(const char* path, const char* mode);
int VirtFileStat(const char* path, void* stat_buf);

}  // namespace grandia_mod
