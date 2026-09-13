#pragma once

#include <cstdint>
#include <cstring>

#include <Windows.h>

namespace grandia_mod {

bool BytesMatch(const std::uint8_t* data, const std::uint8_t* expected, std::size_t size);
bool IsExecutableAddress(void* address);
bool WriteJump(void* site, void* destination, std::uint8_t* original_out, std::size_t patch_size);
void RestoreBytes(void* site, const std::uint8_t* original, std::size_t size);
void* ReadIatFunction(void* call_site);
void* MakeTrampoline(const void* stolen, std::size_t stolen_size, void* continue_at);
std::uintptr_t ModuleBase();
std::uintptr_t CallerRva(std::uintptr_t caller);
bool IsNearRva(std::uintptr_t rva, std::uintptr_t target, std::uintptr_t slack = 0x10);
std::uintptr_t ScanExecutable(HMODULE module, const int* pat, std::size_t n);
bool SafeReadByte(std::uintptr_t address, std::uint8_t* out_byte);
bool SafeReadU32(std::uintptr_t address, std::uint32_t* out_value);
bool SafeReadPointer(std::uintptr_t address, void** out_pointer);
void ReadMsvcString(std::uintptr_t str, char* dest, int dest_len);
bool ReadHdAssetPath(std::uintptr_t object, char* dest, int dest_len);
void NoteHdAssetPath(const char* path);
bool LastHdSpriteInfoPath(char* dest, int dest_len);
void BindHdObjectPath(std::uintptr_t object);
bool LookupHdObjectPath(std::uintptr_t object, char* dest, int dest_len);
bool SafeWriteByte(std::uintptr_t address, std::uint8_t value);
bool SafeWriteU32(std::uintptr_t address, std::uint32_t value);

}  // namespace grandia_mod
