#pragma once

#include <cstdint>

namespace grandia_mod {

int __cdecl HdDecodePng(const std::uint8_t* src, int src_len, int* w, int* h, std::uint8_t** rgba,
                        int* rgba_len);
int __cdecl HdEncodePng(const std::uint8_t* rgba, int w, int h, std::uint8_t** png, int* png_len);
void __cdecl HdFreeBuf(void* p);

}  // namespace grandia_mod
