#include "wm_picture.h"

#include "hook_util.h"
#include "log.h"

#include <Windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <dxgi.h>
#include <wincodec.h>

#include <cstdint>
#include <cstring>
#include <mutex>
#include <string>
#include <vector>

#pragma comment(lib, "windowscodecs.lib")
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3dcompiler.lib")
#pragma comment(lib, "ole32.lib")

namespace grandia_mod {
namespace {

constexpr int kSlots = 16;
constexpr std::uintptr_t kD3dDevicePtrRva = 0x2C22FCu;
constexpr std::uintptr_t kCreateDeviceIatRva = 0x1FE478u;
constexpr std::uintptr_t kScreenSizeRva = 0x2412E0u;
constexpr std::uintptr_t kHdAdjustFlagRva = 0x240E7Eu;
constexpr std::uintptr_t kHdXOffRva = 0x2186C8u;
constexpr std::uintptr_t kHdXOffScaleRva = 0x209B40u;
constexpr unsigned kAtlasW = 5432;
constexpr unsigned kAtlasH = 1908;
constexpr int kDevCreateTex = 5;
constexpr int kCtxPsSetSrv = 8;
constexpr int kCtxDrawIndexed = 12;
constexpr int kCtxDraw = 13;
constexpr int kCtxDrawIndexedInstanced = 20;
constexpr int kCtxDrawInstanced = 21;

using CreateDeviceAndSwapFn = HRESULT(WINAPI*)(IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT,
                                               const D3D_FEATURE_LEVEL*, UINT, UINT,
                                               const DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**,
                                               ID3D11Device**, D3D_FEATURE_LEVEL*,
                                               ID3D11DeviceContext**);
using CreateTexture2DFn = HRESULT(STDMETHODCALLTYPE*)(ID3D11Device*, const D3D11_TEXTURE2D_DESC*,
                                                      const D3D11_SUBRESOURCE_DATA*,
                                                      ID3D11Texture2D**);
using PsSetSrvFn = void(STDMETHODCALLTYPE*)(ID3D11DeviceContext*, UINT, UINT,
                                            ID3D11ShaderResourceView* const*);
using DrawFn = void(STDMETHODCALLTYPE*)(ID3D11DeviceContext*, UINT, UINT);
using DrawIndexedFn = void(STDMETHODCALLTYPE*)(ID3D11DeviceContext*, UINT, UINT, INT);
using DrawInstancedFn = void(STDMETHODCALLTYPE*)(ID3D11DeviceContext*, UINT, UINT, UINT, UINT);
using DrawIndexedInstancedFn = void(STDMETHODCALLTYPE*)(ID3D11DeviceContext*, UINT, UINT, UINT, INT,
                                                        UINT);
using PresentFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT);

void** g_create_device_iat = nullptr;
CreateDeviceAndSwapFn g_orig_create_device = nullptr;

struct CustomPic {
    bool on = false;
    char path[260]{};
    int dest_x = 0;
    int dest_y = 0;
    int dest_w = 0;
    int dest_h = 0;
    int want_w = 0;
    int want_h = 0;
    unsigned tex_w = 0;
    unsigned tex_h = 0;
    ID3D11Texture2D* tex = nullptr;
    ID3D11ShaderResourceView* srv = nullptr;
};

struct OverlayVert {
    float x;
    float y;
    float u;
    float v;
};

std::recursive_mutex g_pic_mutex;
CustomPic g_pics[kSlots]{};
bool g_com = false;

ID3D11Device* g_device = nullptr;
ID3D11DeviceContext* g_ctx = nullptr;
ID3D11Texture2D* g_atlas = nullptr;
ID3D11VertexShader* g_vs = nullptr;
ID3D11PixelShader* g_ps = nullptr;
ID3D11InputLayout* g_layout = nullptr;
ID3D11Buffer* g_vb = nullptr;
ID3D11Buffer* g_fade_cb = nullptr;
ID3D11SamplerState* g_samp = nullptr;
ID3D11BlendState* g_blend = nullptr;
ID3D11RasterizerState* g_rast = nullptr;
ID3D11DepthStencilState* g_depth = nullptr;

void** g_dev_slot = nullptr;
void** g_ctx_slot_srv = nullptr;
void** g_ctx_slot_draw = nullptr;
void** g_ctx_slot_draw_i = nullptr;
void** g_ctx_slot_draw_in = nullptr;
void** g_ctx_slot_draw_n = nullptr;
CreateTexture2DFn g_orig_create_tex = nullptr;
PsSetSrvFn g_orig_ps_set_srv = nullptr;
DrawFn g_orig_draw = nullptr;
DrawIndexedFn g_orig_draw_indexed = nullptr;
DrawInstancedFn g_orig_draw_instanced = nullptr;
DrawIndexedInstancedFn g_orig_draw_indexed_instanced = nullptr;
PresentFn g_orig_present = nullptr;
void** g_present_slot = nullptr;
bool g_wrapped_dev = false;
bool g_wrapped_ctx = false;
bool g_atlas_bound = false;
bool g_in_overlay = false;
bool g_gpu_ready = false;
int g_overlay_logs = 0;
int g_skip_logs = 0;
int g_submit_logs = 0;
DWORD g_dest_tick = 0;
DWORD g_fade_start = 0;
bool g_fade_hold = false;
constexpr DWORD kFadeMs = 1000;
constexpr DWORD kDestFreshMs = 120;

void EnsureCom() {
    if (g_com) {
        return;
    }
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    g_com = true;
}

std::wstring Utf8ToWide(const char* utf8) {
    if (!utf8 || !*utf8) {
        return {};
    }
    const int needed = MultiByteToWideChar(CP_UTF8, 0, utf8, -1, nullptr, 0);
    if (needed <= 1) {
        return {};
    }
    std::wstring out(static_cast<std::size_t>(needed - 1), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8, -1, out.data(), needed);
    return out;
}

bool LoadPngRgba(const wchar_t* path, std::vector<std::uint8_t>* rgba, unsigned* w, unsigned* h) {
    EnsureCom();
    IWICImagingFactory* factory = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
                                  IID_PPV_ARGS(&factory));
    if (FAILED(hr) || !factory) {
        return false;
    }
    IWICBitmapDecoder* decoder = nullptr;
    hr = factory->CreateDecoderFromFilename(path, nullptr, GENERIC_READ,
                                            WICDecodeMetadataCacheOnLoad, &decoder);
    if (FAILED(hr) || !decoder) {
        factory->Release();
        return false;
    }
    IWICBitmapFrameDecode* frame = nullptr;
    hr = decoder->GetFrame(0, &frame);
    if (FAILED(hr) || !frame) {
        decoder->Release();
        factory->Release();
        return false;
    }
    IWICFormatConverter* conv = nullptr;
    hr = factory->CreateFormatConverter(&conv);
    if (FAILED(hr) || !conv) {
        frame->Release();
        decoder->Release();
        factory->Release();
        return false;
    }
    hr = conv->Initialize(frame, GUID_WICPixelFormat32bppRGBA, WICBitmapDitherTypeNone, nullptr, 0.0,
                          WICBitmapPaletteTypeCustom);
    unsigned iw = 0;
    unsigned ih = 0;
    if (SUCCEEDED(hr)) {
        hr = conv->GetSize(&iw, &ih);
    }
    if (FAILED(hr) || iw == 0 || ih == 0 || iw > 2048 || ih > 512) {
        conv->Release();
        frame->Release();
        decoder->Release();
        factory->Release();
        return false;
    }
    rgba->assign(static_cast<std::size_t>(iw) * ih * 4, 255);
    hr = conv->CopyPixels(nullptr, iw * 4, static_cast<UINT>(rgba->size()), rgba->data());
    conv->Release();
    frame->Release();
    decoder->Release();
    factory->Release();
    if (FAILED(hr)) {
        return false;
    }
    *w = iw;
    *h = ih;
    return true;
}

