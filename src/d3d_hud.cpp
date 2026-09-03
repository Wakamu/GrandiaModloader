#include "d3d_hud.h"

#include "hook_util.h"
#include "log.h"
#include "qol.h"

#include <Windows.h>
#include <d3d11.h>
#include <dxgi.h>

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <string>
#include <vector>

#pragma comment(lib, "gdi32.lib")

namespace grandia_mod {
namespace {

constexpr UINT kTexW = 800;
constexpr UINT kTexH = 560;
constexpr size_t kMaxToastLines = 8;
constexpr UINT kToastLineH = 30;
constexpr UINT kToastPadY = 8;
constexpr UINT kToastW = 640;
constexpr UINT kToastH = kToastPadY * 2 + static_cast<UINT>(kMaxToastLines) * kToastLineH;
constexpr int kToastX = 12;
constexpr int kToastY = 12;
constexpr size_t kMaxPanelLines = 14;
constexpr UINT kPanelLineH = 40;
constexpr UINT kPanelPad = 28;
constexpr UINT kPanelFontPx = 28;
constexpr UINT kPanelW = 720;
constexpr UINT kPanelH = kPanelPad * 2 + static_cast<UINT>(kMaxPanelLines) * kPanelLineH;

using PresentFn = HRESULT(__stdcall*)(IDXGISwapChain*, UINT, UINT);
using D3D11CreateDeviceAndSwapChain_t = HRESULT(WINAPI*)(
    IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT, const D3D_FEATURE_LEVEL*, UINT, UINT,
    const DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**, ID3D11Device**, D3D_FEATURE_LEVEL*,
    ID3D11DeviceContext**);

void* g_present_site = nullptr;
std::uint8_t g_present_original[16]{};
std::size_t g_present_patch_size = 0;
std::mutex g_present_mu;

struct ToastLine {
    std::wstring text;
    DWORD expire_tick = 0;
    unsigned rgb = 0xFFE528;
};

std::mutex g_toast_mu;
std::vector<ToastLine> g_toasts;
bool g_toast_dirty = true;

struct PanelLine {
    std::wstring text;
    unsigned rgb = 0xFFE528;
};

std::mutex g_panel_mu;
std::vector<PanelLine> g_panel_lines;
bool g_panel_active = false;
bool g_panel_dirty = true;

ID3D11Device* g_cached_device = nullptr;
ID3D11Texture2D* g_overlay_tex = nullptr;
DXGI_FORMAT g_overlay_format = DXGI_FORMAT_UNKNOWN;
std::vector<std::uint8_t> g_pixel_scratch;

std::atomic<bool> g_logged_first{false};
std::atomic<bool> g_logged_text_ok{false};
std::atomic<bool> g_logged_text_fail{false};

std::size_t ChoosePresentPatchSize(const std::uint8_t* p) {
    if (p[0] == 0x8B && p[1] == 0xFF && p[2] == 0x55 && p[3] == 0x8B && p[4] == 0xEC) {
        return 5;
    }
    if (p[0] == 0x55 && p[1] == 0x8B && p[2] == 0xEC && p[3] == 0x83 && p[4] == 0xE4) {
        return 6;
    }
    if (p[0] == 0x55 && p[1] == 0x8B && p[2] == 0xEC && p[3] == 0x83 && p[4] == 0xEC) {
        return 6;
    }
    if (p[0] == 0x55 && p[1] == 0x8B && p[2] == 0xEC && p[3] == 0x81 && p[4] == 0xEC) {
        return 9;
    }
    if (p[0] == 0xE9) {
        return 5;
    }
    if (p[0] == 0x55 && p[1] == 0x8B && p[2] == 0xEC) {
        return 5;
    }
    return 0;
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

void ReleaseOverlaySurface() {
    if (g_overlay_tex) {
        g_overlay_tex->Release();
        g_overlay_tex = nullptr;
    }
    g_overlay_format = DXGI_FORMAT_UNKNOWN;
}

void ReleaseDeviceResources() {
    ReleaseOverlaySurface();
    if (g_cached_device) {
        g_cached_device->Release();
        g_cached_device = nullptr;
    }
}

bool EnsureOverlayTexture(ID3D11Device* device, DXGI_FORMAT format) {
    if (g_cached_device != device) {
        ReleaseOverlaySurface();
        if (g_cached_device) {
            g_cached_device->Release();
            g_cached_device = nullptr;
        }
        g_cached_device = device;
        g_cached_device->AddRef();
        g_toast_dirty = true;
        g_panel_dirty = true;
    }
    if (g_overlay_tex && g_overlay_format == format) {
        return true;
    }
    ReleaseOverlaySurface();
    D3D11_TEXTURE2D_DESC td{};
    td.Width = kTexW;
    td.Height = kTexH;
    td.MipLevels = 1;
    td.ArraySize = 1;
    td.Format = format;
    td.SampleDesc.Count = 1;
    td.Usage = D3D11_USAGE_DEFAULT;
    td.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    const HRESULT hr = device->CreateTexture2D(&td, nullptr, &g_overlay_tex);
    if (FAILED(hr) || !g_overlay_tex) {
        if (!g_logged_text_fail.exchange(true)) {
            LogWarn("D3D overlay: CreateTexture2D failed hr=0x%08X", static_cast<unsigned>(hr));
        }
        return false;
    }
    g_overlay_format = format;
    g_toast_dirty = true;
    g_panel_dirty = true;
    return true;
}

bool PruneExpiredToastsLocked() {
    const DWORD now = GetTickCount();
    const std::size_t before = g_toasts.size();
    g_toasts.erase(std::remove_if(g_toasts.begin(), g_toasts.end(),
                                  [now](const ToastLine& line) {
                                      return line.expire_tick != 0 && now >= line.expire_tick;
                                  }),
                   g_toasts.end());
    if (g_toasts.size() != before) {
        g_toast_dirty = true;
        return true;
    }
    return false;
}

COLORREF RgbToColorRef(unsigned rgb) {
    return RGB((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}

bool RasterizeLinesToRgba(const std::vector<std::wstring>& lines, const std::vector<unsigned>& rgbs,
                          UINT box_w, UINT box_h, UINT line_h, UINT pad_y, int font_px, bool center_text,
                          UINT pixel_h, std::vector<std::uint8_t>* out_rgba) {
    out_rgba->assign(static_cast<std::size_t>(kTexW) * kTexH * 4, 0);
    if (lines.empty() || pixel_h == 0 || box_w == 0 || box_h == 0) {
        return true;
    }

    BITMAPINFO bmi{};
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = static_cast<LONG>(kTexW);
    bmi.bmiHeader.biHeight = -static_cast<LONG>(kTexH);
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32;
    bmi.bmiHeader.biCompression = BI_RGB;

    void* dib_bits = nullptr;
    HDC screen = GetDC(nullptr);
    if (!screen) {
        return false;
    }
    HDC mem = CreateCompatibleDC(screen);
    HBITMAP dib = CreateDIBSection(screen, &bmi, DIB_RGB_COLORS, &dib_bits, nullptr, 0);
    ReleaseDC(nullptr, screen);
    if (!mem || !dib || !dib_bits) {
        if (dib) {
            DeleteObject(dib);
        }
        if (mem) {
            DeleteDC(mem);
        }
        return false;
    }

    HGDIOBJ old_bmp = SelectObject(mem, dib);
    HBRUSH bg = CreateSolidBrush(RGB(16, 18, 28));
    RECT bg_rc = {0, 0, static_cast<LONG>(box_w), static_cast<LONG>(pixel_h)};
    FillRect(mem, &bg_rc, bg);
    DeleteObject(bg);

    HPEN pen = CreatePen(PS_SOLID, 2, RGB(180, 160, 80));
    HGDIOBJ old_pen = SelectObject(mem, pen);
    HGDIOBJ old_br = SelectObject(mem, GetStockObject(NULL_BRUSH));
    Rectangle(mem, 1, 1, static_cast<int>(box_w) - 1, static_cast<int>(pixel_h) - 1);
    SelectObject(mem, old_br);
    SelectObject(mem, old_pen);
    DeleteObject(pen);

    SetBkMode(mem, TRANSPARENT);
    HFONT font = CreateFontW(font_px, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                             OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                             DEFAULT_PITCH | FF_SWISS, L"Segoe UI");
    HGDIOBJ old_font = font ? SelectObject(mem, font) : nullptr;
    const UINT dt_flags = DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS | DT_NOPREFIX |
                          (center_text ? DT_CENTER : DT_LEFT);
    for (std::size_t i = 0; i < lines.size(); ++i) {
        const unsigned rgb = i < rgbs.size() ? rgbs[i] : 0xFFE528u;
        SetTextColor(mem, RgbToColorRef(rgb));
        RECT text_rc = {static_cast<LONG>(pad_y), static_cast<LONG>(pad_y + i * line_h),
                        static_cast<LONG>(box_w - pad_y),
                        static_cast<LONG>(pad_y + (i + 1) * line_h)};
        DrawTextW(mem, lines[i].c_str(), static_cast<int>(lines[i].size()), &text_rc, dt_flags);
    }
    if (old_font) {
        SelectObject(mem, old_font);
    }
    if (font) {
        DeleteObject(font);
    }

    const auto* src = static_cast<const std::uint8_t*>(dib_bits);
    std::uint8_t* dst = out_rgba->data();
    const std::size_t pixels = static_cast<std::size_t>(kTexW) * kTexH;
    for (std::size_t i = 0; i < pixels; ++i) {
        const UINT x = static_cast<UINT>(i % kTexW);
        const UINT y = static_cast<UINT>(i / kTexW);
        if (y >= pixel_h || x >= box_w) {
            dst[i * 4 + 0] = 0;
            dst[i * 4 + 1] = 0;
            dst[i * 4 + 2] = 0;
            dst[i * 4 + 3] = 0;
            continue;
        }
        dst[i * 4 + 0] = src[i * 4 + 2];
        dst[i * 4 + 1] = src[i * 4 + 1];
        dst[i * 4 + 2] = src[i * 4 + 0];
        dst[i * 4 + 3] = 255;
    }
    SelectObject(mem, old_bmp);
    DeleteObject(dib);
    DeleteDC(mem);
    return true;
}

bool GetActiveToastLines(std::vector<std::wstring>* out_lines, std::vector<unsigned>* out_rgbs,
                         UINT* out_pixel_h) {
    std::lock_guard<std::mutex> lock(g_toast_mu);
    PruneExpiredToastsLocked();
    if (g_toasts.empty()) {
        return false;
    }
    out_lines->clear();
    out_rgbs->clear();
    for (const ToastLine& line : g_toasts) {
        out_lines->push_back(line.text);
        out_rgbs->push_back(line.rgb);
    }
    *out_pixel_h = kToastPadY * 2 + static_cast<UINT>(out_lines->size()) * kToastLineH;
    if (*out_pixel_h > kToastH) {
        *out_pixel_h = kToastH;
    }
    return true;
}

bool GetActivePanelLines(std::vector<std::wstring>* out_lines, std::vector<unsigned>* out_rgbs,
                         UINT* out_pixel_h) {
    std::lock_guard<std::mutex> lock(g_panel_mu);
    if (!g_panel_active || g_panel_lines.empty()) {
        return false;
    }
    out_lines->clear();
    out_rgbs->clear();
    for (const PanelLine& line : g_panel_lines) {
        out_lines->push_back(line.text);
        out_rgbs->push_back(line.rgb);
    }
    *out_pixel_h = kPanelPad * 2 + static_cast<UINT>(out_lines->size()) * kPanelLineH;
    if (*out_pixel_h > kPanelH) {
        *out_pixel_h = kPanelH;
    }
    return true;
}

void DrawOverlayText(IDXGISwapChain* swap) {
    std::vector<std::wstring> lines;
    std::vector<unsigned> rgbs;
    UINT pixel_h = 0;
    bool panel = false;
    bool dirty = false;
    {
        std::lock_guard<std::mutex> plock(g_panel_mu);
        if (g_panel_active && !g_panel_lines.empty()) {
            panel = true;
            dirty = g_panel_dirty;
            if (dirty) {
                g_panel_dirty = false;
            }
        }
    }
    if (panel) {
        if (!GetActivePanelLines(&lines, &rgbs, &pixel_h)) {
            return;
        }
    } else {
        if (!GetActiveToastLines(&lines, &rgbs, &pixel_h)) {
            return;
        }
        std::lock_guard<std::mutex> lock(g_toast_mu);
        dirty = g_toast_dirty;
        if (dirty) {
            g_toast_dirty = false;
        }
    }

    ID3D11Device* device = nullptr;
    if (FAILED(swap->GetDevice(__uuidof(ID3D11Device), reinterpret_cast<void**>(&device))) || !device) {
        return;
    }
    DXGI_SWAP_CHAIN_DESC desc{};
    if (FAILED(swap->GetDesc(&desc))) {
        device->Release();
        return;
    }
    DXGI_FORMAT format = desc.BufferDesc.Format;
    if (format == DXGI_FORMAT_UNKNOWN) {
        format = DXGI_FORMAT_R8G8B8A8_UNORM;
    }
    if (format != DXGI_FORMAT_R8G8B8A8_UNORM && format != DXGI_FORMAT_R8G8B8A8_UNORM_SRGB) {
        if (!g_logged_text_fail.exchange(true)) {
            LogWarn("D3D overlay: unsupported backbuffer fmt=%u", static_cast<unsigned>(format));
        }
        device->Release();
        return;
    }
    if (!EnsureOverlayTexture(device, DXGI_FORMAT_R8G8B8A8_UNORM)) {
        device->Release();
        return;
    }
    ID3D11DeviceContext* context = nullptr;
    device->GetImmediateContext(&context);
    if (!context) {
        device->Release();
        return;
    }
    if (dirty) {
        const UINT box_w = panel ? kPanelW : kToastW;
        const UINT box_h = panel ? kPanelH : kToastH;
        const UINT line_h = panel ? kPanelLineH : kToastLineH;
        const UINT pad = panel ? kPanelPad : kToastPadY;
        const int font_px = panel ? static_cast<int>(kPanelFontPx) : 22;
        if (!RasterizeLinesToRgba(lines, rgbs, box_w, box_h, line_h, pad, font_px, panel, pixel_h,
                                  &g_pixel_scratch)) {
            if (!g_logged_text_fail.exchange(true)) {
                LogWarn("D3D overlay: GDI rasterize failed");
            }
            context->Release();
            device->Release();
            return;
        }
        context->UpdateSubresource(g_overlay_tex, 0, nullptr, g_pixel_scratch.data(), kTexW * 4,
                                   kTexW * kTexH * 4);
    }

    ID3D11Texture2D* back_buffer = nullptr;
    if (FAILED(swap->GetBuffer(0, IID_PPV_ARGS(&back_buffer))) || !back_buffer) {
        context->Release();
        device->Release();
        return;
    }
    const UINT dst_w = desc.BufferDesc.Width;
    const UINT dst_h = desc.BufferDesc.Height;
    const UINT box_w = panel ? kPanelW : kToastW;
    UINT dst_x = panel ? ((dst_w > box_w) ? (dst_w - box_w) / 2u : 0u) : static_cast<UINT>(kToastX);
    UINT dst_y = panel ? ((dst_h > pixel_h) ? (dst_h - pixel_h) / 2u : 0u) : static_cast<UINT>(kToastY);
    if (dst_w > dst_x && dst_h > dst_y) {
        const UINT copy_w = (box_w < dst_w - dst_x) ? box_w : (dst_w - dst_x);
        const UINT copy_h = (pixel_h < dst_h - dst_y) ? pixel_h : (dst_h - dst_y);
        D3D11_BOX src_box{};
        src_box.right = copy_w;
        src_box.bottom = copy_h;
        src_box.back = 1;
        context->CopySubresourceRegion(back_buffer, 0, dst_x, dst_y, 0, g_overlay_tex, 0, &src_box);
    }
    back_buffer->Release();
    context->Release();
    device->Release();
    if (!g_logged_text_ok.exchange(true)) {
        LogInfo("D3D overlay: toast + center panel ready");
    }
}

HRESULT __stdcall PresentHook(IDXGISwapChain* swap, UINT sync_interval, UINT flags) {
    SwallowBlockedGamePad();
    if (swap && (flags & DXGI_PRESENT_TEST) == 0) {
        if (!g_logged_first.exchange(true)) {
            DXGI_SWAP_CHAIN_DESC desc{};
            if (SUCCEEDED(swap->GetDesc(&desc))) {
                LogInfo("D3D Present hooked (%ux%u)", desc.BufferDesc.Width, desc.BufferDesc.Height);
            }
        }
        DrawOverlayText(swap);
    }
    const int turbo = GetSpeedTurboLevel();
    UINT present_interval = sync_interval;
    if (turbo >= 2 && (flags & DXGI_PRESENT_TEST) == 0) {
        present_interval = 0;
    }
    HRESULT hr = E_FAIL;
    {
        std::lock_guard<std::mutex> lock(g_present_mu);
        RestoreBytes(g_present_site, g_present_original, g_present_patch_size);
        auto* original = reinterpret_cast<PresentFn>(g_present_site);
        hr = original(swap, present_interval, flags);
        WriteJump(g_present_site, reinterpret_cast<void*>(&PresentHook), nullptr, g_present_patch_size);
    }
    if (turbo >= 2 && (flags & DXGI_PRESENT_TEST) == 0) {
        PaceSpeedTurboFrame();
    }
    return hr;
}

bool ResolvePresentAddress(void** out_present, std::size_t* out_patch_size) {
    *out_present = nullptr;
    *out_patch_size = 0;
    HMODULE d3d = LoadLibraryW(L"d3d11.dll");
    if (!d3d) {
        return false;
    }
    auto* create = reinterpret_cast<D3D11CreateDeviceAndSwapChain_t>(
        GetProcAddress(d3d, "D3D11CreateDeviceAndSwapChain"));
    if (!create) {
        return false;
    }
    WNDCLASSW wc{};
    wc.lpfnWndProc = DefWindowProcW;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = L"GrandiaModD3dProbe";
    const ATOM atom = RegisterClassW(&wc);
    if (!atom && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) {
        LogWarn("D3D overlay: RegisterClass failed (%lu)", GetLastError());
        return false;
    }
    HWND hwnd = CreateWindowExW(0, wc.lpszClassName, L"", WS_OVERLAPPEDWINDOW, 0, 0, 64, 64, nullptr,
                                nullptr, wc.hInstance, nullptr);
    if (!hwnd) {
        LogWarn("D3D overlay: CreateWindow failed (%lu)", GetLastError());
        return false;
    }
    DXGI_SWAP_CHAIN_DESC sd{};
    sd.BufferCount = 1;
    sd.BufferDesc.Width = 64;
    sd.BufferDesc.Height = 64;
    sd.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    sd.OutputWindow = hwnd;
    sd.SampleDesc.Count = 1;
    sd.Windowed = TRUE;
    sd.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* context = nullptr;
    IDXGISwapChain* swap = nullptr;
    D3D_FEATURE_LEVEL level{};
    const HRESULT hr = create(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, nullptr, 0,
                              D3D11_SDK_VERSION, &sd, &swap, &device, &level, &context);
    if (FAILED(hr) || !swap) {
        LogWarn("D3D overlay: probe swap chain failed (hr=0x%08X)", static_cast<unsigned>(hr));
        if (context) {
            context->Release();
        }
        if (device) {
            device->Release();
        }
        DestroyWindow(hwnd);
        return false;
    }
    void** vtable = *reinterpret_cast<void***>(swap);
    void* present = vtable[8];
    const auto* bytes = reinterpret_cast<const std::uint8_t*>(present);
    const std::size_t patch_size = ChoosePresentPatchSize(bytes);
    if (patch_size < 5 || patch_size > 16) {
        LogWarn("D3D overlay: unsupported Present prologue");
        swap->Release();
        context->Release();
        device->Release();
        DestroyWindow(hwnd);
        return false;
    }
    *out_present = present;
    *out_patch_size = patch_size;
    swap->Release();
    context->Release();
    device->Release();
    DestroyWindow(hwnd);
    return true;
}

}  // namespace

void OverlayToast(const char* utf8, unsigned duration_ms, unsigned rgb) {
    ToastLine line;
    line.text = Utf8ToWide(utf8);
    if (line.text.empty()) {
        return;
    }
    line.expire_tick = duration_ms == 0 ? 0 : (GetTickCount() + duration_ms);
    line.rgb = rgb & 0xFFFFFFu;
    std::lock_guard<std::mutex> lock(g_toast_mu);
    PruneExpiredToastsLocked();
    if (g_toasts.size() >= kMaxToastLines) {
        g_toasts.erase(g_toasts.begin());
    }
    g_toasts.push_back(std::move(line));
    g_toast_dirty = true;
}

void OverlayClearToasts() {
    std::lock_guard<std::mutex> lock(g_toast_mu);
    g_toasts.clear();
    g_toast_dirty = true;
}

void OverlaySetPanel(const char* joined_utf8, const unsigned* rgbs, int count) {
    std::lock_guard<std::mutex> lock(g_panel_mu);
    g_panel_lines.clear();
    if (!joined_utf8 || count <= 0) {
        g_panel_active = false;
        g_panel_dirty = true;
        return;
    }
    std::string joined(joined_utf8);
    std::size_t start = 0;
    int idx = 0;
    while (idx < count && idx < static_cast<int>(kMaxPanelLines)) {
        const std::size_t nl = joined.find('\n', start);
        const std::string piece =
            nl == std::string::npos ? joined.substr(start) : joined.substr(start, nl - start);
        PanelLine line;
        line.text = Utf8ToWide(piece.c_str());
        line.rgb = rgbs ? (rgbs[idx] & 0xFFFFFFu) : 0xFFE528u;
        g_panel_lines.push_back(std::move(line));
        ++idx;
        if (nl == std::string::npos) {
            break;
        }
        start = nl + 1;
    }
    g_panel_active = !g_panel_lines.empty();
    g_panel_dirty = true;
}

void OverlayClearPanel() {
    OverlaySetPanel(nullptr, nullptr, 0);
}

bool OverlayPanelActive() {
    std::lock_guard<std::mutex> lock(g_panel_mu);
    return g_panel_active;
}

bool IsD3dHudInstalled() {
    return g_present_site != nullptr;
}

bool InstallD3dHud() {
    if (g_present_site) {
        return true;
    }
    void* present = nullptr;
    std::size_t patch_size = 0;
    if (!ResolvePresentAddress(&present, &patch_size)) {
        return false;
    }
    if (!WriteJump(present, reinterpret_cast<void*>(&PresentHook), g_present_original, patch_size)) {
        LogWarn("D3D overlay: Present patch failed");
        return false;
    }
    g_present_site = present;
    g_present_patch_size = patch_size;
    LogInfo("D3D overlay installed (Present@0x%08X, patch=%zu)",
            static_cast<unsigned>(reinterpret_cast<std::uintptr_t>(present)), patch_size);
    OverlayToast("GrandiaMod overlay ready", 4000, 0x7CFC00u);
    return true;
}

void RemoveD3dHud() {
    {
        std::lock_guard<std::mutex> lock(g_present_mu);
        if (g_present_site && g_present_patch_size) {
            RestoreBytes(g_present_site, g_present_original, g_present_patch_size);
            g_present_site = nullptr;
            g_present_patch_size = 0;
        }
    }
    OverlayClearToasts();
    OverlayClearPanel();
    ReleaseDeviceResources();
}

}  // namespace grandia_mod

extern "C" int ModOverlayReady() {
    return grandia_mod::IsD3dHudInstalled() ? 1 : 0;
}

extern "C" int ModOverlayToast(const char* message, int duration_ms, unsigned rgb) {
    grandia_mod::OverlayToast(message, duration_ms < 0 ? 0 : static_cast<unsigned>(duration_ms), rgb);
    return 1;
}

extern "C" int ModOverlayClearToasts() {
    grandia_mod::OverlayClearToasts();
    return 1;
}

extern "C" int ModOverlaySetPanel(const char* joined, const unsigned* rgbs, int count) {
    grandia_mod::OverlaySetPanel(joined, rgbs, count);
    return 1;
}

extern "C" int ModOverlayClearPanel() {
    grandia_mod::OverlayClearPanel();
    return 1;
}

extern "C" int ModOverlayPanelActive() {
    return grandia_mod::OverlayPanelActive() ? 1 : 0;
}
