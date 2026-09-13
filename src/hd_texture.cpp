#include "hd_texture.h"

#include <Windows.h>
#include <wincodec.h>

#include <cstdint>
#include <cstdlib>
#include <cstring>

#pragma comment(lib, "windowscodecs.lib")
#pragma comment(lib, "ole32.lib")

namespace grandia_mod {
namespace {

constexpr int kMaxDim = 8192;
constexpr int kMaxRgba = 96 * 1024 * 1024;

void EnsureCom() {
    static bool once = false;
    if (once) {
        return;
    }
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    once = true;
}

IStream* StreamFromMemory(const void* data, int len) {
    if (!data || len <= 0) {
        return nullptr;
    }
    IStream* stream = nullptr;
    if (FAILED(CreateStreamOnHGlobal(nullptr, TRUE, &stream)) || !stream) {
        return nullptr;
    }
    ULONG wrote = 0;
    if (FAILED(stream->Write(data, static_cast<ULONG>(len), &wrote)) ||
        wrote != static_cast<ULONG>(len)) {
        stream->Release();
        return nullptr;
    }
    LARGE_INTEGER zero{};
    stream->Seek(zero, STREAM_SEEK_SET, nullptr);
    return stream;
}

}  // namespace

int __cdecl HdDecodePng(const std::uint8_t* src, int src_len, int* w, int* h, std::uint8_t** rgba,
                        int* rgba_len) {
    if (w) {
        *w = 0;
    }
    if (h) {
        *h = 0;
    }
    if (rgba) {
        *rgba = nullptr;
    }
    if (rgba_len) {
        *rgba_len = 0;
    }
    if (!src || src_len <= 0 || !w || !h || !rgba || !rgba_len) {
        return 0;
    }

    EnsureCom();
    IWICImagingFactory* factory = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
                                  IID_PPV_ARGS(&factory));
    if (FAILED(hr) || !factory) {
        return 0;
    }

    IStream* stream = StreamFromMemory(src, src_len);
    if (!stream) {
        factory->Release();
        return 0;
    }

    IWICBitmapDecoder* decoder = nullptr;
    hr = factory->CreateDecoderFromStream(stream, nullptr, WICDecodeMetadataCacheOnLoad, &decoder);
    stream->Release();
    if (FAILED(hr) || !decoder) {
        factory->Release();
        return 0;
    }

    IWICBitmapFrameDecode* frame = nullptr;
    hr = decoder->GetFrame(0, &frame);
    if (FAILED(hr) || !frame) {
        decoder->Release();
        factory->Release();
        return 0;
    }

    IWICFormatConverter* conv = nullptr;
    hr = factory->CreateFormatConverter(&conv);
    if (FAILED(hr) || !conv) {
        frame->Release();
        decoder->Release();
        factory->Release();
        return 0;
    }

    hr = conv->Initialize(frame, GUID_WICPixelFormat32bppRGBA, WICBitmapDitherTypeNone, nullptr, 0.0,
                          WICBitmapPaletteTypeCustom);
    UINT iw = 0;
    UINT ih = 0;
    if (SUCCEEDED(hr)) {
        hr = conv->GetSize(&iw, &ih);
    }
    const std::uint64_t bytes = static_cast<std::uint64_t>(iw) * ih * 4;
    if (FAILED(hr) || iw == 0 || ih == 0 || iw > static_cast<UINT>(kMaxDim) ||
        ih > static_cast<UINT>(kMaxDim) || bytes > static_cast<std::uint64_t>(kMaxRgba)) {
        conv->Release();
        frame->Release();
        decoder->Release();
        factory->Release();
        return 0;
    }

    auto* buf = static_cast<std::uint8_t*>(std::malloc(static_cast<std::size_t>(bytes)));
    if (!buf) {
        conv->Release();
        frame->Release();
        decoder->Release();
        factory->Release();
        return 0;
    }

    hr = conv->CopyPixels(nullptr, iw * 4, static_cast<UINT>(bytes), buf);
    conv->Release();
    frame->Release();
    decoder->Release();
    factory->Release();
    if (FAILED(hr)) {
        std::free(buf);
        return 0;
    }