void ReleasePicGpu(CustomPic* pic) {
    if (pic->srv) {
        pic->srv->Release();
        pic->srv = nullptr;
    }
    if (pic->tex) {
        pic->tex->Release();
        pic->tex = nullptr;
    }
    pic->tex_w = 0;
    pic->tex_h = 0;
}

bool EnsurePicTexture(CustomPic* pic) {
    if (pic->srv) {
        return true;
    }
    if (!g_device || !pic->path[0]) {
        return false;
    }
    std::vector<std::uint8_t> rgba;
    unsigned w = 0;
    unsigned h = 0;
    const auto wide = Utf8ToWide(pic->path);
    if (wide.empty() || !LoadPngRgba(wide.c_str(), &rgba, &w, &h)) {
        LogWarn("world-map HD: cannot load %s", pic->path);
        return false;
    }
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = w;
    desc.Height = h;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_IMMUTABLE;
    desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    D3D11_SUBRESOURCE_DATA data{};
    data.pSysMem = rgba.data();
    data.SysMemPitch = w * 4;
    if (FAILED(g_device->CreateTexture2D(&desc, &data, &pic->tex)) || !pic->tex) {
        return false;
    }
    if (FAILED(g_device->CreateShaderResourceView(pic->tex, nullptr, &pic->srv)) || !pic->srv) {
        ReleasePicGpu(pic);
        return false;
    }
    pic->tex_w = w;
    pic->tex_h = h;
    return true;
}

bool CompileShaders() {
    if (g_vs && g_ps && g_layout) {
        return true;
    }
    static const char kSrc[] = R"(
        struct VSIn { float2 pos : POSITION; float2 uv : TEXCOORD0; };
        struct VSOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        Texture2D tex : register(t0);
        SamplerState samp : register(s0);
        VSOut vs(VSIn i) {
            VSOut o;
            o.pos = float4(i.pos, 0.0, 1.0);
            o.uv = i.uv;
            return o;
        }
        cbuffer FadeBuf : register(b0) { float fade; float3 fade_pad; };
        float4 ps(VSOut i) : SV_TARGET {
            float4 c = tex.Sample(samp, i.uv);
            c *= fade;
            return c;
        }
    )";
    ID3DBlob* vsb = nullptr;
    ID3DBlob* psb = nullptr;
    ID3DBlob* err = nullptr;
    HRESULT hr = D3DCompile(kSrc, sizeof(kSrc) - 1, nullptr, nullptr, nullptr, "vs", "vs_4_0", 0, 0,
                            &vsb, &err);
    if (FAILED(hr) || !vsb) {
        if (err) {
            LogWarn("world-map HD: VS compile failed: %s", static_cast<const char*>(err->GetBufferPointer()));
            err->Release();
        }
        return false;
    }
    if (err) {
        err->Release();
        err = nullptr;
    }
    hr = D3DCompile(kSrc, sizeof(kSrc) - 1, nullptr, nullptr, nullptr, "ps", "ps_4_0", 0, 0, &psb,
                    &err);
    if (FAILED(hr) || !psb) {
        if (err) {
            LogWarn("world-map HD: PS compile failed: %s", static_cast<const char*>(err->GetBufferPointer()));
            err->Release();
        }
        vsb->Release();
        return false;
    }
    if (err) {
        err->Release();
    }
    if (FAILED(g_device->CreateVertexShader(vsb->GetBufferPointer(), vsb->GetBufferSize(), nullptr,
                                            &g_vs)) ||
        FAILED(g_device->CreatePixelShader(psb->GetBufferPointer(), psb->GetBufferSize(), nullptr,
                                           &g_ps))) {
        vsb->Release();
        psb->Release();
        return false;
    }
    D3D11_INPUT_ELEMENT_DESC il[] = {
        {"POSITION", 0, DXGI_FORMAT_R32G32_FLOAT, 0, 0, D3D11_INPUT_PER_VERTEX_DATA, 0},
        {"TEXCOORD", 0, DXGI_FORMAT_R32G32_FLOAT, 0, 8, D3D11_INPUT_PER_VERTEX_DATA, 0},
    };
    hr = g_device->CreateInputLayout(il, 2, vsb->GetBufferPointer(), vsb->GetBufferSize(), &g_layout);
    vsb->Release();
    psb->Release();
    return SUCCEEDED(hr) && g_layout;
}

