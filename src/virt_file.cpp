#include "virt_file.h"

#include "clr_host.h"
#include "hook_util.h"
#include "log.h"

#include <Windows.h>

#include <cctype>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <string>
#include <sys/stat.h>
#include <vector>

namespace grandia_mod {
namespace {

using FopenFn = FILE*(__cdecl*)(const char*, const char*);
using FcloseFn = int(__cdecl*)(FILE*);
using FreadFn = std::size_t(__cdecl*)(void*, std::size_t, std::size_t, FILE*);
using FwriteFn = std::size_t(__cdecl*)(const void*, std::size_t, std::size_t, FILE*);
using FseekFn = int(__cdecl*)(FILE*, long, int);
using FtellFn = long(__cdecl*)(FILE*);
using FgetcFn = int(__cdecl*)(FILE*);
using UngetcFn = int(__cdecl*)(int, FILE*);
using RewindFn = void(__cdecl*)(FILE*);
using FflushFn = int(__cdecl*)(FILE*);
using Fseeki64Fn = int(__cdecl*)(FILE*, std::int64_t, int);
using FgetposFn = int(__cdecl*)(FILE*, void*);
using FsetposFn = int(__cdecl*)(FILE*, const void*);
using Stat64i32Fn = int(__cdecl*)(const char*, struct _stat64i32*);
using SdlRwFromFileFn = void*(__cdecl*)(const char*, const char*);
using SdlRwFromConstMemFn = void*(__cdecl*)(const void*, int);

constexpr std::uintptr_t kFcloseIatRva = 0x1FE3ECu;
constexpr std::uintptr_t kRewindIatRva = 0x1FE3E8u;
constexpr std::uintptr_t kFflushIatRva = 0x1FE404u;
constexpr std::uintptr_t kFgetcIatRva = 0x1FE414u;
constexpr std::uintptr_t kFtellIatRva = 0x1FE41Cu;
constexpr std::uintptr_t kFreadIatRva = 0x1FE420u;
constexpr std::uintptr_t kFwriteIatRva = 0x1FE424u;
constexpr std::uintptr_t kFseekIatRva = 0x1FE42Cu;
constexpr std::uintptr_t kFgetposIatRva = 0x1FE434u;
constexpr std::uintptr_t kFseeki64IatRva = 0x1FE438u;
constexpr std::uintptr_t kFsetposIatRva = 0x1FE43Cu;
constexpr std::uintptr_t kUngetcIatRva = 0x1FE440u;
constexpr std::uintptr_t kStat64IatRva = 0x1FE320u;
constexpr std::uintptr_t kSdlRwFromFileIatRva = 0x1FE24Cu;

struct VirtFile {
    FILE* dummy = nullptr;
    const std::uint8_t* data = nullptr;
    std::size_t size = 0;
    std::size_t pos = 0;
    int ungetc = EOF;
};

std::mutex g_mu;
std::vector<VirtFile> g_files;
FopenFn g_orig_fopen = nullptr;
FcloseFn g_orig_fclose = nullptr;
FreadFn g_orig_fread = nullptr;
FwriteFn g_orig_fwrite = nullptr;
FseekFn g_orig_fseek = nullptr;
FtellFn g_orig_ftell = nullptr;
FgetcFn g_orig_fgetc = nullptr;
UngetcFn g_orig_ungetc = nullptr;
RewindFn g_orig_rewind = nullptr;
FflushFn g_orig_fflush = nullptr;
Fseeki64Fn g_orig_fseeki64 = nullptr;
FgetposFn g_orig_fgetpos = nullptr;
FsetposFn g_orig_fsetpos = nullptr;
Stat64i32Fn g_orig_stat = nullptr;
SdlRwFromFileFn g_orig_sdl_rw = nullptr;
SdlRwFromConstMemFn g_sdl_rw_mem = nullptr;

void** g_fclose_slot = nullptr;
void** g_rewind_slot = nullptr;
void** g_fflush_slot = nullptr;
void** g_fgetc_slot = nullptr;
void** g_ftell_slot = nullptr;
void** g_fread_slot = nullptr;
void** g_fwrite_slot = nullptr;
void** g_fseek_slot = nullptr;
void** g_fgetpos_slot = nullptr;
void** g_fseeki64_slot = nullptr;
void** g_fsetpos_slot = nullptr;
void** g_ungetc_slot = nullptr;
void** g_stat_slot = nullptr;
void** g_sdl_slot = nullptr;

void* g_fclose_orig = nullptr;
void* g_rewind_orig = nullptr;
void* g_fflush_orig = nullptr;
void* g_fgetc_orig = nullptr;
void* g_ftell_orig = nullptr;
void* g_fread_orig = nullptr;
void* g_fwrite_orig = nullptr;
void* g_fseek_orig = nullptr;
void* g_fgetpos_orig = nullptr;
void* g_fseeki64_orig = nullptr;
void* g_fsetpos_orig = nullptr;
void* g_ungetc_orig = nullptr;
void* g_stat_orig = nullptr;
void* g_sdl_orig = nullptr;

std::string ToUpperAscii(std::string s) {
    for (char& c : s) {
        if (c >= 'a' && c <= 'z') {
            c = static_cast<char>(c - 'a' + 'A');
        }
    }
    return s;
}

std::string Basename(const char* path) {
    if (!path || !*path) {
        return {};
    }
    const char* base = path;
    for (const char* p = path; *p; ++p) {
        if (*p == '\\' || *p == '/') {
            base = p + 1;
        }
    }
    return base;
}

bool ParseMapPath(const char* path, std::string* stem, int* kind) {
    if (!path) {
        return false;
    }
    const std::string base = Basename(path);
    const char* dot = base.empty() ? nullptr : strrchr(base.c_str(), '.');
    if (!dot) {
        return false;
    }
    int k = -1;
    if (_stricmp(dot, ".MDP") == 0) {
        k = 0;
    } else if (_stricmp(dot, ".SCN") == 0) {
        k = 1;
    } else if (_stricmp(dot, ".OFS") == 0) {
        k = 2;
    } else {
        return false;
    }
    std::string st = ToUpperAscii(base.substr(0, static_cast<std::size_t>(dot - base.c_str())));
    if (st.size() < 3 || st.size() > 8) {
        return false;
    }
    for (unsigned char c : st) {
        if (!std::isxdigit(c)) {
            return false;
        }
    }
    if (stem) {
        *stem = st;
    }
    if (kind) {
        *kind = k;
    }
    return true;
}

bool LookupBytes(const char* path, const void** data, int* len) {
    std::string stem;
    int kind = -1;
    if (!ParseMapPath(path, &stem, &kind)) {
        return false;
    }
    return RuntimeGetMapFile(stem.c_str(), kind, data, len) == 1 && data && *data && len && *len > 0;
}

VirtFile* Find(FILE* f) {
    if (!f) {
        return nullptr;
    }
    for (auto& v : g_files) {
        if (v.dummy == f) {
            return &v;
        }
    }
    return nullptr;
}

void CloseEntry(VirtFile* v) {
    if (!v) {
        return;
    }
    if (v->dummy && g_orig_fclose) {
        g_orig_fclose(v->dummy);
    }
    v->dummy = nullptr;
    v->data = nullptr;
    v->size = 0;
    v->pos = 0;
    v->ungetc = EOF;
}

bool PatchSlot(std::uintptr_t rva, void* detour, void*** slot_out, void** orig_out) {
    auto* slot = reinterpret_cast<void**>(ModuleBase() + rva);
    if (!slot || !*slot || !detour) {
        return false;
    }
    DWORD old = 0;
    if (!VirtualProtect(slot, sizeof(void*), PAGE_EXECUTE_READWRITE, &old)) {
        return false;
    }
    if (orig_out) {
        *orig_out = *slot;
    }
    *slot = detour;
    VirtualProtect(slot, sizeof(void*), old, &old);
    if (slot_out) {
        *slot_out = slot;
    }
    return true;
}

void RestoreSlot(void** slot, void* orig) {
    if (!slot || !orig) {
        return;
    }
    DWORD old = 0;
    if (!VirtualProtect(slot, sizeof(void*), PAGE_EXECUTE_READWRITE, &old)) {
        return;
    }
    *slot = orig;
    VirtualProtect(slot, sizeof(void*), old, &old);
}

int SeekTo(VirtFile* v, std::int64_t off, int origin) {
    std::int64_t base = 0;
    if (origin == SEEK_CUR) {
        base = static_cast<std::int64_t>(v->pos);
    } else if (origin == SEEK_END) {
        base = static_cast<std::int64_t>(v->size);
    } else if (origin != SEEK_SET) {
        return -1;
    }
    const std::int64_t next = base + off;
    if (next < 0 || static_cast<std::uint64_t>(next) > v->size) {
        return -1;
    }
    v->pos = static_cast<std::size_t>(next);
    v->ungetc = EOF;
    return 0;
}

int __cdecl HookFclose(FILE* f) {
    std::lock_guard<std::mutex> lock(g_mu);
    if (auto* v = Find(f)) {
        CloseEntry(v);
        g_files.erase(g_files.begin() + (v - g_files.data()));
        return 0;
    }
    return g_orig_fclose ? g_orig_fclose(f) : EOF;
}

std::size_t __cdecl HookFread(void* buf, std::size_t size, std::size_t count, FILE* f) {
    if (!buf || size == 0 || count == 0) {
        return 0;
    }
    std::lock_guard<std::mutex> lock(g_mu);
    auto* v = Find(f);
    if (!v) {
        return g_orig_fread ? g_orig_fread(buf, size, count, f) : 0;
    }
    auto* out = static_cast<std::uint8_t*>(buf);
    std::size_t got = 0;
    if (v->ungetc != EOF) {
        if (size == 1) {
            *out = static_cast<std::uint8_t>(v->ungetc);
            v->ungetc = EOF;
            ++got;
        } else {
            v->ungetc = EOF;
        }
    }
    const std::size_t want = size * count;
    const std::size_t left = v->pos < v->size ? v->size - v->pos : 0;
    const std::size_t n = want - got < left ? want - got : left;
    if (n > 0) {
        std::memcpy(out + got, v->data + v->pos, n);
        v->pos += n;
        got += n;
    }
    return size ? got / size : 0;
}

std::size_t __cdecl HookFwrite(const void* buf, std::size_t size, std::size_t count, FILE* f) {
    std::lock_guard<std::mutex> lock(g_mu);
    if (Find(f)) {
        return 0;
    }
    return g_orig_fwrite ? g_orig_fwrite(buf, size, count, f) : 0;
}

int __cdecl HookFseek(FILE* f, long off, int origin) {
    std::lock_guard<std::mutex> lock(g_mu);
    if (auto* v = Find(f)) {
        return SeekTo(v, off, origin);
    }
    return g_orig_fseek ? g_orig_fseek(f, off, origin) : -1;
}

int __cdecl HookFseeki64(FILE* f, std::int64_t off, int origin) {
    std::lock_guard<std::mutex> lock(g_mu);
    if (auto* v = Find(f)) {
        return SeekTo(v, off, origin);
    }
    return g_orig_fseeki64 ? g_orig_fseeki64(f, off, origin) : -1;
}

long __cdecl HookFtell(FILE* f) {
    std::lock_guard<std::mutex> lock(g_mu);
    if (auto* v = Find(f)) {
        return static_cast<long>(v->pos);
    }
    return g_orig_ftell ? g_orig_ftell(f) : -1L;
}

int __cdecl HookFgetc(FILE* f) {
    std::lock_guard<std::mutex> lock(g_mu);
    auto* v = Find(f);
    if (!v) {
        return g_orig_fgetc ? g_orig_fgetc(f) : EOF;
    }
    if (v->ungetc != EOF) {
        const int c = v->ungetc;
        v->ungetc = EOF;
        return c;
    }
    if (v->pos >= v->size) {
        return EOF;
    }
    return v->data[v->pos++];
}

int __cdecl HookUngetc(int c, FILE* f) {
    std::lock_guard<std::mutex> lock(g_mu);
    if (auto* v = Find(f)) {
        if (c == EOF) {
            return EOF;
        }
        v->ungetc = c;
        return c;
    }
    return g_orig_ungetc ? g_orig_ungetc(c, f) : EOF;
}

void __cdecl HookRewind(FILE* f) {
    std::lock_guard<std::mutex> lock(g_mu);
    if (auto* v = Find(f)) {
        v->pos = 0;
        v->ungetc = EOF;
        return;
    }
    if (g_orig_rewind) {
        g_orig_rewind(f);
    }
}

int __cdecl HookFflush(FILE* f) {
    std::lock_guard<std::mutex> lock(g_mu);
    if (Find(f)) {
        return 0;
    }
    return g_orig_fflush ? g_orig_fflush(f) : 0;
}

int __cdecl HookFgetpos(FILE* f, void* pos) {
    std::lock_guard<std::mutex> lock(g_mu);
    if (auto* v = Find(f)) {
        if (!pos) {
            return -1;
        }
        *static_cast<std::int64_t*>(pos) = static_cast<std::int64_t>(v->pos);
        return 0;
    }
    return g_orig_fgetpos ? g_orig_fgetpos(f, pos) : -1;
}

int __cdecl HookFsetpos(FILE* f, const void* pos) {
    std::lock_guard<std::mutex> lock(g_mu);
    if (auto* v = Find(f)) {
        if (!pos) {
            return -1;
        }
        return SeekTo(v, *static_cast<const std::int64_t*>(pos), SEEK_SET);
    }
    return g_orig_fsetpos ? g_orig_fsetpos(f, pos) : -1;
}

int __cdecl HookStat(const char* path, struct _stat64i32* buf) {
    const void* data = nullptr;
    int len = 0;
    if (LookupBytes(path, &data, &len) && buf) {
        std::memset(buf, 0, sizeof(*buf));
        buf->st_mode = _S_IFREG | _S_IREAD;
        buf->st_nlink = 1;
        buf->st_size = len;
        return 0;
    }
    return g_orig_stat ? g_orig_stat(path, buf) : -1;
}

void* __cdecl HookSdlRwFromFile(const char* path, const char* mode) {
    const void* data = nullptr;
    int len = 0;
    if (g_sdl_rw_mem && LookupBytes(path, &data, &len)) {
        return g_sdl_rw_mem(data, len);
    }
    return g_orig_sdl_rw ? g_orig_sdl_rw(path, mode) : nullptr;
}

}  // namespace

void VirtFileSetOrigFopen(FILE*(__cdecl* fopen_fn)(const char*, const char*)) {
    g_orig_fopen = fopen_fn;
}

FILE* VirtFileOpen(const char* path, const char* mode) {
    const void* data = nullptr;
    int len = 0;
    if (!LookupBytes(path, &data, &len)) {
        return nullptr;
    }
    if (mode && (std::strchr(mode, 'w') || std::strchr(mode, 'a') || std::strchr(mode, '+'))) {
        return nullptr;
    }
    if (!g_orig_fopen) {
        return nullptr;
    }
    FILE* dummy = g_orig_fopen("NUL", "rb");
    if (!dummy) {
        LogWarn("virt file: fopen(NUL) failed for %s", path);
        return nullptr;
    }
    std::lock_guard<std::mutex> lock(g_mu);
    g_files.push_back(VirtFile{dummy, static_cast<const std::uint8_t*>(data),
                               static_cast<std::size_t>(len), 0, EOF});
    LogInfo("virt file: %s %d bytes", path, len);
    return dummy;
}

int VirtFileStat(const char* path, void* stat_buf) {
    return HookStat(path, static_cast<struct _stat64i32*>(stat_buf));
}

bool InstallVirtFileHooks() {
    if (g_fread_slot) {
        return true;
    }
    if (ModuleBase() == 0) {
        return false;
    }

    bool ok = true;
    ok = PatchSlot(kFcloseIatRva, reinterpret_cast<void*>(&HookFclose), &g_fclose_slot,
                   &g_fclose_orig) &&
         ok;
    ok = PatchSlot(kFreadIatRva, reinterpret_cast<void*>(&HookFread), &g_fread_slot, &g_fread_orig) &&
         ok;
    ok = PatchSlot(kFwriteIatRva, reinterpret_cast<void*>(&HookFwrite), &g_fwrite_slot,
                   &g_fwrite_orig) &&
         ok;
    ok = PatchSlot(kFseekIatRva, reinterpret_cast<void*>(&HookFseek), &g_fseek_slot, &g_fseek_orig) &&
         ok;
    ok = PatchSlot(kFtellIatRva, reinterpret_cast<void*>(&HookFtell), &g_ftell_slot, &g_ftell_orig) &&
         ok;
    ok = PatchSlot(kFgetcIatRva, reinterpret_cast<void*>(&HookFgetc), &g_fgetc_slot, &g_fgetc_orig) &&
         ok;
    ok = PatchSlot(kUngetcIatRva, reinterpret_cast<void*>(&HookUngetc), &g_ungetc_slot,
                   &g_ungetc_orig) &&
         ok;
    ok = PatchSlot(kRewindIatRva, reinterpret_cast<void*>(&HookRewind), &g_rewind_slot,
                   &g_rewind_orig) &&
         ok;
    ok = PatchSlot(kFflushIatRva, reinterpret_cast<void*>(&HookFflush), &g_fflush_slot,
                   &g_fflush_orig) &&
         ok;
    ok = PatchSlot(kFseeki64IatRva, reinterpret_cast<void*>(&HookFseeki64), &g_fseeki64_slot,
                   &g_fseeki64_orig) &&
         ok;
    ok = PatchSlot(kFgetposIatRva, reinterpret_cast<void*>(&HookFgetpos), &g_fgetpos_slot,
                   &g_fgetpos_orig) &&
         ok;
    ok = PatchSlot(kFsetposIatRva, reinterpret_cast<void*>(&HookFsetpos), &g_fsetpos_slot,
                   &g_fsetpos_orig) &&
         ok;
    ok = PatchSlot(kStat64IatRva, reinterpret_cast<void*>(&HookStat), &g_stat_slot, &g_stat_orig) &&
         ok;

    g_orig_fclose = reinterpret_cast<FcloseFn>(g_fclose_orig);
    g_orig_fread = reinterpret_cast<FreadFn>(g_fread_orig);
    g_orig_fwrite = reinterpret_cast<FwriteFn>(g_fwrite_orig);
    g_orig_fseek = reinterpret_cast<FseekFn>(g_fseek_orig);
    g_orig_ftell = reinterpret_cast<FtellFn>(g_ftell_orig);
    g_orig_fgetc = reinterpret_cast<FgetcFn>(g_fgetc_orig);
    g_orig_ungetc = reinterpret_cast<UngetcFn>(g_ungetc_orig);
    g_orig_rewind = reinterpret_cast<RewindFn>(g_rewind_orig);
    g_orig_fflush = reinterpret_cast<FflushFn>(g_fflush_orig);
    g_orig_fseeki64 = reinterpret_cast<Fseeki64Fn>(g_fseeki64_orig);
    g_orig_fgetpos = reinterpret_cast<FgetposFn>(g_fgetpos_orig);
    g_orig_fsetpos = reinterpret_cast<FsetposFn>(g_fsetpos_orig);
    g_orig_stat = reinterpret_cast<Stat64i32Fn>(g_stat_orig);

    if (PatchSlot(kSdlRwFromFileIatRva, reinterpret_cast<void*>(&HookSdlRwFromFile), &g_sdl_slot,
                  &g_sdl_orig)) {
        g_orig_sdl_rw = reinterpret_cast<SdlRwFromFileFn>(g_sdl_orig);
        if (HMODULE sdl = GetModuleHandleA("SDL2.dll")) {
            g_sdl_rw_mem = reinterpret_cast<SdlRwFromConstMemFn>(
                GetProcAddress(sdl, "SDL_RWFromConstMem"));
        }
    }

    if (!ok) {
        LogWarn("virt file: some stdio IAT hooks failed");
        return false;
    }
    LogInfo("virt file: stdio IAT hooked (embedded maps)");
    return true;
}

void RemoveVirtFileHooks() {
    RestoreSlot(g_fclose_slot, g_fclose_orig);
    RestoreSlot(g_fread_slot, g_fread_orig);
    RestoreSlot(g_fwrite_slot, g_fwrite_orig);
    RestoreSlot(g_fseek_slot, g_fseek_orig);
    RestoreSlot(g_ftell_slot, g_ftell_orig);
    RestoreSlot(g_fgetc_slot, g_fgetc_orig);
    RestoreSlot(g_ungetc_slot, g_ungetc_orig);
    RestoreSlot(g_rewind_slot, g_rewind_orig);
    RestoreSlot(g_fflush_slot, g_fflush_orig);
    RestoreSlot(g_fseeki64_slot, g_fseeki64_orig);
    RestoreSlot(g_fgetpos_slot, g_fgetpos_orig);
    RestoreSlot(g_fsetpos_slot, g_fsetpos_orig);
    RestoreSlot(g_stat_slot, g_stat_orig);
    RestoreSlot(g_sdl_slot, g_sdl_orig);
    g_fclose_slot = nullptr;
    g_fread_slot = nullptr;
    g_fwrite_slot = nullptr;
    g_fseek_slot = nullptr;
    g_ftell_slot = nullptr;
    g_fgetc_slot = nullptr;
    g_ungetc_slot = nullptr;
    g_rewind_slot = nullptr;
    g_fflush_slot = nullptr;
    g_fseeki64_slot = nullptr;
    g_fgetpos_slot = nullptr;
    g_fsetpos_slot = nullptr;
    g_stat_slot = nullptr;
    g_sdl_slot = nullptr;
    std::lock_guard<std::mutex> lock(g_mu);
    for (auto& v : g_files) {
        CloseEntry(&v);
    }
    g_files.clear();
}

}  // namespace grandia_mod