    *w = static_cast<int>(iw);
    *h = static_cast<int>(ih);
    *rgba = buf;
    *rgba_len = static_cast<int>(bytes);
    return 1;
}

int __cdecl HdEncodePng(const std::uint8_t* rgba, int w, int h, std::uint8_t** png, int* png_len) {
    if (png) {
        *png = nullptr;
    }
    if (png_len) {
        *png_len = 0;
    }
    if (!rgba || w <= 0 || h <= 0 || w > kMaxDim || h > kMaxDim || !png || !png_len) {
        return 0;
    }
    const std::uint64_t bytes = static_cast<std::uint64_t>(w) * static_cast<std::uint64_t>(h) * 4;
    if (bytes > static_cast<std::uint64_t>(kMaxRgba)) {
        return 0;
    }

    EnsureCom();
    IWICImagingFactory* factory = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
                                  IID_PPV_ARGS(&factory));
    if (FAILED(hr) || !factory) {
        return 0;
    }

    IStream* stream = nullptr;
    hr = CreateStreamOnHGlobal(nullptr, TRUE, &stream);
    if (FAILED(hr) || !stream) {
        factory->Release();
        return 0;
    }

    IWICBitmapEncoder* encoder = nullptr;
    hr = factory->CreateEncoder(GUID_ContainerFormatPng, nullptr, &encoder);
    if (FAILED(hr) || !encoder) {
        stream->Release();
        factory->Release();
        return 0;
    }

    hr = encoder->Initialize(stream, WICBitmapEncoderNoCache);
    IWICBitmapFrameEncode* frame = nullptr;
    IPropertyBag2* props = nullptr;
    if (SUCCEEDED(hr)) {
        hr = encoder->CreateNewFrame(&frame, &props);
    }
    if (SUCCEEDED(hr) && frame) {
        hr = frame->Initialize(props);
    }
    if (SUCCEEDED(hr) && frame) {
        hr = frame->SetSize(static_cast<UINT>(w), static_cast<UINT>(h));
    }
    WICPixelFormatGUID fmt = GUID_WICPixelFormat32bppRGBA;
    if (SUCCEEDED(hr) && frame) {
        hr = frame->SetPixelFormat(&fmt);
    }
    if (SUCCEEDED(hr) && frame) {
        hr = frame->WritePixels(static_cast<UINT>(h), static_cast<UINT>(w * 4),
                                static_cast<UINT>(bytes), const_cast<BYTE*>(rgba));
    }
    if (SUCCEEDED(hr) && frame) {
        hr = frame->Commit();
    }
    if (SUCCEEDED(hr)) {
        hr = encoder->Commit();
    }
    if (props) {
        props->Release();
    }
    if (frame) {
        frame->Release();
    }
    encoder->Release();
    factory->Release();
    if (FAILED(hr)) {
        stream->Release();
        return 0;
    }

    STATSTG stat{};
    if (FAILED(stream->Stat(&stat, STATFLAG_NONAME)) || stat.cbSize.QuadPart <= 0 ||
        stat.cbSize.QuadPart > 64ll * 1024 * 1024) {
        stream->Release();
        return 0;
    }

    const int len = static_cast<int>(stat.cbSize.QuadPart);
    LARGE_INTEGER zero{};
    stream->Seek(zero, STREAM_SEEK_SET, nullptr);
    auto* out = static_cast<std::uint8_t*>(std::malloc(static_cast<std::size_t>(len)));
    if (!out) {
        stream->Release();
        return 0;
    }
    ULONG got = 0;
    hr = stream->Read(out, static_cast<ULONG>(len), &got);
    stream->Release();
    if (FAILED(hr) || got != static_cast<ULONG>(len)) {
        std::free(out);
        return 0;
    }

    *png = out;
    *png_len = len;
    return 1;
}

void __cdecl HdFreeBuf(void* p) {
    std::free(p);
}

}  // namespace grandia_mod