bool EnsureGpu() {
    if (g_gpu_ready) {
        return true;
    }
    if (!g_device || !g_ctx) {
        return false;
    }
    if (!CompileShaders()) {
        return false;
    }
    D3D11_BUFFER_DESC bd{};
    bd.ByteWidth = sizeof(OverlayVert) * 4;
    bd.Usage = D3D11_USAGE_DYNAMIC;
    bd.BindFlags = D3D11_BIND_VERTEX_BUFFER;
    bd.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    if (FAILED(g_device->CreateBuffer(&bd, nullptr, &g_vb))) {
        return false;
    }
    D3D11_BUFFER_DESC cbd{};
    cbd.ByteWidth = 16;
    cbd.Usage = D3D11_USAGE_DYNAMIC;
    cbd.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbd.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    if (FAILED(g_device->CreateBuffer(&cbd, nullptr, &g_fade_cb))) {
        return false;
    }
    D3D11_SAMPLER_DESC sd{};
    sd.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sd.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
    sd.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
    sd.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
    sd.MaxLOD = D3D11_FLOAT32_MAX;
    if (FAILED(g_device->CreateSamplerState(&sd, &g_samp))) {
        return false;
    }
    D3D11_BLEND_DESC bl{};
    bl.RenderTarget[0].BlendEnable = TRUE;
    bl.RenderTarget[0].SrcBlend = D3D11_BLEND_SRC_ALPHA;
    bl.RenderTarget[0].DestBlend = D3D11_BLEND_INV_SRC_ALPHA;
    bl.RenderTarget[0].BlendOp = D3D11_BLEND_OP_ADD;
    bl.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ONE;
    bl.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_INV_SRC_ALPHA;
    bl.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
    bl.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
    if (FAILED(g_device->CreateBlendState(&bl, &g_blend))) {
        return false;
    }
    D3D11_RASTERIZER_DESC rd{};
    rd.FillMode = D3D11_FILL_SOLID;
    rd.CullMode = D3D11_CULL_NONE;
    rd.DepthClipEnable = TRUE;
    if (FAILED(g_device->CreateRasterizerState(&rd, &g_rast))) {
        return false;
    }
    D3D11_DEPTH_STENCIL_DESC dd{};
    dd.DepthEnable = FALSE;
    dd.StencilEnable = FALSE;
    if (FAILED(g_device->CreateDepthStencilState(&dd, &g_depth))) {
        return false;
    }
    g_gpu_ready = true;
    LogInfo("world-map HD: overlay shaders ready");
    return true;
}

float OverlayFade() {
    if (g_fade_start == 0) {
        return g_fade_hold ? 0.0f : 1.0f;
    }
    const DWORD dt = GetTickCount() - g_fade_start;
    if (dt >= kFadeMs) {
        return 0.0f;
    }
    return 1.0f - static_cast<float>(dt) / static_cast<float>(kFadeMs);
}

void Ps1ToNdc(int x, int y, float* nx, float* ny) {
    std::int16_t sw = 320;
    std::int16_t sh = 240;
    const auto base = ModuleBase();
    std::uint32_t packed = 0;
    if (base != 0 && SafeReadU32(base + kScreenSizeRva, &packed)) {
        sw = static_cast<std::int16_t>(packed & 0xFFFF);
        sh = static_cast<std::int16_t>(packed >> 16);
    }
    if (sw <= 0) {
        sw = 320;
    }
    if (sh <= 0) {
        sh = 240;
    }

    // SoftHD +0x207E0: when [640E7E]==0, dest X and virtual width get a
    // widescreen offset ([6186C8], usually 60) so plates sit in the 4:3
    // pillarbox. Same math or the overlay pans at the wrong rate.
    float fx = static_cast<float>(x);
    float fy = static_cast<float>(y);
    float fsw = static_cast<float>(sw);
    float fsh = static_cast<float>(sh);
    std::uint8_t skip_adjust = 1;
    if (base != 0) {
        SafeReadByte(base + kHdAdjustFlagRva, &skip_adjust);
    }
    if (skip_adjust == 0) {
        float x_off = 60.0f;
        float x_scale = 2.0f;
        std::uint32_t bits = 0;
        if (SafeReadU32(base + kHdXOffRva, &bits)) {
            std::memcpy(&x_off, &bits, sizeof(x_off));
        }
        if (SafeReadU32(base + kHdXOffScaleRva, &bits)) {
            std::memcpy(&x_scale, &bits, sizeof(x_scale));
        }
        fx += x_off;
        fsw += x_off * x_scale;
        if (g_submit_logs == 1) {
            LogInfo("world-map HD: SoftHD NDC xoff=%.1f scale=%.1f sw=%.0f->%.0f", x_off, x_scale,
                    static_cast<float>(sw), fsw);
        }
    }
    if (fsw <= 0.0f) {
        fsw = 320.0f;
    }
    if (fsh <= 0.0f) {
        fsh = 240.0f;
    }
    *nx = (fx / fsw) * 2.0f - 1.0f;
    *ny = 1.0f - (fy / fsh) * 2.0f;
}

void DrawOverlays() {
    if (!g_orig_draw || !EnsureGpu()) {
        return;
    }
    const float fade = OverlayFade();
    if (fade <= 0.01f) {
        return;
    }

    ID3D11InputLayout* old_layout = nullptr;
    ID3D11Buffer* old_vb = nullptr;
    UINT old_stride = 0;
    UINT old_offset = 0;
    D3D11_PRIMITIVE_TOPOLOGY old_topo = D3D11_PRIMITIVE_TOPOLOGY_UNDEFINED;
    ID3D11VertexShader* old_vs = nullptr;
    ID3D11ClassInstance* vs_inst[1]{};
    UINT vs_n = 0;
    ID3D11PixelShader* old_ps = nullptr;
    ID3D11ClassInstance* ps_inst[1]{};
    UINT ps_n = 0;
    ID3D11ShaderResourceView* old_srv = nullptr;
    ID3D11SamplerState* old_samp = nullptr;
    ID3D11BlendState* old_blend = nullptr;
    FLOAT old_factor[4]{};
    UINT old_mask = 0;
    ID3D11DepthStencilState* old_depth = nullptr;
    UINT old_stencil = 0;
    ID3D11RasterizerState* old_rast = nullptr;
    ID3D11Buffer* old_cb = nullptr;
    g_ctx->IAGetInputLayout(&old_layout);
    g_ctx->IAGetVertexBuffers(0, 1, &old_vb, &old_stride, &old_offset);
    g_ctx->IAGetPrimitiveTopology(&old_topo);
    g_ctx->VSGetShader(&old_vs, vs_inst, &vs_n);
    g_ctx->PSGetShader(&old_ps, ps_inst, &ps_n);
    g_ctx->PSGetShaderResources(0, 1, &old_srv);
    g_ctx->PSGetSamplers(0, 1, &old_samp);
    g_ctx->OMGetBlendState(&old_blend, old_factor, &old_mask);
    g_ctx->OMGetDepthStencilState(&old_depth, &old_stencil);
    g_ctx->RSGetState(&old_rast);
    g_ctx->PSGetConstantBuffers(0, 1, &old_cb);

    if (g_fade_cb) {
        D3D11_MAPPED_SUBRESOURCE mapped_cb{};
        if (SUCCEEDED(g_ctx->Map(g_fade_cb, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped_cb)) &&
            mapped_cb.pData) {
            float* slot = static_cast<float*>(mapped_cb.pData);
            slot[0] = fade;
            slot[1] = 0.0f;
            slot[2] = 0.0f;
            slot[3] = 0.0f;
            g_ctx->Unmap(g_fade_cb, 0);
        }
    }

    OverlayVert quad[4]{};
    bool any = false;
    for (int i = 0; i < kSlots; ++i) {
        CustomPic& pic = g_pics[i];
        if (!pic.on || !pic.path[0] || pic.dest_w <= 0 || pic.dest_h <= 0) {
            continue;
        }
        if (!EnsurePicTexture(&pic)) {
            continue;
        }
        int draw_w = pic.dest_w;
        int draw_h = pic.dest_h;
        if (pic.want_w > 0 && pic.want_h > 0) {
            draw_w = pic.want_w;
            draw_h = pic.want_h;
        } else if (pic.want_w > 0) {
            draw_w = pic.want_w;
            if (pic.tex_w > 0 && pic.tex_h > 0) {
                draw_h = static_cast<int>(
                    (static_cast<unsigned>(pic.want_w) * pic.tex_h + pic.tex_w / 2) / pic.tex_w);
            }
        } else if (pic.want_h > 0) {
            draw_h = pic.want_h;
            if (pic.tex_w > 0 && pic.tex_h > 0) {
                draw_w = static_cast<int>(
                    (static_cast<unsigned>(pic.want_h) * pic.tex_w + pic.tex_h / 2) / pic.tex_h);
            }
        }
        if (draw_w < 1) {
            draw_w = 1;
        }
        if (draw_h < 1) {
            draw_h = 1;
        }
        if (draw_w > 512) {
            draw_w = 512;
        }
        if (draw_h > 512) {
            draw_h = 512;
        }
        float x0 = 0;
        float y0 = 0;
        float x1 = 0;
        float y1 = 0;
        Ps1ToNdc(pic.dest_x, pic.dest_y, &x0, &y0);
        Ps1ToNdc(pic.dest_x + draw_w, pic.dest_y + draw_h, &x1, &y1);
        quad[0] = {x0, y0, 0.0f, 0.0f};
        quad[1] = {x1, y0, 1.0f, 0.0f};
        quad[2] = {x0, y1, 0.0f, 1.0f};
        quad[3] = {x1, y1, 1.0f, 1.0f};

        D3D11_MAPPED_SUBRESOURCE mapped{};
        if (FAILED(g_ctx->Map(g_vb, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
            continue;
        }
        std::memcpy(mapped.pData, quad, sizeof(quad));
        g_ctx->Unmap(g_vb, 0);

        const UINT stride = sizeof(OverlayVert);
        const UINT offset = 0;
        g_ctx->IASetInputLayout(g_layout);
        g_ctx->IASetVertexBuffers(0, 1, &g_vb, &stride, &offset);
        g_ctx->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLESTRIP);
        g_ctx->VSSetShader(g_vs, nullptr, 0);
        g_ctx->PSSetShader(g_ps, nullptr, 0);
        g_ctx->PSSetShaderResources(0, 1, &pic.srv);
        g_ctx->PSSetSamplers(0, 1, &g_samp);
        if (g_fade_cb) {
            g_ctx->PSSetConstantBuffers(0, 1, &g_fade_cb);
        }
        g_ctx->OMSetBlendState(g_blend, nullptr, 0xFFFFFFFF);
        g_ctx->OMSetDepthStencilState(g_depth, 0);
        g_ctx->RSSetState(g_rast);
        g_orig_draw(g_ctx, 4, 0);
        any = true;
    }

    g_ctx->IASetInputLayout(old_layout);
    g_ctx->IASetVertexBuffers(0, 1, &old_vb, &old_stride, &old_offset);
    g_ctx->IASetPrimitiveTopology(old_topo);
    g_ctx->VSSetShader(old_vs, vs_n ? vs_inst : nullptr, vs_n);
    g_ctx->PSSetShader(old_ps, ps_n ? ps_inst : nullptr, ps_n);
    g_ctx->PSSetShaderResources(0, 1, &old_srv);
    g_ctx->PSSetSamplers(0, 1, &old_samp);
    g_ctx->OMSetBlendState(old_blend, old_factor, old_mask);
    g_ctx->OMSetDepthStencilState(old_depth, old_stencil);
    g_ctx->RSSetState(old_rast);
    g_ctx->PSSetConstantBuffers(0, 1, &old_cb);
    if (old_layout) {
        old_layout->Release();
    }
    if (old_vb) {
        old_vb->Release();
    }
    if (old_vs) {
        old_vs->Release();
    }
    if (old_ps) {
        old_ps->Release();
    }
    if (vs_n && vs_inst[0]) {
        vs_inst[0]->Release();
    }
    if (ps_n && ps_inst[0]) {
        ps_inst[0]->Release();
    }
    if (old_srv) {
        old_srv->Release();
    }
    if (old_samp) {
        old_samp->Release();
    }
    if (old_blend) {
        old_blend->Release();
    }
    if (old_depth) {
        old_depth->Release();
    }
    if (old_rast) {
        old_rast->Release();
    }
    if (old_cb) {
        old_cb->Release();
    }

    if (any && g_overlay_logs < 1) {
        ++g_overlay_logs;
        LogInfo("world-map HD: custom plate overlay active");
    }
}

bool SrvIsAtlas(ID3D11ShaderResourceView* srv) {
    if (!srv) {
        return false;
    }
    ID3D11Resource* res = nullptr;
    srv->GetResource(&res);
    if (!res) {
        return false;
    }
    ID3D11Texture2D* tex = nullptr;
    bool hit = false;
    if (SUCCEEDED(res->QueryInterface(__uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&tex))) &&
        tex) {
        D3D11_TEXTURE2D_DESC desc{};
        tex->GetDesc(&desc);
        if (desc.Width == kAtlasW && desc.Height == kAtlasH) {
            if (g_atlas != tex) {
                if (g_atlas) {
                    g_atlas->Release();
                }
                g_atlas = tex;
                g_atlas->AddRef();
                LogInfo("world-map HD: areamap atlas %ux%u", desc.Width, desc.Height);
            }
            hit = true;
        }
        tex->Release();
    }
    res->Release();
    return hit;
}

bool AtlasBoundNow() {
    if (!g_ctx) {
        return false;
    }
    ID3D11ShaderResourceView* views[8]{};
    g_ctx->PSGetShaderResources(0, 8, views);
    bool hit = false;
    for (int i = 0; i < 8; ++i) {
        if (SrvIsAtlas(views[i])) {
            hit = true;
        }
        if (views[i]) {
            views[i]->Release();
        }
    }
    if (!hit) {
        ID3D11ShaderResourceView* vs[4]{};
        g_ctx->VSGetShaderResources(0, 4, vs);
        for (int i = 0; i < 4; ++i) {
            if (SrvIsAtlas(vs[i])) {
                hit = true;
            }
            if (vs[i]) {
                vs[i]->Release();
            }
        }
    }
    return hit;
}

bool HaveDests(bool require_recent) {
    if (OverlayFade() <= 0.01f) {
        return false;
    }
    if (require_recent && g_fade_start == 0 &&
        (g_dest_tick == 0 || GetTickCount() - g_dest_tick > kDestFreshMs)) {
        return false;
    }
    for (int i = 0; i < kSlots; ++i) {
        if (g_pics[i].on && g_pics[i].path[0] && g_pics[i].dest_w > 0) {
            return true;
        }
    }
    return false;
}

void FindSwapChain();

void ClearOverlayDests() {
    for (int i = 0; i < kSlots; ++i) {
        g_pics[i].dest_w = 0;
        g_pics[i].dest_h = 0;
    }
    g_dest_tick = 0;
}

void TryDrawCustomPlates(const char* why) {
    if (g_in_overlay || !g_ctx) {
        return;
    }
    std::lock_guard<std::recursive_mutex> lock(g_pic_mutex);
    if (!HaveDests(false)) {
        return;
    }
    g_in_overlay = true;
    const bool gpu = EnsureGpu();
    if (!gpu && g_skip_logs < 6) {
        ++g_skip_logs;
        LogWarn("world-map HD: overlay GPU not ready (%s)", why);
    }
    if (gpu) {
        DrawOverlays();
    }
    g_in_overlay = false;
}

void AfterAtlasDraw() {
    if (g_in_overlay || !g_ctx || !HaveDests(false)) {
        return;
    }
    FindSwapChain();
}

void NoteAtlasSrv(ID3D11ShaderResourceView* const* views, UINT n) {
    if (!views || n == 0) {
        return;
    }
    for (UINT i = 0; i < n; ++i) {
        SrvIsAtlas(views[i]);
    }
}

void TryHookSwapChain(IDXGISwapChain* sc);

HRESULT STDMETHODCALLTYPE HookPresent(IDXGISwapChain* self, UINT sync, UINT flags) {
    if (HaveDests(true)) {
        TryDrawCustomPlates("present");
    } else if (g_fade_hold && OverlayFade() <= 0.01f) {
        std::lock_guard<std::recursive_mutex> lock(g_pic_mutex);
        ClearOverlayDests();
    }
    return g_orig_present(self, sync, flags);
}

HRESULT STDMETHODCALLTYPE HookCreateTexture2D(ID3D11Device* self, const D3D11_TEXTURE2D_DESC* desc,
                                              const D3D11_SUBRESOURCE_DATA* data,
                                              ID3D11Texture2D** out) {
    return g_orig_create_tex(self, desc, data, out);
}

void STDMETHODCALLTYPE HookPsSetSrv(ID3D11DeviceContext* self, UINT start, UINT n,
                                    ID3D11ShaderResourceView* const* views) {
    g_orig_ps_set_srv(self, start, n, views);
    if (!g_in_overlay) {
        NoteAtlasSrv(views, n);
    }
}

void STDMETHODCALLTYPE HookDraw(ID3D11DeviceContext* self, UINT nvert, UINT start) {
    g_orig_draw(self, nvert, start);
    AfterAtlasDraw();
}

void STDMETHODCALLTYPE HookDrawIndexed(ID3D11DeviceContext* self, UINT nidx, UINT start, INT base) {
    g_orig_draw_indexed(self, nidx, start, base);
    AfterAtlasDraw();
}

void STDMETHODCALLTYPE HookDrawInstanced(ID3D11DeviceContext* self, UINT nvert, UINT ninst,
                                         UINT start, UINT start_inst) {
    g_orig_draw_instanced(self, nvert, ninst, start, start_inst);
    AfterAtlasDraw();
}

void STDMETHODCALLTYPE HookDrawIndexedInstanced(ID3D11DeviceContext* self, UINT nidx, UINT ninst,
                                                UINT start, INT base, UINT start_inst) {
    g_orig_draw_indexed_instanced(self, nidx, ninst, start, base, start_inst);
    AfterAtlasDraw();
}

bool PatchVtableSlot(void* obj, int slot, void* hook, void** orig_fn, void*** slot_out) {
    if (!obj || slot < 0 || !hook || !orig_fn) {
        return false;
    }
    void** vtbl = nullptr;
    __try {
        vtbl = *reinterpret_cast<void***>(obj);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
    if (!vtbl) {
        return false;
    }
    DWORD old = 0;
    if (!VirtualProtect(&vtbl[slot], sizeof(void*), PAGE_EXECUTE_READWRITE, &old)) {
        return false;
    }
    if (!*orig_fn) {
        *orig_fn = vtbl[slot];
    }
    vtbl[slot] = hook;
    VirtualProtect(&vtbl[slot], sizeof(void*), old, &old);
    if (slot_out) {
        *slot_out = &vtbl[slot];
    }
    return *orig_fn != nullptr;
}

void TryHookSwapChain(IDXGISwapChain* sc) {
    if (!sc || g_orig_present) {
        return;
    }
    if (!PatchVtableSlot(sc, 8, reinterpret_cast<void*>(&HookPresent),
                         reinterpret_cast<void**>(&g_orig_present), &g_present_slot)) {
        LogWarn("world-map HD: failed to hook SwapChain Present");
        return;
    }
    LogInfo("world-map HD: hooked SwapChain Present");
}

void FindSwapChain() {
    if (g_orig_present || !g_ctx) {
        return;
    }
    ID3D11RenderTargetView* rtv = nullptr;
    g_ctx->OMGetRenderTargets(1, &rtv, nullptr);
    if (!rtv) {
        return;
    }
    ID3D11Resource* res = nullptr;
    rtv->GetResource(&res);
    rtv->Release();
    if (!res) {
        return;
    }
    IDXGIResource* dxgi = nullptr;
    if (SUCCEEDED(res->QueryInterface(__uuidof(IDXGIResource), reinterpret_cast<void**>(&dxgi))) &&
        dxgi) {
        IDXGISwapChain* sc = nullptr;
        if (SUCCEEDED(dxgi->GetParent(__uuidof(IDXGISwapChain), reinterpret_cast<void**>(&sc))) &&
            sc) {
            TryHookSwapChain(sc);
            sc->Release();
        }
        dxgi->Release();
    }
    res->Release();
}

void WrapContext(ID3D11DeviceContext* ctx) {
    if (!ctx || g_wrapped_ctx) {
        return;
    }
    if (!PatchVtableSlot(ctx, kCtxPsSetSrv, reinterpret_cast<void*>(&HookPsSetSrv),
                         reinterpret_cast<void**>(&g_orig_ps_set_srv), &g_ctx_slot_srv) ||
        !PatchVtableSlot(ctx, kCtxDraw, reinterpret_cast<void*>(&HookDraw),
                         reinterpret_cast<void**>(&g_orig_draw), &g_ctx_slot_draw) ||
        !PatchVtableSlot(ctx, kCtxDrawIndexed, reinterpret_cast<void*>(&HookDrawIndexed),
                         reinterpret_cast<void**>(&g_orig_draw_indexed), &g_ctx_slot_draw_i) ||
        !PatchVtableSlot(ctx, kCtxDrawInstanced, reinterpret_cast<void*>(&HookDrawInstanced),
                         reinterpret_cast<void**>(&g_orig_draw_instanced), &g_ctx_slot_draw_n) ||
        !PatchVtableSlot(ctx, kCtxDrawIndexedInstanced,
                         reinterpret_cast<void*>(&HookDrawIndexedInstanced),
                         reinterpret_cast<void**>(&g_orig_draw_indexed_instanced),
                         &g_ctx_slot_draw_in)) {
        LogWarn("world-map HD: failed to hook D3D draw");
        return;
    }
    if (g_ctx) {
        g_ctx->Release();
    }
    g_ctx = ctx;
    g_ctx->AddRef();
    g_wrapped_ctx = true;
    LogInfo("world-map HD: hooked SoftHD D3D Draw");
    FindSwapChain();
}

void WrapDevice(ID3D11Device* dev) {
    if (!dev) {
        return;
    }
    if (!g_wrapped_dev) {
        if (!PatchVtableSlot(dev, kDevCreateTex, reinterpret_cast<void*>(&HookCreateTexture2D),
                             reinterpret_cast<void**>(&g_orig_create_tex), &g_dev_slot)) {
            LogWarn("world-map HD: failed to hook CreateTexture2D");
            return;
        }
        g_device = dev;
        g_device->AddRef();
        g_wrapped_dev = true;
        LogInfo("world-map HD: hooked CreateTexture2D");
    }
    if (!g_wrapped_ctx) {
        ID3D11DeviceContext* ctx = nullptr;
        dev->GetImmediateContext(&ctx);
        if (ctx) {
            WrapContext(ctx);
            ctx->Release();
        }
    }
}

void TryWrapExistingDevice() {
    const auto base = ModuleBase();
    if (base == 0 || g_wrapped_dev) {
        return;
    }
    void* raw = nullptr;
    if (!SafeReadPointer(base + kD3dDevicePtrRva, &raw) || !raw) {
        return;
    }
    ID3D11Device* dev = nullptr;
    ID3D11DeviceContext* ctx = nullptr;
    __try {
        auto* unk = static_cast<IUnknown*>(raw);
        if (FAILED(unk->QueryInterface(__uuidof(ID3D11Device), reinterpret_cast<void**>(&dev)))) {
            dev = nullptr;
        }
        if (!dev &&
            SUCCEEDED(unk->QueryInterface(__uuidof(ID3D11DeviceContext),
                                          reinterpret_cast<void**>(&ctx))) &&
            ctx) {
            ctx->GetDevice(&dev);
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        dev = nullptr;
        ctx = nullptr;
    }
    if (dev) {
        WrapDevice(dev);
        dev->Release();
    }
    if (ctx) {
        ctx->Release();
    }
}

HRESULT WINAPI HookCreateDeviceAndSwap(IDXGIAdapter* adapter, D3D_DRIVER_TYPE type, HMODULE software,
                                       UINT flags, const D3D_FEATURE_LEVEL* levels, UINT nlevels,
                                       UINT sdk, const DXGI_SWAP_CHAIN_DESC* swap_desc,
                                       IDXGISwapChain** swap, ID3D11Device** device,
                                       D3D_FEATURE_LEVEL* out_level, ID3D11DeviceContext** ctx) {
    const HRESULT hr =
        g_orig_create_device(adapter, type, software, flags, levels, nlevels, sdk, swap_desc, swap,
                             device, out_level, ctx);
    if (SUCCEEDED(hr)) {
        if (device && *device) {
            WrapDevice(*device);
        }
        if (ctx && *ctx) {
            WrapContext(*ctx);
        }
        if (swap && *swap) {
            TryHookSwapChain(*swap);
        }
    }
    return hr;
}

bool HookCreateDeviceIat() {
    const auto base = ModuleBase();
    if (base == 0 || g_create_device_iat) {
        return g_create_device_iat != nullptr;
    }
    auto* slot = reinterpret_cast<void**>(base + kCreateDeviceIatRva);
    if (!*slot) {
        return false;
    }
    DWORD old = 0;
    if (!VirtualProtect(slot, sizeof(void*), PAGE_EXECUTE_READWRITE, &old)) {
        return false;
    }
    g_orig_create_device = reinterpret_cast<CreateDeviceAndSwapFn>(*slot);
    *slot = reinterpret_cast<void*>(&HookCreateDeviceAndSwap);
    VirtualProtect(slot, sizeof(void*), old, &old);
    g_create_device_iat = slot;
    LogInfo("world-map HD: D3D11CreateDeviceAndSwapChain IAT hooked");
    return true;
}

void RestoreSlot(void*** slot, void* orig) {
    if (!slot || !*slot || !orig) {
        return;
    }
    DWORD old = 0;
    if (VirtualProtect(*slot, sizeof(void*), PAGE_EXECUTE_READWRITE, &old)) {
        **slot = orig;
        VirtualProtect(*slot, sizeof(void*), old, &old);
    }
    *slot = nullptr;
}

void ReleaseGpu() {
    for (int i = 0; i < kSlots; ++i) {
        ReleasePicGpu(&g_pics[i]);
    }
    if (g_vb) {
        g_vb->Release();
        g_vb = nullptr;
    }
    if (g_fade_cb) {
        g_fade_cb->Release();
        g_fade_cb = nullptr;
    }
    if (g_samp) {
        g_samp->Release();
        g_samp = nullptr;
    }
    if (g_blend) {
        g_blend->Release();
        g_blend = nullptr;
    }
    if (g_rast) {
        g_rast->Release();
        g_rast = nullptr;
    }
    if (g_depth) {
        g_depth->Release();
        g_depth = nullptr;
    }
    if (g_layout) {
        g_layout->Release();
        g_layout = nullptr;
    }
    if (g_vs) {
        g_vs->Release();
        g_vs = nullptr;
    }
    if (g_ps) {
        g_ps->Release();
        g_ps = nullptr;
    }
    g_gpu_ready = false;
}

}  // namespace

void ClearWorldMapCustomPictures() {
    std::lock_guard<std::recursive_mutex> lock(g_pic_mutex);
    for (int i = 0; i < kSlots; ++i) {
        ReleasePicGpu(&g_pics[i]);
        g_pics[i].on = false;
        g_pics[i].path[0] = 0;
        g_pics[i].dest_w = 0;
        g_pics[i].dest_h = 0;
    }
    g_dest_tick = 0;
    g_fade_start = 0;
    g_fade_hold = false;
}

void WorldMapNotifyTravelStarted() {
    std::lock_guard<std::recursive_mutex> lock(g_pic_mutex);
    if (g_fade_start == 0) {
        g_fade_start = GetTickCount();
    }
    g_fade_hold = true;
}

void SetWorldMapCustomPicture(int icon, int x, int y, const char* path, int width, int height) {
    (void)x;
    (void)y;
    if (icon < 0 || icon >= kSlots) {
        return;
    }
    std::lock_guard<std::recursive_mutex> lock(g_pic_mutex);
    CustomPic& pic = g_pics[icon];
    ReleasePicGpu(&pic);
    pic.tex_w = 0;
    pic.tex_h = 0;
    if (!path || !path[0]) {
        pic.on = false;
        pic.path[0] = 0;
        pic.want_w = 0;
        pic.want_h = 0;
        return;
    }
    std::size_t n = 0;
    for (; path[n] && n < sizeof(pic.path) - 1; ++n) {
        pic.path[n] = path[n];
    }
    pic.path[n] = 0;
    pic.on = true;
    pic.want_w = width > 0 ? width : 0;
    pic.want_h = height > 0 ? height : 0;
    LogInfo("world-map custom HD picture icon=%d (SoftHD draw overlay) %s size=%dx%d", icon,
            pic.path, pic.want_w, pic.want_h);
}

void OnIconSubmit(int icon, int dest_x, int dest_y, int dest_w, int dest_h) {
    if (icon < 0 || icon >= kSlots) {
        return;
    }
    std::lock_guard<std::recursive_mutex> lock(g_pic_mutex);
    if (g_fade_hold && OverlayFade() <= 0.01f) {
        return;
    }
    CustomPic& pic = g_pics[icon];
    if (!pic.on) {
        return;
    }
    pic.dest_x = dest_x;
    pic.dest_y = dest_y;
    pic.dest_w = dest_w;
    pic.dest_h = dest_h;
    g_dest_tick = GetTickCount();
    if (g_submit_logs < 6) {
        ++g_submit_logs;
        LogInfo("world-map HD: icon=%d dest=(%d,%d %dx%d) skip SoftHD plate", icon, dest_x, dest_y,
                dest_w, dest_h);
    }
    TryWrapExistingDevice();
    FindSwapChain();
}

bool InstallWorldMapPictureHook() {
#if !defined(_M_IX86)
    return false;
#else
    HookCreateDeviceIat();
    TryWrapExistingDevice();
    LogInfo("world-map picture hook: SoftHD Draw overlay (no hash / no atlas write)");
    return g_create_device_iat != nullptr || g_wrapped_dev;
#endif
}

void RemoveWorldMapPictureHook() {
    RestoreSlot(&g_create_device_iat, reinterpret_cast<void*>(g_orig_create_device));
    g_orig_create_device = nullptr;
    RestoreSlot(&g_dev_slot, reinterpret_cast<void*>(g_orig_create_tex));
    RestoreSlot(&g_ctx_slot_srv, reinterpret_cast<void*>(g_orig_ps_set_srv));
    RestoreSlot(&g_ctx_slot_draw, reinterpret_cast<void*>(g_orig_draw));
    RestoreSlot(&g_ctx_slot_draw_i, reinterpret_cast<void*>(g_orig_draw_indexed));
    RestoreSlot(&g_ctx_slot_draw_n, reinterpret_cast<void*>(g_orig_draw_instanced));
    RestoreSlot(&g_ctx_slot_draw_in, reinterpret_cast<void*>(g_orig_draw_indexed_instanced));
    RestoreSlot(&g_present_slot, reinterpret_cast<void*>(g_orig_present));
    g_orig_present = nullptr;
    ReleaseGpu();
    if (g_atlas) {
        g_atlas->Release();
        g_atlas = nullptr;
    }
    if (g_ctx) {
        g_ctx->Release();
        g_ctx = nullptr;
    }
    if (g_device) {
        g_device->Release();
        g_device = nullptr;
    }
    g_wrapped_dev = false;
    g_wrapped_ctx = false;
}

}  // namespace grandia_mod

extern "C" void WorldMapOnIconSubmit(int icon, int dest_x, int dest_y, int dest_w, int dest_h) {
    grandia_mod::OnIconSubmit(icon, dest_x, dest_y, dest_w, dest_h);
}
