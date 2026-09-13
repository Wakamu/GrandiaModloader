#include "menu.h"

#include "hook_util.h"
#include "log.h"

#include <Windows.h>

#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>

#if defined(_M_IX86)
extern "C" {
void* g_mod_hd_draw_tramp = nullptr;
void* g_mod_hd_draw_skip = nullptr;
void* g_mod_type5_tramp = nullptr;
void* g_mod_hd_text_tramp = nullptr;
void* g_mod_fb_text_tramp = nullptr;
void* g_mod_menu_cursor_tramp = nullptr;
void* g_mod_menu_cursor_skip = nullptr;
void* g_mod_menu_confirm_tramp = nullptr;
void* g_mod_menu_confirm_skip = nullptr;
void* g_mod_menu_cancel_tramp = nullptr;
void* g_mod_state0_resume = nullptr;
void* g_mod_window_resume = nullptr;
void* g_mod_present_resume = nullptr;
void* g_mod_clip_rest = nullptr;
void* g_mod_clip_success = nullptr;
void* g_mod_stack_cookie = nullptr;
void* g_mod_title_box_tramp = nullptr;
void ModHdDrawDetour();
void ModType5Detour();
void ModHdTextDetour();
void ModFbTextDetour();
void ModMenuAfterCursorDetour();
void ModMenuConfirmDetour();
void ModMenuCancelDetour();
void ModMenuState0CursorDetour();
void ModMenuWindowDetour();
void ModMenuPresentDetour();
void ModMenuClipDetour();
void ModTitleBoxDetour();
}
#endif

namespace grandia_mod {
namespace {

constexpr int kMaxItems = 12;
constexpr int kStockRows = 4;
constexpr int kVisibleRows = 8;
constexpr int kMaxOptions = 8;
constexpr int kMaxLabel = 20;
constexpr int kMaxMessage = 40;
constexpr int kMaxWindows = 4;
constexpr int kMaxLabels = 12;
constexpr int kMaxLine = 15;

constexpr std::uintptr_t kOpenMenuRva = 0x670C0u;
constexpr std::uintptr_t kCloseHelperRva = 0x66D60u;
constexpr std::uintptr_t kHdDrawRva = 0x63E90u;
constexpr std::uintptr_t kHdRowBoxRva = 0x63E20u;
constexpr std::uintptr_t kHdDrawSiteRva = 0x6305Eu;
constexpr std::uintptr_t kHdDrawSkipRva = 0x6324Cu;
constexpr std::uintptr_t kType5Rva = 0x62350u;
constexpr std::uintptr_t kAfterCursorSiteRva = 0x628C5u;
constexpr std::uintptr_t kAfterCursorSkipRva = 0x62B4Eu;
constexpr std::uintptr_t kCursorAnimRva = 0x67990u;
constexpr std::uintptr_t kPadHeldRva = 0x319440u;
constexpr std::uintptr_t kSettingsWordRva = 0x2C2E08u;
constexpr std::uintptr_t kAudioWordRva = 0x2C2E14u;
constexpr std::uintptr_t kOptXRva = 0x2C2E0Cu;
constexpr std::uintptr_t kOptWRva = 0x2C38A0u;
constexpr std::uintptr_t kCursorSlotRva = 0x31D140u;
constexpr std::uintptr_t kFbTextRva = 0x64450u;
constexpr std::size_t kCursorStride = 0x0Eu;
// 0x40 at +0x62BAA applies the option under the cursor (vanilla writes the
// save / can exit to title). 0x20 at +0x62B4E just closes.
constexpr std::uintptr_t kConfirmSiteRva = 0x62BAAu;
constexpr std::uintptr_t kConfirmSkipRva = 0x633A3u;
constexpr std::uintptr_t kCancelSiteRva = 0x62B4Eu;
// After the state-0 text paint: `mov byte ptr [6C301D], 0` then finger sprites.
constexpr std::uintptr_t kState0CursorRva = 0x63259u;
constexpr std::uintptr_t kState0ResumeRva = 0x63260u;
// Common tail: two window submits (`push 0x50` height) then finger present.
constexpr std::uintptr_t kWindowSiteRva = 0x633A3u;
constexpr std::uintptr_t kWindowResumeRva = 0x633D5u;
constexpr std::uintptr_t kWindowBoxRva = 0x57C00u;
constexpr std::uintptr_t kWindowBox2Rva = 0x579D0u;
constexpr std::uintptr_t kPresentCountRva = 0x674B8u;
constexpr std::uintptr_t kPresentResumeRva = 0x674BDu;
constexpr std::uintptr_t kSlashRva = 0x2044A0u;
constexpr std::uintptr_t kTextClipXRVA = 0x21871Cu;
constexpr std::uintptr_t kTextClipYRVA = 0x21871Eu;
constexpr std::uintptr_t kTextClipWRVA = 0x218720u;
constexpr std::uintptr_t kTextClipHRVA = 0x218722u;
constexpr std::uintptr_t kGlyphRecRva = 0x3191A4u;
constexpr std::uintptr_t kGlyphSubmitRva = 0xA110u;
constexpr std::uintptr_t kGlyphStartRva = 0x240C20u;
constexpr std::uintptr_t kGlyphBumpRva = 0x240C24u;
constexpr std::uintptr_t kTitleBoxRva = 0x642C0u;
constexpr std::uintptr_t kTextClipFnRva = 0x1E690u;
constexpr std::uintptr_t kTextClipRestRva = 0x1E696u;
constexpr std::uintptr_t kTextClipSuccessRva = 0x1E6FBu;
constexpr std::uintptr_t kStackCookieRva = 0x21000Cu;

// Preferred image base 0x400000. Use ModuleBase()+RVA for reads/writes.
constexpr std::uintptr_t kMenuTypeRva = 0x2C3018u;
constexpr std::uintptr_t kMenuStateRva = 0x2C301Bu;
constexpr std::uintptr_t kMenuCursorRva = 0x2C301Du;
constexpr std::uintptr_t kMenuToggleRva = 0x2C301Eu;
constexpr std::uintptr_t kMenuModeRva = 0x31942Cu;
constexpr std::uintptr_t kMenuWordRva = 0x319428u;
constexpr std::uintptr_t kPadLatchRva = 0x319464u;
constexpr std::uintptr_t kPadWordRva = 0x319444u;
constexpr std::uintptr_t kColorRva = 0x2412BAu;
constexpr std::uintptr_t kGateByteRva = 0x23FA5Au;
constexpr std::uintptr_t kBusyByteRva = 0x31CD38u;
constexpr std::uintptr_t kOptionsKeyRva = 0x204498u;
constexpr std::uintptr_t kOptionsTKeyRva = 0x204440u;
constexpr std::uintptr_t kCameraKeyRva = 0x20444Cu;
constexpr std::uintptr_t kVibrationKeyRva = 0x204468u;
constexpr std::uintptr_t kAudioKeyRva = 0x20447Cu;
constexpr std::uintptr_t kExitTitleKeyRva = 0x2083C4u;
constexpr std::uintptr_t kStandardKeyRva = 0x204454u;
constexpr std::uintptr_t kReverseKeyRva = 0x204460u;
constexpr std::uintptr_t kOnKeyRva = 0x204474u;
constexpr std::uintptr_t kOffKeyRva = 0x204478u;
constexpr std::uintptr_t kEnglishKeyRva = 0x204484u;
constexpr std::uintptr_t kJapaneseKeyRva = 0x20448Cu;
constexpr std::uintptr_t kYesKeyRva = 0x2083D4u;
constexpr std::uintptr_t kNoKeyRva = 0x2083D8u;

std::uintptr_t At(std::uintptr_t rva) {
    return ModuleBase() + rva;
}

void WriteU32(std::uint8_t* dest, std::uint32_t value) {
    std::memcpy(dest, &value, 4);
}

void ExpectedHdDraw(std::uint8_t out[5], std::uintptr_t base) {
    out[0] = 0xB9;
    WriteU32(out + 1, static_cast<std::uint32_t>(base + kOptionsKeyRva));
}

void ExpectedAfterCursor(std::uint8_t out[10], std::uintptr_t base) {
    out[0] = 0xF7;
    out[1] = 0x05;
    WriteU32(out + 2, static_cast<std::uint32_t>(base + kPadWordRva));
    out[6] = 0x00;
    out[7] = 0x80;
    out[8] = 0x00;
    out[9] = 0x00;
}

void ExpectedConfirm(std::uint8_t out[7], std::uintptr_t base) {
    out[0] = 0xF6;
    out[1] = 0x05;
    WriteU32(out + 2, static_cast<std::uint32_t>(base + kPadWordRva));
    out[6] = 0x40;
}

void ExpectedCancel(std::uint8_t out[7], std::uintptr_t base) {
    out[0] = 0xF6;
    out[1] = 0x05;
    WriteU32(out + 2, static_cast<std::uint32_t>(base + kPadWordRva));
    out[6] = 0x20;
}

void ExpectedState0Cursor(std::uint8_t out[7], std::uintptr_t base) {
    out[0] = 0xC6;
    out[1] = 0x05;
    WriteU32(out + 2, static_cast<std::uint32_t>(base + kMenuCursorRva));
    out[6] = 0x00;
}

void LogSiteMismatch(const char* label, std::uintptr_t rva, const std::uint8_t* site, std::size_t n) {
    char hex[48]{};
    std::size_t pos = 0;
    for (std::size_t i = 0; i < n && i < 16 && pos + 3 <= sizeof(hex); ++i) {
        pos += static_cast<std::size_t>(
            sprintf_s(hex + pos, sizeof(hex) - pos, "%02X", site[i]));
    }
    LogWarn("Game.Menu %s +0x%X site mismatch got %s", label, static_cast<unsigned>(rva), hex);
}

constexpr std::size_t kHdDrawPatch = 5;
constexpr std::size_t kType5Patch = 6;
constexpr std::size_t kAfterCursorPatch = 10;
constexpr std::size_t kConfirmPatch = 7;
constexpr std::size_t kCancelPatch = 7;
constexpr std::size_t kState0CursorPatch = 7;
constexpr std::size_t kWindowPatch = 5;
constexpr std::size_t kPresentPatch = 5;
constexpr std::uint8_t kWindowBytes[] = {0xBA, 0x28, 0x00, 0x00, 0x00};
constexpr std::uint8_t kPresentBytes[] = {0xB9, 0x04, 0x00, 0x00, 0x00};
constexpr std::size_t kTextClipPatch = 6;
constexpr std::uint8_t kTextClipBytes[] = {0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x2C};
constexpr std::uint8_t kType5Bytes[] = {0x55, 0x8B, 0xEC, 0x83, 0xEC, 0x24};
constexpr std::size_t kHdTextPatch = 6;
constexpr std::uint8_t kHdTextBytes[] = {0x55, 0x8B, 0xEC, 0x83, 0xE4, 0xF0};

struct Session {
    bool pending = false;
    bool live = false;
    char title[kMaxLabel + 1]{};
    char items[kMaxItems][kMaxLabel + 1]{};
    char options[kMaxItems][kMaxOptions][kMaxLabel + 1]{};
    int opt_count[kMaxItems]{};
    int selected[kMaxItems]{};
    int count = 0;
    int choice = -1;
    int choice_side = 0;
    int cancel = 0;
};

std::mutex g_mu;
Session g_session;
std::atomic<bool> g_live{false};
int g_open_fail_log = 0;
int g_lookup_log = 0;
std::uint8_t g_prev_cursor = 0;
std::uint32_t g_prev_held = 0;
int g_scroll = 0;
bool g_need_redraw = false;
bool g_restore_after_draw = false;
std::uint8_t g_keep_cursor = 0;
std::uint8_t g_saved_toggles[kMaxItems]{};
std::uint32_t g_saved_settings = 0;
std::uint32_t g_saved_audio = 0;
bool g_have_saved_toggles = false;
char g_draw_left[kMaxItems][kMaxLabel + 1]{};
char g_draw_right[kMaxItems][kMaxLabel + 1]{};
std::uint8_t g_opt_x[kMaxItems][2]{};
std::uint8_t g_opt_w[kMaxItems][2]{};
bool g_kit = false;
bool g_layout = false;
int g_item_line[kMaxItems]{};
int g_window_n = 0;
int g_label_n = 0;
int g_layout_max_line = 0;

constexpr int kMaxNodes = 64;
constexpr std::uint8_t kNCol = 1;
constexpr std::uint8_t kNRow = 2;
constexpr std::uint8_t kNBox = 3;
constexpr std::uint8_t kNLab = 4;
constexpr std::uint8_t kNItem = 5;

struct LNode {
    uint8_t kind = 0;
    uint8_t style = 5;
    std::int16_t child0 = -1;
    std::int16_t sib = -1;
    int group = -1;
    int line = 0;
    int width = 0;
    char text[kMaxMessage + 1]{};
};

struct LGroup {
    int n = 0;
    int sel = 0;
    int line = 0;
    int item_node[kMaxOptions]{};
    int width[kMaxOptions]{};
    bool block = false;
};

int g_node_n = 0;
LNode g_nodes[kMaxNodes]{};
int g_root_n = 0;
int g_roots[16]{};
int g_group_n = 0;
LGroup g_groups[kMaxItems]{};

struct MenuWindow {
    int y = 0x28;
    int w = 0x12C;
    int h = 0x50;
    int style = 5;
};

struct MenuLabel {
    int line = 0;
    char text[kMaxMessage + 1]{};
};

MenuWindow g_windows[kMaxWindows]{};
MenuLabel g_labels[kMaxLabels]{};

void ResetWidgets() {
    g_kit = false;
    g_layout = false;
    g_window_n = 0;
    g_label_n = 0;
    g_layout_max_line = 0;
    g_node_n = 0;
    g_root_n = 0;
    g_group_n = 0;
    std::memset(g_nodes, 0, sizeof(g_nodes));
    std::memset(g_groups, 0, sizeof(g_groups));
    for (int i = 0; i < kMaxNodes; ++i) {
        g_nodes[i].child0 = -1;
        g_nodes[i].sib = -1;
        g_nodes[i].group = -1;
    }
    for (int i = 0; i < kMaxItems; ++i) {
        g_item_line[i] = i + 1;
    }
}

void AddDefaultWindow() {
    if (g_window_n > 0) {
        return;
    }
    g_windows[0] = MenuWindow{};
    g_window_n = 1;
}

void SetLive(bool on) {
    g_session.live = on;
    g_live.store(on, std::memory_order_release);
    if (!on) {
        g_prev_cursor = 0;
        g_prev_held = 0;
        g_scroll = 0;
        g_need_redraw = false;
        g_restore_after_draw = false;
        g_keep_cursor = 0;
    }
}

void CopyLabel(char* dest, const char* src);
void CopyBounded(char* dest, const char* src, int max);

int ClampLine(int line) {
    if (line < 0) {
        return 0;
    }
    return line > kMaxLine ? kMaxLine : line;
}

int ItemAtLine(int line) {
    for (int i = 0; i < g_session.count; ++i) {
        if (g_item_line[i] == line) {
            return i;
        }
    }
    return -1;
}

bool RowIsSlot(int row) {
    return row >= 0 && row < g_session.count && g_session.opt_count[row] <= 1;
}

int ItemLine(int row) {
    if (row < 0 || row >= kMaxItems) {
        return row + 1;
    }
    return g_item_line[row];
}

int MaxUsedLine() {
    if (g_layout && g_layout_max_line > 0) {
        return g_layout_max_line;
    }
    int m = 0;
    for (int i = 0; i < g_session.count; ++i) {
        if (g_item_line[i] > m) {
            m = g_item_line[i];
        }
    }
    for (int i = 0; i < g_label_n; ++i) {
        if (g_labels[i].line > m) {
            m = g_labels[i].line;
        }
    }
    return m;
}
int CStringLen(const char* s);
const char* SlashText();
void ExpandTextClip(int vis);
void SubmitTextFrame(int y0, int y1);
void OpenHdRowBox(int row);
void DrawHdRow(int row, const char* text, std::uint8_t color);

bool KeyIs(const char* key, std::uintptr_t rva, const char* name) {
    if (!key) {
        return false;
    }
    if (key == reinterpret_cast<const char*>(At(rva))) {
        return true;
    }
    return name && std::strcmp(key, name) == 0;
}

void WriteDrawSlot(char* dest, int row, int slot) {
    dest[0] = 0;
    if (row < 0 || row >= g_session.count) {
        dest[0] = ' ';
        dest[1] = 0;
        return;
    }
    int n = g_session.opt_count[row];
    if (n <= 0) {
        dest[0] = ' ';
        dest[1] = 0;
        return;
    }
    if (n == 1) {
        if (slot == 0) {
            CopyLabel(dest, g_session.options[row][0]);
        } else {
            dest[0] = ' ';
            dest[1] = 0;
        }
        return;
    }
    if (n == 2) {
        CopyLabel(dest, g_session.options[row][slot == 0 ? 0 : 1]);
        return;
    }
    // n >= 3: two stock columns (X and Y). Extra options are blitted
    // separately so their '/' stays white, not the yellow option color.
    if (slot == 0) {
        CopyLabel(dest, g_session.options[row][0]);
        return;
    }
    CopyLabel(dest, g_session.options[row][1]);
}

void RefreshDrawSlots() {
    for (int i = 0; i < kMaxItems; ++i) {
        WriteDrawSlot(g_draw_left[i], i, 0);
        WriteDrawSlot(g_draw_right[i], i, 1);
    }
}

const char* OptionText(int row, int slot) {
    RefreshDrawSlots();
    if (row < 0 || row >= kMaxItems) {
        return " ";
    }
    return slot == 0 ? g_draw_left[row] : g_draw_right[row];
}

const char* LookupCustom(const char* key) {
    if (!g_live.load(std::memory_order_acquire) || g_session.count <= 0) {
        return nullptr;
    }
    const char* blank = " ";
    if (KeyIs(key, kOptionsKeyRva, "options") || KeyIs(key, kOptionsTKeyRva, "options_t")) {
        return g_session.title[0] ? g_session.title : blank;
    }
    if (g_layout) {
        return blank;
    }
    auto vis = [&](int slot) -> int { return g_scroll + slot; };
    auto row_at = [&](int slot) -> int {
        return g_kit ? ItemAtLine(slot + 1) : vis(slot);
    };
    auto label_at = [&](int slot) -> const char* {
        const int row = row_at(slot);
        if (row < 0 || row >= g_session.count || !g_session.items[row][0]) {
            return blank;
        }
        return g_session.items[row];
    };
    if (KeyIs(key, kCameraKeyRva, "camera")) {
        return label_at(0);
    }
    if (KeyIs(key, kVibrationKeyRva, "vibration")) {
        return label_at(1);
    }
    if (KeyIs(key, kAudioKeyRva, "audio")) {
        return label_at(2);
    }
    if (KeyIs(key, kExitTitleKeyRva, "exit_to_title")) {
        return label_at(3);
    }
    if (KeyIs(key, kStandardKeyRva, "standard") || KeyIs(key, kOnKeyRva, "on") ||
        KeyIs(key, kEnglishKeyRva, "english") || KeyIs(key, kYesKeyRva, "yes")) {
        const int slot = KeyIs(key, kStandardKeyRva, "standard")   ? 0
                         : KeyIs(key, kOnKeyRva, "on")             ? 1
                         : KeyIs(key, kEnglishKeyRva, "english")   ? 2
                                                                    : 3;
        return OptionText(row_at(slot), 0);
    }
    if (KeyIs(key, kReverseKeyRva, "reverse") || KeyIs(key, kOffKeyRva, "off") ||
        KeyIs(key, kJapaneseKeyRva, "japanese") || KeyIs(key, kNoKeyRva, "no")) {
        const int slot = KeyIs(key, kReverseKeyRva, "reverse")       ? 0
                         : KeyIs(key, kOffKeyRva, "off")             ? 1
                         : KeyIs(key, kJapaneseKeyRva, "japanese")   ? 2
                                                                     : 3;
        return OptionText(row_at(slot), 1);
    }
    return nullptr;
}

void* g_hd_draw_site = nullptr;
void* g_hd_draw_tramp_mem = nullptr;
std::uint8_t g_hd_draw_original[16]{};

void* g_type5_site = nullptr;
void* g_type5_tramp_mem = nullptr;
std::uint8_t g_type5_original[16]{};

void* g_hd_text_site = nullptr;
void* g_hd_text_tramp_mem = nullptr;
std::uint8_t g_hd_text_original[16]{};

void* g_fb_text_site = nullptr;
void* g_fb_text_tramp_mem = nullptr;
std::uint8_t g_fb_text_original[16]{};

void* g_title_box_site = nullptr;
void* g_title_box_tramp_mem = nullptr;
std::uint8_t g_title_box_original[16]{};

void* g_cursor_site = nullptr;
void* g_cursor_tramp_mem = nullptr;
std::uint8_t g_cursor_original[16]{};

void* g_confirm_site = nullptr;
void* g_confirm_tramp_mem = nullptr;
std::uint8_t g_confirm_original[16]{};

void* g_cancel_site = nullptr;
void* g_cancel_tramp_mem = nullptr;
std::uint8_t g_cancel_original[16]{};

void* g_state0_site = nullptr;
std::uint8_t g_state0_original[16]{};

void* g_window_site = nullptr;
std::uint8_t g_window_original[16]{};

void* g_present_site = nullptr;
std::uint8_t g_present_original[16]{};

void* g_clip_site = nullptr;
std::uint8_t g_clip_original[16]{};

void CopyBounded(char* dest, const char* src, int max) {
    if (!dest) {
        return;
    }
    dest[0] = 0;
    if (!src || max <= 0) {
        return;
    }
    std::size_t n = 0;
    const auto cap = static_cast<std::size_t>(max);
    while (src[n] && n < cap) {
        const char c = src[n];
        dest[n] = (c == '\n' || c == '\r' || c == '\t') ? ' ' : c;
        ++n;
    }
    dest[n] = 0;
}

void CopyLabel(char* dest, const char* src) {
    CopyBounded(dest, src, kMaxLabel);
}

void DefaultYesNo(int row) {
    CopyLabel(g_session.options[row][0], "Yes");
    CopyLabel(g_session.options[row][1], "No");
    g_session.opt_count[row] = 2;
    g_session.selected[row] = 0;
}

void SplitItems(const char* joined, int count) {
    g_session.count = 0;
    std::memset(g_session.items, 0, sizeof(g_session.items));
    std::memset(g_session.options, 0, sizeof(g_session.options));
    std::memset(g_session.opt_count, 0, sizeof(g_session.opt_count));
    std::memset(g_session.selected, 0, sizeof(g_session.selected));
    if (!joined || count <= 0) {
        return;
    }
    const char* p = joined;
    for (int i = 0; i < kMaxItems && i < count; ++i) {
        const char* start = p;
        while (*p && *p != '\n' && *p != '\t') {
            ++p;
        }
        const auto len = static_cast<std::size_t>(p - start);
        const auto take = len < static_cast<std::size_t>(kMaxLabel) ? len : static_cast<std::size_t>(kMaxLabel);
        std::memcpy(g_session.items[i], start, take);
        g_session.items[i][take] = 0;
        g_session.opt_count[i] = 0;
        if (*p == '\t') {
            ++p;
            while (*p && *p != '\n' && g_session.opt_count[i] < kMaxOptions) {
                const char* opt = p;
                while (*p && *p != '\n' && *p != '\t') {
                    ++p;
                }
                const auto olen = static_cast<std::size_t>(p - opt);
                const auto otake =
                    olen < static_cast<std::size_t>(kMaxLabel) ? olen : static_cast<std::size_t>(kMaxLabel);
                const int oi = g_session.opt_count[i];
                std::memcpy(g_session.options[i][oi], opt, otake);
                g_session.options[i][oi][otake] = 0;
                ++g_session.opt_count[i];
                if (*p == '\t') {
                    ++p;
                }
            }
        }
        if (g_session.opt_count[i] <= 0) {
            DefaultYesNo(i);
        }
        ++g_session.count;
        if (*p == '\n') {
            ++p;
        }
    }
}

void WriteU16(std::uintptr_t address, std::uint16_t value) {
    SafeWriteByte(address, static_cast<std::uint8_t>(value & 0xFFu));
    SafeWriteByte(address + 1, static_cast<std::uint8_t>((value >> 8) & 0xFFu));
}

int CStringLen(const char* s) {
    int n = 0;
    if (!s) {
        return 0;
    }
    while (s[n]) {
        ++n;
    }
    return n;
}

const char* SlashText() {
    const char* s = reinterpret_cast<const char*>(At(kSlashRva));
    if (s && s[0]) {
        return s;
    }
    return "     /     ";
}

void SnapshotToggles() {
    for (int i = 0; i < kMaxItems; ++i) {
        std::uint8_t v = 0;
        SafeReadByte(At(kMenuToggleRva) + static_cast<std::uintptr_t>(i), &v);
        g_saved_toggles[i] = v;
    }
    g_saved_settings = 0;
    g_saved_audio = 0;
    SafeReadU32(At(kSettingsWordRva), &g_saved_settings);
    SafeReadU32(At(kAudioWordRva), &g_saved_audio);
    g_have_saved_toggles = true;
}

void RestoreSettings() {
    if (!g_have_saved_toggles) {
        return;
    }
    SafeWriteU32(At(kSettingsWordRva), g_saved_settings);
    SafeWriteU32(At(kAudioWordRva), g_saved_audio);
}

void RestoreToggles() {
    if (!g_have_saved_toggles) {
        return;
    }
    RestoreSettings();
    for (int i = 0; i < kMaxItems; ++i) {
        SafeWriteByte(At(kMenuToggleRva) + static_cast<std::uintptr_t>(i), g_saved_toggles[i]);
    }
}

void PlaceCursorSprite(int slot, int opt) {
    if (slot < 0 || slot >= kVisibleRows) {
        return;
    }
    const int row = g_scroll + slot;
    if (row < 0 || row >= g_session.count) {
        return;
    }
    const int n = g_session.opt_count[row];
    if (n <= 0) {
        return;
    }
    if (opt < 0) {
        opt = 0;
    }
    if (opt >= n) {
        opt = n - 1;
    }
    const auto dest = At(kCursorSlotRva) + static_cast<std::uintptr_t>(slot) * kCursorStride;
    const auto finger_y = static_cast<std::uint16_t>((ItemLine(row) + 2) * 16);
    if (RowIsSlot(row)) {
        WriteU16(dest, 1);
        WriteU16(dest + 2, 0x48);
        WriteU16(dest + 4, finger_y);
        WriteU16(dest + 6, 0x80);
        WriteU16(dest + 8, 0x10);
        SafeWriteByte(dest + 0xA, 0);
        WriteU16(dest + 0xB, 0);
        return;
    }
    std::uint8_t x0 = 0;
    std::uint8_t x1 = 0;
    std::uint8_t w0 = 0x10;
    if (slot < kStockRows) {
        SafeReadByte(At(kOptXRva) + static_cast<std::uintptr_t>(slot * 2), &x0);
        SafeReadByte(At(kOptXRva) + static_cast<std::uintptr_t>(slot * 2 + 1), &x1);
        SafeReadByte(At(kOptWRva) + static_cast<std::uintptr_t>(slot * 2), &w0);
    } else {
        x0 = g_opt_x[slot][0];
        x1 = g_opt_x[slot][1];
        w0 = g_opt_w[slot][0];
    }
    std::uint16_t x = x0;
    std::uint16_t w = w0 ? w0 : 0x10;
    if (opt > 0) {
        x = x1;
        int chars = 0;
        for (int i = 1; i < opt; ++i) {
            chars += CStringLen(g_session.options[row][i]) + 1;
        }
        x = static_cast<std::uint16_t>(x1 + chars * 8);
        const int sl = CStringLen(g_session.options[row][opt]);
        w = static_cast<std::uint16_t>(sl > 0 ? sl * 8 : 0x10);
    }
    WriteU16(dest, 1);
    WriteU16(dest + 2, x);
    WriteU16(dest + 4, finger_y);
    WriteU16(dest + 6, w);
    WriteU16(dest + 8, 0x10);
    SafeWriteByte(dest + 0xA, 0);
    WriteU16(dest + 0xB, 0);
    if (slot < kStockRows) {
        SafeWriteByte(At(kMenuToggleRva) + static_cast<std::uintptr_t>(slot),
                      static_cast<std::uint8_t>(opt == 0 ? 0 : 1));
    }
}

void PlayCursorAnim() {
    const auto base = ModuleBase();
    if (base == 0) {
        return;
    }
    auto* fn = reinterpret_cast<void*>(base + kCursorAnimRva);
    if (!IsExecutableAddress(fn)) {
        return;
    }
#if defined(_M_IX86)
    __asm {
        push 3
        push 0
        mov ecx, 4
        mov eax, fn
        call eax
        add esp, 8
    }
#endif
}

int VisibleRow(int slot) {
    if (g_kit) {
        return slot;
    }
    return g_scroll + slot;
}

int VisibleCount() {
    if (g_kit) {
        return g_session.count < kVisibleRows ? g_session.count : kVisibleRows;
    }
    const int left = g_session.count - g_scroll;
    if (left < 0) {
        return 0;
    }
    return left < kVisibleRows ? left : kVisibleRows;
}

void SyncRowCursorFromNative() {
    const int vis = VisibleCount();
    for (int slot = 0; slot < vis; ++slot) {
        const int row = VisibleRow(slot);
        if (slot >= kStockRows || g_session.opt_count[row] > 2) {
            PlaceCursorSprite(slot, g_session.selected[row]);
            continue;
        }
        std::uint8_t bit = 0;
        SafeReadByte(At(kMenuToggleRva) + static_cast<std::uintptr_t>(slot), &bit);
        g_session.selected[row] = bit ? 1 : 0;
    }
}

void ApplyTogglesFromSelected() {
    for (int slot = 0; slot < kStockRows; ++slot) {
        const int row = VisibleRow(slot);
        if (row < 0 || row >= g_session.count) {
            SafeWriteByte(At(kMenuToggleRva) + static_cast<std::uintptr_t>(slot), 0);
            continue;
        }
        const int sel = g_session.selected[row];
        SafeWriteByte(At(kMenuToggleRva) + static_cast<std::uintptr_t>(slot),
                      static_cast<std::uint8_t>(sel == 0 ? 0 : 1));
    }
}

void RequestRedraw(std::uint8_t cursor) {
    g_keep_cursor = cursor;
    g_need_redraw = true;
    RefreshDrawSlots();

}

void CloseNativeUnlocked() {
    RestoreToggles();
    const auto base = ModuleBase();
    if (base == 0) {
        SetLive(false);
        g_session.pending = false;
        return;
    }
#if defined(_M_IX86)
    auto* close = reinterpret_cast<void*>(base + kCloseHelperRva);
    if (IsExecutableAddress(close)) {
        __asm {
            mov eax, close
            call eax
        }
    }
#endif
    SafeWriteU32(At(kMenuModeRva), 1);
    for (int i = 0; i < 16; ++i) {
        SafeWriteByte(At(kMenuTypeRva) + static_cast<std::uintptr_t>(i), 0);
    }
    SafeWriteByte(At(kMenuStateRva), 0);
    RestoreToggles();
    g_have_saved_toggles = false;
    SetLive(false);
    g_session.pending = false;
}

bool GatesOpen() {
    std::uint32_t mode = 0;
    std::uint32_t word = 0;
    std::uint32_t latch = 0;
    std::uint8_t gate = 1;
    std::uint8_t busy = 1;
    if (!SafeReadU32(At(kMenuModeRva), &mode) || mode != 1) {
        return false;
    }
    if (!SafeReadU32(At(kMenuWordRva), &word) || (word & 0xFFFFu) != 0) {
        return false;
    }
    if (!SafeReadU32(At(kPadLatchRva), &latch) || (latch & 0x8000u) != 0) {
        return false;
    }
    if (!SafeReadByte(At(kGateByteRva), &gate) || gate != 0) {
        return false;
    }
    if (!SafeReadByte(At(kBusyByteRva), &busy) || busy != 0) {
        return false;
    }
    return true;
}

void TryOpenNative() {
    const auto base = ModuleBase();
    if (base == 0) {
        return;
    }
    auto* open = reinterpret_cast<void*>(base + kOpenMenuRva);
    if (!IsExecutableAddress(open)) {
        return;
    }
#if defined(_M_IX86)
    __asm {
        push 0
        xor dl, dl
        mov cl, 5
        mov eax, open
        call eax
        add esp, 4
    }
#endif
}

void RewindGlyphs() {
    std::uint32_t start = 0;
    if (SafeReadU32(At(kGlyphStartRva), &start) && start != 0) {
        SafeWriteU32(At(kGlyphBumpRva), start);
    }
}

void DrawMenuCell(int row, const char* text, int width, std::uint8_t color) {
    const auto base = ModuleBase();
    if (base == 0 || !text) {
        return;
    }
    ExpandTextClip(row + 1);
    SafeWriteByte(At(kColorRva), color);
#if defined(_M_IX86)
    // Prefer the original 464450 trampoline so we do not re-enter our hook.
    auto* fn = g_mod_fb_text_tramp;
    if (!fn) {
        fn = reinterpret_cast<void*>(base + kFbTextRva);
    }
    if (!IsExecutableAddress(fn)) {
        return;
    }
    __asm {
        push width
        mov ecx, row
        mov edx, text
        mov eax, fn
        call eax
        add esp, 4
    }
#endif
}

void DrawTrailingOptions(int slot, int row) {
    (void)slot;
    if (RowIsSlot(row)) {
        return;
    }
    const int n = g_session.opt_count[row];
    if (n <= 2) {
        return;
    }
    const int ecx = ItemLine(row);
    int width = 0x15 + CStringLen(g_session.options[row][1]);
    static const char kSlash[] = "/";
    for (int i = 2; i < n; ++i) {
        DrawMenuCell(ecx, kSlash, width, 0xC0);
        ++width;
        DrawMenuCell(ecx, g_session.options[row][i], width, 0xC0);
        width += CStringLen(g_session.options[row][i]);
    }
}

void LayoutExtraSlot(int slot, int row) {
    const int n = g_session.opt_count[row];
    const int left_len = n > 0 ? CStringLen(g_session.options[row][0]) : 0;
    g_opt_w[slot][0] = static_cast<std::uint8_t>(left_len > 0 ? left_len * 8 : 0x10);
    g_opt_x[slot][0] = static_cast<std::uint8_t>(0xD8 - g_opt_w[slot][0]);
    g_opt_x[slot][1] = 0xE0;
    g_opt_w[slot][1] = 0x10;
    if (n >= 2) {
        const int sl = CStringLen(g_session.options[row][n == 2 ? 1 : 1]);
        g_opt_w[slot][1] = static_cast<std::uint8_t>(sl > 0 ? sl * 8 : 0x10);
    }
}

void DrawExtraSlot(int slot) {
    const int row = VisibleRow(slot);
    if (row < 0 || row >= g_session.count) {
        return;
    }
    const int ecx = ItemLine(row);
    const char* label = g_session.items[row][0] ? g_session.items[row] : " ";
    DrawMenuCell(ecx, label, 0, 0xF0);
    if (!RowIsSlot(row)) {
        DrawMenuCell(ecx, SlashText(), 0x0F, 0xF0);
        DrawMenuCell(ecx, OptionText(row, 0), 0x14, 0xC0);
        DrawMenuCell(ecx, OptionText(row, 1), 0x15, 0xC0);
        DrawTrailingOptions(slot, row);
    }
    LayoutExtraSlot(slot, row);
    PlaceCursorSprite(slot, g_session.selected[row]);
}

void DisableSpriteSlot(int slot) {
    if (slot < kStockRows || slot >= kVisibleRows) {
        return;
    }
    const auto dest = At(kCursorSlotRva) + static_cast<std::uintptr_t>(slot) * kCursorStride;
    WriteU16(dest, 0);
}

void ExpandTextClip(int vis) {
    if (!g_layout && MaxUsedLine() <= kStockRows && vis <= kStockRows) {
        return;
    }
    // 41E5A0 shrinks this rect around each glyph. A leftover 0x70 title
    // box then hides ecx>=5 on present. Keep the scissor open.
    WriteU16(At(kTextClipXRVA), 0);
    WriteU16(At(kTextClipYRVA), 0);
    WriteU16(At(kTextClipWRVA), 0x7FFF);
    WriteU16(At(kTextClipHRVA), 0x7FFF);
}

void SubmitTextFrame(int y0, int y1) {
    const auto base = ModuleBase();
    if (base == 0 || y1 <= y0) {
        return;
    }
#if defined(_M_IX86)
    auto* submit = reinterpret_cast<void*>(base + kGlyphSubmitRva);
    if (!IsExecutableAddress(submit)) {
        return;
    }
    std::uint32_t rec = 0;
    if (!SafeReadU32(At(kGlyphRecRva), &rec) || rec == 0) {
        return;
    }
    auto* recp = reinterpret_cast<void*>(rec);
    const int x = 0x3C0;
    const int flag_start = 0x100040;
    const int flag_end = 0x10000C;
    __asm {
        mov ecx, recp
        mov eax, x
        mov edx, y0
        mov word ptr [ecx], ax
        mov word ptr [ecx + 2], dx
        mov eax, flag_start
        mov dword ptr [ecx + 4], eax
        xor edx, edx
        push 2
        mov eax, submit
        call eax
        add esp, 4
        mov ecx, recp
        mov eax, x
        mov edx, y1
        mov word ptr [ecx], ax
        mov word ptr [ecx + 2], dx
        mov eax, flag_end
        mov dword ptr [ecx + 4], eax
        xor edx, edx
        push 2
        mov eax, submit
        call eax
        add esp, 4
    }
#else
    (void)y0;
    (void)y1;
#endif
}

void DrawKitLabels() {
    for (int i = 0; i < g_label_n; ++i) {
        if (g_labels[i].text[0]) {
            DrawMenuCell(g_labels[i].line, g_labels[i].text, 0, 0xF0);
        }
    }
}

void DrawExtraVisibleRows() {
    const int vis = VisibleCount();
    ExpandTextClip(vis);
    RefreshDrawSlots();
    for (int slot = 0; slot < vis; ++slot) {
        const int row = VisibleRow(slot);
        if (ItemLine(row) > kStockRows) {
            DrawExtraSlot(slot);
        } else {
            DrawTrailingOptions(slot, row);
        }
    }
    for (int slot = (vis < kStockRows ? kStockRows : vis); slot < kVisibleRows; ++slot) {
        DisableSpriteSlot(slot);
    }
}

void OpenHdRowBox(int row) {
    const auto base = ModuleBase();
    if (base == 0 || row < 0 || row >= 8) {
        return;
    }
#if defined(_M_IX86)
    auto* fn = reinterpret_cast<void*>(base + kHdRowBoxRva);
    if (!IsExecutableAddress(fn)) {
        return;
    }
    __asm {
        mov ecx, row
        mov eax, fn
        call eax
    }
#endif
}

void DrawHdRow(int row, const char* text, std::uint8_t color) {
    const auto base = ModuleBase();
    if (base == 0 || !text) {
        return;
    }
    auto* fn = g_mod_hd_text_tramp;
    if (!fn) {
        fn = reinterpret_cast<void*>(base + kHdDrawRva);
    }
    if (!IsExecutableAddress(fn)) {
        return;
    }
    SafeWriteByte(At(kColorRva), color);
#if defined(_M_IX86)
    __asm {
        push 1
        push 0x0F
        mov ecx, row
        mov edx, text
        mov eax, fn
        call eax
        add esp, 8
    }
#endif
}

}  // namespace

void DrawLayoutTree();
void PlaceAllLayoutFingers();
void ParseLayout(const char* body);

void MenuOnTick() {
    std::unique_lock<std::mutex> lock(g_mu);
    if (g_session.live) {
        std::uint8_t type = 0;
        SafeReadByte(At(kMenuTypeRva), &type);
        if (type != 5) {
            if (g_session.choice < 0 && g_session.cancel == 0) {
                g_session.cancel = 1;
            }
            SetLive(false);
        } else {
            RefreshDrawSlots();
        }
    }
    if (!g_session.pending || g_session.live) {
        return;
    }
    if (!GatesOpen()) {
        if ((g_open_fail_log++ % 120) == 0) {

        }
        return;
    }
    // live before 670C0 so init draw sees us. Unlock so type-5 hooks cannot
    // deadlock on g_mu if the opener runs the handler immediately.
    SetLive(true);
    g_session.pending = false;
    lock.unlock();
    TryOpenNative();
    lock.lock();
    std::uint8_t type = 0;
    SafeReadByte(At(kMenuTypeRva), &type);
    if (type != 5) {
        SetLive(false);
        g_session.pending = true;
        if ((g_open_fail_log++ % 120) == 0) {
            LogWarn("Game.Menu: +0x670C0 did not take type 5 (got %u)", type);
        }
        return;
    }
    SnapshotToggles();
    RefreshDrawSlots();
    g_prev_cursor = 0;
    g_prev_held = 0;
    g_open_fail_log = 0;

}

extern "C" int ModMenuShouldDrawCustom() {
    return g_live.load(std::memory_order_acquire) && g_session.count > 0 ? 1 : 0;
}

extern "C" void ModMenuDrawCustom() {
    if (!g_live.load(std::memory_order_acquire)) {
        return;
    }
    if (g_layout) {
        // 463E90 would submit another 0x70 title box and clip extra rows.
        return;
    }
    static bool logged = false;
    if (!logged) {
        logged = true;

    }
    DrawHdRow(0, g_session.title, 0xF0);
    std::uint8_t cursor = 0;
    SafeReadByte(At(kMenuCursorRva), &cursor);
    for (int i = 0; i < kVisibleRows; ++i) {
        const int row = VisibleRow(i);
        const char* text = " ";
        if (row >= 0 && row < g_session.count && g_session.items[row][0]) {
            text = g_session.items[row];
        }
        const std::uint8_t color = cursor == static_cast<std::uint8_t>(i) ? 0xF0 : 0xC0;
        DrawHdRow(i + 1, text, color);
    }
}

extern "C" void ModMenuOnType5() {
    if (!g_live.load(std::memory_order_acquire)) {
        return;
    }
    RestoreSettings();
    if (g_layout || MaxUsedLine() > kStockRows) {
        ExpandTextClip(MaxUsedLine());
    }
    if (g_need_redraw) {
        SafeWriteByte(At(kMenuStateRva), 0);
        SafeWriteByte(At(kMenuCursorRva), g_keep_cursor);
        g_restore_after_draw = true;
        g_need_redraw = false;
    }
    std::uint8_t cursor = 0;
    SafeReadByte(At(kMenuCursorRva), &cursor);
    const int vis = VisibleCount();
    const int limit = vis < 1 ? 1 : vis;
    if (cursor >= static_cast<std::uint8_t>(limit)) {
        cursor = g_prev_cursor == 0 ? static_cast<std::uint8_t>(limit - 1) : 0;
        SafeWriteByte(At(kMenuCursorRva), cursor);
    }
    g_prev_cursor = cursor;
    // State 0 reloads 6C301E from the save. Do not copy those bits into
    // selected[] for the newly visible rows. A layout tree owns its own
    // groups; stock toggles are leftover Options state.
    if (!g_restore_after_draw && !g_layout) {
        SyncRowCursorFromNative();
    }
    RefreshDrawSlots();
}

extern "C" void ModMenuAfterState0Draw() {
    if (!g_live.load(std::memory_order_acquire)) {
        SafeWriteByte(At(kMenuCursorRva), 0);
        return;
    }
    std::uint8_t cursor = g_restore_after_draw ? g_keep_cursor : 0;
    SafeWriteByte(At(kMenuCursorRva), cursor);
    ApplyTogglesFromSelected();
    RestoreSettings();
    if (g_layout) {
        DrawLayoutTree();
        PlaceAllLayoutFingers();
        ExpandTextClip(g_layout_max_line);
    } else {
        DrawExtraVisibleRows();
        DrawKitLabels();
    }
    g_prev_cursor = cursor;
    g_restore_after_draw = false;
}

extern "C" int ModMenuSkipTextClip() {
    if (!g_live.load(std::memory_order_acquire)) {
        return 0;
    }
    // Do not skip 41E690 for a layout: its "success" tail rewalks the
    // glyph list and restores the title 0x70 scissor, which hides ecx>4.
    return 0;
}

extern "C" int ModMenuSkipTitleBox() {
    return (g_live.load(std::memory_order_acquire) && g_layout) ? 1 : 0;
}

extern "C" int ModMenuSkipText(const char* text, int line) {
    if (!g_live.load(std::memory_order_acquire) || !text) {
        return 0;
    }
    if (g_layout) {
        return 1;
    }
    const char* slash = SlashText();
    const bool is_slash = text == slash || std::strcmp(text, slash) == 0 || std::strcmp(text, "/") == 0;
    if (is_slash) {
        const int row = ItemAtLine(line);
        return (row < 0 || RowIsSlot(row)) ? 1 : 0;
    }
    return 0;
}

void SubmitWindowBox(int box_y, int box_h, int box_w, int box_style) {
    const auto base = ModuleBase();
    if (base == 0) {
        return;
    }
#if defined(_M_IX86)
    auto* box1 = reinterpret_cast<void*>(base + kWindowBoxRva);
    auto* box2 = reinterpret_cast<void*>(base + kWindowBox2Rva);
    if (!IsExecutableAddress(box1) || !IsExecutableAddress(box2)) {
        return;
    }
    __asm {
        mov edx, box_y
        push box_style
        push box_h
        push box_w
        lea ecx, [edx + 4]
        mov eax, box1
        call eax
        add esp, 0xC
        mov edx, box_y
        push box_style
        push box_h
        push box_w
        lea ecx, [edx + 4]
        mov eax, box2
        call eax
        add esp, 0xC
    }
#else
    (void)box_y;
    (void)box_h;
    (void)box_w;
    (void)box_style;
#endif
}

extern "C" void ModMenuDrawWindow() {
    if (g_live.load(std::memory_order_acquire) && g_window_n > 0) {
        for (int i = 0; i < g_window_n; ++i) {
            SubmitWindowBox(g_windows[i].y, g_windows[i].h, g_windows[i].w, g_windows[i].style);
        }
    } else {
        int height = 0x50;
        if (g_live.load(std::memory_order_acquire)) {
            const int vis = VisibleCount();
            if (vis > kStockRows) {
                height = 16 * (1 + vis) + 16;
            }
        }
        SubmitWindowBox(0x28, height, 0x12C, 5);
    }
    if (g_live.load(std::memory_order_acquire)) {
        ExpandTextClip(VisibleCount());
    }
}

extern "C" int ModMenuPresentCount() {
    if (!g_live.load(std::memory_order_acquire)) {
        return kStockRows;
    }
    if (g_layout || MaxUsedLine() > kStockRows) {
        ExpandTextClip(MaxUsedLine());
    }
    const int vis = VisibleCount();
    return vis > kStockRows ? vis : kStockRows;
}

extern "C" int ModMenuAfterCursor() {
    if (!g_live.load(std::memory_order_acquire)) {
        return 0;
    }
    std::uint8_t cursor = 0;
    SafeReadByte(At(kMenuCursorRva), &cursor);
    std::uint32_t edge = 0;
    std::uint32_t held = 0;
    SafeReadU32(At(kPadWordRva), &edge);
    SafeReadU32(At(kPadHeldRva), &held);
    const std::uint32_t rise = held & ~g_prev_held;
    g_prev_held = held;
    std::uint32_t bits = edge;
    if ((bits & 0xF000u) == 0) {
        bits = rise;
    }

    const int vis = VisibleCount();
    const int limit = vis < 1 ? 1 : vis;
    const int last = limit - 1;
    const int max_scroll = (g_kit || g_layout)
                               ? 0
                               : (g_session.count > kVisibleRows ? g_session.count - kVisibleRows : 0);
    if ((g_kit || g_layout) && cursor >= static_cast<std::uint8_t>(limit)) {
        cursor = (bits & 0x1000u) != 0 ? static_cast<std::uint8_t>(last) : 0;
        SafeWriteByte(At(kMenuCursorRva), cursor);
        PlayCursorAnim();
    }
    // Stock Options only wraps 0↔3. Extra groups live past that.
    if ((bits & 0x1000u) != 0 && g_prev_cursor == 0 && cursor == 3) {
        if (g_scroll > 0) {
            --g_scroll;
            cursor = 0;
            SafeWriteByte(At(kMenuCursorRva), cursor);
            PlayCursorAnim();
            RequestRedraw(cursor);
        } else if (last > kStockRows - 1) {
            cursor = static_cast<std::uint8_t>(last);
            SafeWriteByte(At(kMenuCursorRva), cursor);
            PlayCursorAnim();
        }
    } else if ((bits & 0x4000u) != 0 && g_prev_cursor == 3 && cursor == 0) {
        if (last > kStockRows - 1) {
            cursor = static_cast<std::uint8_t>(kStockRows);
            SafeWriteByte(At(kMenuCursorRva), cursor);
            PlayCursorAnim();
        } else if (max_scroll > 0) {
            if (g_scroll < max_scroll) {
                ++g_scroll;
                cursor = static_cast<std::uint8_t>(last);
            } else {
                g_scroll = 0;
                cursor = 0;
            }
            SafeWriteByte(At(kMenuCursorRva), cursor);
            PlayCursorAnim();
            RequestRedraw(cursor);
        }
    }
    if (cursor >= static_cast<std::uint8_t>(limit)) {
        if (max_scroll > 0 && g_scroll < max_scroll) {
            ++g_scroll;
            cursor = static_cast<std::uint8_t>(last);
            RequestRedraw(cursor);
        } else {
            if (max_scroll > 0) {
                g_scroll = 0;
                RequestRedraw(0);
            }
            cursor = 0;
        }
        SafeWriteByte(At(kMenuCursorRva), cursor);
        PlayCursorAnim();
    }
    g_prev_cursor = cursor;

    const int row = VisibleRow(cursor);
    if (row < 0 || row >= g_session.count) {
        return 0;
    }
    if (g_layout) {
        const int n = g_session.opt_count[row];
        int sel = g_session.selected[row];
        if (n > 1) {
            if (sel < 0 || sel >= n) {
                sel = 0;
            }
            const int prev = sel;
            if ((bits & 0x8000u) != 0) {
                sel = (sel - 1 + n) % n;
            }
            if ((bits & 0x2000u) != 0) {
                sel = (sel + 1) % n;
            }
            g_session.selected[row] = sel;
            if (sel != prev) {
                PlayCursorAnim();
            }
        }
        PlaceAllLayoutFingers();
        if ((edge & 0xA000u) != 0) {
            SafeWriteU32(At(kPadWordRva), edge & ~0xA000u);
        }
        return 1;
    }
    const int n = g_session.opt_count[row];
    if (RowIsSlot(row)) {
        PlaceCursorSprite(cursor, 0);
        return 1;
    }
    if (n <= 2 && cursor < kStockRows) {
        return 0;
    }

    int sel = g_session.selected[row];
    if (sel < 0 || sel >= n) {
        sel = 0;
    }
    const int prev = sel;
    if ((bits & 0x8000u) != 0) {
        sel = (sel - 1 + n) % n;
    }
    if ((bits & 0x2000u) != 0) {
        sel = (sel + 1) % n;
    }
    g_session.selected[row] = sel;
    PlaceCursorSprite(cursor, sel);
    if (sel != prev) {
        PlayCursorAnim();
    }
    if ((edge & 0xA000u) != 0) {
        SafeWriteU32(At(kPadWordRva), edge & ~0xA000u);
    }
    return 1;
}

extern "C" void ModMenuDimOptionText(const char* text) {
    if (!g_live.load(std::memory_order_acquire) || !text) {
        return;
    }
    for (int i = 0; i < kMaxItems; ++i) {
        if (text == g_draw_left[i] || text == g_draw_right[i]) {
            SafeWriteByte(At(kColorRva), 0xC0);
            return;
        }
    }
}

extern "C" int ModMenuOnConfirm() {
    if (g_live.load(std::memory_order_acquire)) {
        RestoreSettings();
    }
    if (!g_live.load(std::memory_order_acquire)) {
        return 0;
    }
    std::uint32_t pad = 0;
    SafeReadU32(At(kPadWordRva), &pad);
    if ((pad & 0x40u) == 0) {
        return 0;
    }
    std::uint8_t cursor = 0;
    SafeReadByte(At(kMenuCursorRva), &cursor);
    const int row = VisibleRow(cursor);
    if (row < 0 || row >= g_session.count || g_session.items[row][0] == 0) {
        return 1;
    }
    const int n = g_session.opt_count[row];
    int sel = g_session.selected[row];
    if (n <= 1) {
        sel = 0;
    } else if (g_layout) {
        if (sel < 0 || sel >= n) {
            sel = 0;
        }
    } else if (n <= 2 && cursor < kStockRows) {
        std::uint8_t bit = 0;
        SafeReadByte(At(kMenuToggleRva) + static_cast<std::uintptr_t>(cursor), &bit);
        sel = bit ? 1 : 0;
    } else if (sel < 0 || sel >= n) {
        sel = 0;
    }
    g_session.choice = row;
    g_session.choice_side = sel;
    g_session.cancel = 0;
    CloseNativeUnlocked();

    return 1;
}

extern "C" void ModMenuOnCancelEdge() {
    if (g_live.load(std::memory_order_acquire)) {
        RestoreSettings();
    }
    if (!g_live.load(std::memory_order_acquire)) {
        return;
    }
    std::uint32_t pad = 0;
    SafeReadU32(At(kPadWordRva), &pad);
    if ((pad & 0x20u) == 0) {
        return;
    }
    if (g_session.choice < 0) {
        g_session.cancel = 1;
    }
}

#if defined(_M_IX86)
extern "C" __declspec(naked) void ModHdDrawDetour() {
    __asm {
        pushad
        call ModMenuShouldDrawCustom
        test eax, eax
        popad
        jne custom
        jmp dword ptr [g_mod_hd_draw_tramp]
    custom:
        pushad
        call ModMenuDrawCustom
        popad
        jmp dword ptr [g_mod_hd_draw_skip]
    }
}

extern "C" __declspec(naked) void ModType5Detour() {
    __asm {
        pushad
        call ModMenuOnType5
        popad
        jmp dword ptr [g_mod_type5_tramp]
    }
}

extern "C" __declspec(naked) void ModHdTextDetour() {
    __asm {
        pushad
        push ecx
        push edx
        call ModMenuSkipText
        add esp, 8
        test eax, eax
        popad
        jne skip
        pushad
        push edx
        call ModMenuDimOptionText
        add esp, 4
        popad
        jmp dword ptr [g_mod_hd_text_tramp]
    skip:
        ret
    }
}

extern "C" __declspec(naked) void ModFbTextDetour() {
    __asm {
        pushad
        push ecx
        push edx
        call ModMenuSkipText
        add esp, 8
        test eax, eax
        popad
        jne skip
        pushad
        push edx
        call ModMenuDimOptionText
        add esp, 4
        popad
        jmp dword ptr [g_mod_fb_text_tramp]
    skip:
        ret
    }
}

extern "C" __declspec(naked) void ModMenuAfterCursorDetour() {
    __asm {
        pushad
        call ModMenuAfterCursor
        test eax, eax
        popad
        jne skip
        jmp dword ptr [g_mod_menu_cursor_tramp]
    skip:
        jmp dword ptr [g_mod_menu_cursor_skip]
    }
}

extern "C" __declspec(naked) void ModMenuConfirmDetour() {
    __asm {
        pushad
        call ModMenuOnConfirm
        test eax, eax
        popad
        jne skip
        jmp dword ptr [g_mod_menu_confirm_tramp]
    skip:
        jmp dword ptr [g_mod_menu_confirm_skip]
    }
}

extern "C" __declspec(naked) void ModMenuCancelDetour() {
    __asm {
        pushad
        call ModMenuOnCancelEdge
        popad
        jmp dword ptr [g_mod_menu_cancel_tramp]
    }
}

extern "C" __declspec(naked) void ModMenuState0CursorDetour() {
    __asm {
        pushad
        call ModMenuAfterState0Draw
        popad
        jmp dword ptr [g_mod_state0_resume]
    }
}

extern "C" __declspec(naked) void ModMenuWindowDetour() {
    __asm {
        pushad
        call ModMenuDrawWindow
        popad
        jmp dword ptr [g_mod_window_resume]
    }
}

extern "C" __declspec(naked) void ModMenuPresentDetour() {
    __asm {
        call ModMenuPresentCount
        mov ecx, eax
        jmp dword ptr [g_mod_present_resume]
    }
}

extern "C" __declspec(naked) void ModTitleBoxDetour() {
    __asm {
        pushad
        call ModMenuSkipTitleBox
        test eax, eax
        popad
        jne skip
        jmp dword ptr [g_mod_title_box_tramp]
    skip:
        ret
    }
}

extern "C" __declspec(naked) void ModMenuClipDetour() {
    __asm {
        push ebp
        mov ebp, esp
        sub esp, 0x2c
        pushad
        call ModMenuSkipTextClip
        test eax, eax
        popad
        je rest
        mov eax, dword ptr [g_mod_stack_cookie]
        mov eax, dword ptr [eax]
        xor eax, ebp
        mov dword ptr [ebp - 4], eax
        push ebx
        mov ebx, ecx
        push esi
        push edi
        mov dword ptr [ebp - 0x10], ebx
        jmp dword ptr [g_mod_clip_success]
    rest:
        jmp dword ptr [g_mod_clip_rest]
    }
}
#endif

bool InstallMenuHooks() {
#if !defined(_M_IX86)
    return false;
#else
    const auto base = ModuleBase();
    if (base == 0) {
        return false;
    }

    std::uint8_t want_draw[5]{};
    std::uint8_t want_cursor[10]{};
    std::uint8_t want_confirm[7]{};
    std::uint8_t want_cancel[7]{};
    std::uint8_t want_state0[7]{};
    ExpectedHdDraw(want_draw, base);
    ExpectedAfterCursor(want_cursor, base);
    ExpectedConfirm(want_confirm, base);
    ExpectedCancel(want_cancel, base);
    ExpectedState0Cursor(want_state0, base);

    auto* draw = reinterpret_cast<std::uint8_t*>(base + kHdDrawSiteRva);
    if (!IsExecutableAddress(draw)) {
        LogWarn("Game.Menu HD draw +0x6305E not executable");
        return false;
    }
    if (!BytesMatch(draw, want_draw, kHdDrawPatch)) {
        LogSiteMismatch("HD draw", kHdDrawSiteRva, draw, kHdDrawPatch);
        return false;
    }
    g_hd_draw_tramp_mem = MakeTrampoline(draw, kHdDrawPatch, draw + kHdDrawPatch);
    if (!g_hd_draw_tramp_mem) {
        LogWarn("Game.Menu HD draw trampoline alloc failed");
        return false;
    }
    g_mod_hd_draw_tramp = g_hd_draw_tramp_mem;
    g_mod_hd_draw_skip = reinterpret_cast<void*>(base + kHdDrawSkipRva);
    if (!WriteJump(draw, reinterpret_cast<void*>(&ModHdDrawDetour), g_hd_draw_original, kHdDrawPatch)) {
        VirtualFree(g_hd_draw_tramp_mem, 0, MEM_RELEASE);
        g_hd_draw_tramp_mem = nullptr;
        g_mod_hd_draw_tramp = nullptr;
        LogWarn("Game.Menu HD draw hook failed");
        return false;
    }
    g_hd_draw_site = draw;

    auto* type5 = reinterpret_cast<std::uint8_t*>(base + kType5Rva);
    if (!IsExecutableAddress(type5) || !BytesMatch(type5, kType5Bytes, kType5Patch)) {
        LogSiteMismatch("type5", kType5Rva, type5, kType5Patch);
        RemoveMenuHooks();
        return false;
    }
    g_type5_tramp_mem = MakeTrampoline(type5, kType5Patch, type5 + kType5Patch);
    if (!g_type5_tramp_mem ||
        !WriteJump(type5, reinterpret_cast<void*>(&ModType5Detour), g_type5_original, kType5Patch)) {
        LogWarn("Game.Menu type5 hook failed");
        RemoveMenuHooks();
        return false;
    }
    g_mod_type5_tramp = g_type5_tramp_mem;
    g_type5_site = type5;

    auto* hd_text = reinterpret_cast<std::uint8_t*>(base + kHdDrawRva);
    if (!IsExecutableAddress(hd_text) || !BytesMatch(hd_text, kHdTextBytes, kHdTextPatch)) {
        LogSiteMismatch("HD text", kHdDrawRva, hd_text, kHdTextPatch);
        RemoveMenuHooks();
        return false;
    }
    g_hd_text_tramp_mem = MakeTrampoline(hd_text, kHdTextPatch, hd_text + kHdTextPatch);
    if (!g_hd_text_tramp_mem ||
        !WriteJump(hd_text, reinterpret_cast<void*>(&ModHdTextDetour), g_hd_text_original,
                   kHdTextPatch)) {
        LogWarn("Game.Menu HD text hook failed");
        RemoveMenuHooks();
        return false;
    }
    g_mod_hd_text_tramp = g_hd_text_tramp_mem;
    g_hd_text_site = hd_text;

    auto* fb_text = reinterpret_cast<std::uint8_t*>(base + kFbTextRva);
    if (!IsExecutableAddress(fb_text) || !BytesMatch(fb_text, kHdTextBytes, kHdTextPatch)) {
        LogSiteMismatch("FB text", kFbTextRva, fb_text, kHdTextPatch);
        RemoveMenuHooks();
        return false;
    }
    g_fb_text_tramp_mem = MakeTrampoline(fb_text, kHdTextPatch, fb_text + kHdTextPatch);
    if (!g_fb_text_tramp_mem ||
        !WriteJump(fb_text, reinterpret_cast<void*>(&ModFbTextDetour), g_fb_text_original,
                   kHdTextPatch)) {
        LogWarn("Game.Menu FB text hook failed");
        RemoveMenuHooks();
        return false;
    }
    g_mod_fb_text_tramp = g_fb_text_tramp_mem;
    g_fb_text_site = fb_text;

    auto* confirm = reinterpret_cast<std::uint8_t*>(base + kConfirmSiteRva);
    if (!IsExecutableAddress(confirm) || !BytesMatch(confirm, want_confirm, kConfirmPatch)) {
        LogSiteMismatch("confirm", kConfirmSiteRva, confirm, kConfirmPatch);
        RemoveMenuHooks();
        return false;
    }
    g_confirm_tramp_mem = MakeTrampoline(confirm, kConfirmPatch, confirm + kConfirmPatch);
    if (!g_confirm_tramp_mem ||
        !WriteJump(confirm, reinterpret_cast<void*>(&ModMenuConfirmDetour), g_confirm_original,
                   kConfirmPatch)) {
        LogWarn("Game.Menu confirm hook failed");
        RemoveMenuHooks();
        return false;
    }
    g_mod_menu_confirm_tramp = g_confirm_tramp_mem;
    g_mod_menu_confirm_skip = reinterpret_cast<void*>(base + kConfirmSkipRva);
    g_confirm_site = confirm;

    auto* cancel = reinterpret_cast<std::uint8_t*>(base + kCancelSiteRva);
    if (!IsExecutableAddress(cancel) || !BytesMatch(cancel, want_cancel, kCancelPatch)) {
        LogSiteMismatch("cancel", kCancelSiteRva, cancel, kCancelPatch);
        RemoveMenuHooks();
        return false;
    }
    g_cancel_tramp_mem = MakeTrampoline(cancel, kCancelPatch, cancel + kCancelPatch);
    if (!g_cancel_tramp_mem ||
        !WriteJump(cancel, reinterpret_cast<void*>(&ModMenuCancelDetour), g_cancel_original,
                   kCancelPatch)) {
        LogWarn("Game.Menu cancel hook failed");
        RemoveMenuHooks();
        return false;
    }
    g_mod_menu_cancel_tramp = g_cancel_tramp_mem;
    g_cancel_site = cancel;

    auto* cursor = reinterpret_cast<std::uint8_t*>(base + kAfterCursorSiteRva);
    if (!IsExecutableAddress(cursor) || !BytesMatch(cursor, want_cursor, kAfterCursorPatch)) {
        LogSiteMismatch("L/R", kAfterCursorSiteRva, cursor, kAfterCursorPatch);
        RemoveMenuHooks();
        return false;
    }
    g_cursor_tramp_mem = MakeTrampoline(cursor, kAfterCursorPatch, cursor + kAfterCursorPatch);
    if (!g_cursor_tramp_mem ||
        !WriteJump(cursor, reinterpret_cast<void*>(&ModMenuAfterCursorDetour), g_cursor_original,
                   kAfterCursorPatch)) {
        LogWarn("Game.Menu L/R hook failed");
        RemoveMenuHooks();
        return false;
    }
    g_mod_menu_cursor_tramp = g_cursor_tramp_mem;
    g_mod_menu_cursor_skip = reinterpret_cast<void*>(base + kAfterCursorSkipRva);
    g_cursor_site = cursor;

    auto* state0 = reinterpret_cast<std::uint8_t*>(base + kState0CursorRva);
    if (!IsExecutableAddress(state0) || !BytesMatch(state0, want_state0, kState0CursorPatch)) {
        LogSiteMismatch("state0 cursor", kState0CursorRva, state0, kState0CursorPatch);
        RemoveMenuHooks();
        return false;
    }
    g_mod_state0_resume = reinterpret_cast<void*>(base + kState0ResumeRva);
    if (!WriteJump(state0, reinterpret_cast<void*>(&ModMenuState0CursorDetour), g_state0_original,
                   kState0CursorPatch)) {
        LogWarn("Game.Menu state0 cursor hook failed");
        g_mod_state0_resume = nullptr;
        RemoveMenuHooks();
        return false;
    }
    g_state0_site = state0;

    auto* window = reinterpret_cast<std::uint8_t*>(base + kWindowSiteRva);
    if (!IsExecutableAddress(window) || !BytesMatch(window, kWindowBytes, kWindowPatch)) {
        LogSiteMismatch("window", kWindowSiteRva, window, kWindowPatch);
        RemoveMenuHooks();
        return false;
    }
    g_mod_window_resume = reinterpret_cast<void*>(base + kWindowResumeRva);
    if (!WriteJump(window, reinterpret_cast<void*>(&ModMenuWindowDetour), g_window_original,
                   kWindowPatch)) {
        LogWarn("Game.Menu window hook failed");
        g_mod_window_resume = nullptr;
        RemoveMenuHooks();
        return false;
    }
    g_window_site = window;

    auto* present = reinterpret_cast<std::uint8_t*>(base + kPresentCountRva);
    if (!IsExecutableAddress(present) || !BytesMatch(present, kPresentBytes, kPresentPatch)) {
        LogSiteMismatch("present", kPresentCountRva, present, kPresentPatch);
        RemoveMenuHooks();
        return false;
    }
    g_mod_present_resume = reinterpret_cast<void*>(base + kPresentResumeRva);
    if (!WriteJump(present, reinterpret_cast<void*>(&ModMenuPresentDetour), g_present_original,
                   kPresentPatch)) {
        LogWarn("Game.Menu present hook failed");
        g_mod_present_resume = nullptr;
        RemoveMenuHooks();
        return false;
    }
    g_present_site = present;

    auto* clip = reinterpret_cast<std::uint8_t*>(base + kTextClipFnRva);
    if (!IsExecutableAddress(clip) || !BytesMatch(clip, kTextClipBytes, kTextClipPatch)) {
        LogSiteMismatch("text clip", kTextClipFnRva, clip, kTextClipPatch);
        RemoveMenuHooks();
        return false;
    }
    g_mod_clip_rest = reinterpret_cast<void*>(base + kTextClipRestRva);
    g_mod_clip_success = reinterpret_cast<void*>(base + kTextClipSuccessRva);
    g_mod_stack_cookie = reinterpret_cast<void*>(base + kStackCookieRva);
    if (!WriteJump(clip, reinterpret_cast<void*>(&ModMenuClipDetour), g_clip_original,
                   kTextClipPatch)) {
        LogWarn("Game.Menu text clip hook failed");
        g_mod_clip_rest = nullptr;
        g_mod_clip_success = nullptr;
        g_mod_stack_cookie = nullptr;
        RemoveMenuHooks();
        return false;
    }
    g_clip_site = clip;

    auto* title = reinterpret_cast<std::uint8_t*>(base + kTitleBoxRva);
    if (!IsExecutableAddress(title) || !BytesMatch(title, kHdTextBytes, kHdTextPatch)) {
        LogSiteMismatch("title box", kTitleBoxRva, title, kHdTextPatch);
        RemoveMenuHooks();
        return false;
    }
    g_title_box_tramp_mem = MakeTrampoline(title, kHdTextPatch, title + kHdTextPatch);
    if (!g_title_box_tramp_mem ||
        !WriteJump(title, reinterpret_cast<void*>(&ModTitleBoxDetour), g_title_box_original,
                   kHdTextPatch)) {
        LogWarn("Game.Menu title box hook failed");
        RemoveMenuHooks();
        return false;
    }
    g_mod_title_box_tramp = g_title_box_tramp_mem;
    g_title_box_site = title;

    return true;
#endif
}

void RemoveMenuHooks() {
    if (g_title_box_site) {
        RestoreBytes(g_title_box_site, g_title_box_original, kHdTextPatch);
        g_title_box_site = nullptr;
    }
    if (g_clip_site) {
        RestoreBytes(g_clip_site, g_clip_original, kTextClipPatch);
        g_clip_site = nullptr;
    }
    if (g_present_site) {
        RestoreBytes(g_present_site, g_present_original, kPresentPatch);
        g_present_site = nullptr;
    }
    if (g_window_site) {
        RestoreBytes(g_window_site, g_window_original, kWindowPatch);
        g_window_site = nullptr;
    }
    if (g_state0_site) {
        RestoreBytes(g_state0_site, g_state0_original, kState0CursorPatch);
        g_state0_site = nullptr;
    }
    if (g_cursor_site) {
        RestoreBytes(g_cursor_site, g_cursor_original, kAfterCursorPatch);
        g_cursor_site = nullptr;
    }
    if (g_cancel_site) {
        RestoreBytes(g_cancel_site, g_cancel_original, kCancelPatch);
        g_cancel_site = nullptr;
    }
    if (g_confirm_site) {
        RestoreBytes(g_confirm_site, g_confirm_original, kConfirmPatch);
        g_confirm_site = nullptr;
    }
    if (g_fb_text_site) {
        RestoreBytes(g_fb_text_site, g_fb_text_original, kHdTextPatch);
        g_fb_text_site = nullptr;
    }
    if (g_hd_text_site) {
        RestoreBytes(g_hd_text_site, g_hd_text_original, kHdTextPatch);
        g_hd_text_site = nullptr;
    }
    if (g_type5_site) {
        RestoreBytes(g_type5_site, g_type5_original, kType5Patch);
        g_type5_site = nullptr;
    }
    if (g_hd_draw_site) {
        RestoreBytes(g_hd_draw_site, g_hd_draw_original, kHdDrawPatch);
        g_hd_draw_site = nullptr;
    }
    if (g_fb_text_tramp_mem) {
        VirtualFree(g_fb_text_tramp_mem, 0, MEM_RELEASE);
        g_fb_text_tramp_mem = nullptr;
    }
    if (g_hd_text_tramp_mem) {
        VirtualFree(g_hd_text_tramp_mem, 0, MEM_RELEASE);
        g_hd_text_tramp_mem = nullptr;
    }
    if (g_type5_tramp_mem) {
        VirtualFree(g_type5_tramp_mem, 0, MEM_RELEASE);
        g_type5_tramp_mem = nullptr;
    }
    if (g_cursor_tramp_mem) {
        VirtualFree(g_cursor_tramp_mem, 0, MEM_RELEASE);
        g_cursor_tramp_mem = nullptr;
    }
    if (g_cancel_tramp_mem) {
        VirtualFree(g_cancel_tramp_mem, 0, MEM_RELEASE);
        g_cancel_tramp_mem = nullptr;
    }
    if (g_confirm_tramp_mem) {
        VirtualFree(g_confirm_tramp_mem, 0, MEM_RELEASE);
        g_confirm_tramp_mem = nullptr;
    }
    if (g_hd_draw_tramp_mem) {
        VirtualFree(g_hd_draw_tramp_mem, 0, MEM_RELEASE);
        g_hd_draw_tramp_mem = nullptr;
    }
    if (g_title_box_tramp_mem) {
        VirtualFree(g_title_box_tramp_mem, 0, MEM_RELEASE);
        g_title_box_tramp_mem = nullptr;
    }
#if defined(_M_IX86)
    g_mod_hd_draw_tramp = nullptr;
    g_mod_hd_draw_skip = nullptr;
    g_mod_type5_tramp = nullptr;
    g_mod_hd_text_tramp = nullptr;
    g_mod_fb_text_tramp = nullptr;
    g_mod_menu_cursor_tramp = nullptr;
    g_mod_menu_cursor_skip = nullptr;
    g_mod_menu_confirm_tramp = nullptr;
    g_mod_menu_confirm_skip = nullptr;
    g_mod_menu_cancel_tramp = nullptr;
    g_mod_state0_resume = nullptr;
    g_mod_window_resume = nullptr;
    g_mod_present_resume = nullptr;
    g_mod_clip_rest = nullptr;
    g_mod_clip_success = nullptr;
    g_mod_stack_cookie = nullptr;
    g_mod_title_box_tramp = nullptr;
#endif
}

int NextInt(const char*& p) {
    while (*p == '\t' || *p == ' ') {
        ++p;
    }
    char* end = nullptr;
    const long v = std::strtol(p, &end, 0);
    if (end) {
        p = end;
    }
    return static_cast<int>(v);
}

void NextField(const char*& p, char* dest, int max) {
    while (*p == '\t') {
        ++p;
    }
    int n = 0;
    while (p[n] && p[n] != '\t' && p[n] != '\n' && n < max) {
        dest[n] = p[n] == '\r' ? ' ' : p[n];
        ++n;
    }
    dest[n] = 0;
    p += n;
    while (*p && *p != '\t' && *p != '\n') {
        ++p;
    }
}

void SkipLine(const char*& p) {
    while (*p && *p != '\n') {
        ++p;
    }
    if (*p == '\n') {
        ++p;
    }
}

int AllocNode(std::uint8_t kind, std::uint8_t style) {
    if (g_node_n >= kMaxNodes) {
        return -1;
    }
    const int id = g_node_n++;
    g_nodes[id] = LNode{};
    g_nodes[id].kind = kind;
    g_nodes[id].style = style;
    g_nodes[id].child0 = -1;
    g_nodes[id].sib = -1;
    g_nodes[id].group = -1;
    return id;
}

void AttachNode(int parent, int id) {
    if (id < 0) {
        return;
    }
    if (parent < 0) {
        if (g_root_n < 16) {
            g_roots[g_root_n++] = id;
        }
        return;
    }
    if (g_nodes[parent].child0 < 0) {
        g_nodes[parent].child0 = static_cast<std::int16_t>(id);
        return;
    }
    int c = g_nodes[parent].child0;
    while (g_nodes[c].sib >= 0) {
        c = g_nodes[c].sib;
    }
    g_nodes[c].sib = static_cast<std::int16_t>(id);
}

int FirstLabelText(int id, char* dest, int max) {
    if (id < 0) {
        return 0;
    }
    if (g_nodes[id].text[0]) {
        CopyBounded(dest, g_nodes[id].text, max);
        return 1;
    }
    for (int c = g_nodes[id].child0; c >= 0; c = g_nodes[c].sib) {
        if (FirstLabelText(c, dest, max)) {
            return 1;
        }
    }
    return 0;
}

int MeasureNode(int id) {
    if (id < 0) {
        return 0;
    }
    const auto& n = g_nodes[id];
    if (n.kind == kNLab || (n.kind == kNItem && n.child0 < 0)) {
        return 1;
    }
    if (n.kind == kNRow) {
        int m = 1;
        for (int c = n.child0; c >= 0; c = g_nodes[c].sib) {
            const int h = MeasureNode(c);
            if (h > m) {
                m = h;
            }
        }
        return m;
    }
    int sum = 0;
    for (int c = n.child0; c >= 0; c = g_nodes[c].sib) {
        sum += MeasureNode(c);
    }
    return sum > 0 ? sum : 1;
}

void PlaceNode(int id, int line, int indent) {
    if (id < 0) {
        return;
    }
    auto& n = g_nodes[id];
    n.line = line;
    if (line > g_layout_max_line) {
        g_layout_max_line = line;
    }
    if (n.kind == kNLab) {
        n.width = indent;
        return;
    }
    if (n.kind == kNItem && n.child0 < 0) {
        n.width = indent;
        return;
    }
    if (n.kind == kNRow) {
        int label_w = indent;
        int item_i = 0;
        int extra_w = 0x15;
        for (int c = n.child0; c >= 0; c = g_nodes[c].sib) {
            auto& ch = g_nodes[c];
            ch.line = line;
            if (line > g_layout_max_line) {
                g_layout_max_line = line;
            }
            if (ch.kind == kNLab) {
                ch.width = label_w;
                label_w += CStringLen(ch.text) + 1;
            } else if (ch.kind == kNItem && ch.child0 < 0) {
                if (item_i == 0) {
                    ch.width = 0x14;
                } else if (item_i == 1) {
                    ch.width = 0x15;
                } else {
                    extra_w += 1 + CStringLen(ch.text);
                    ch.width = extra_w - CStringLen(ch.text);
                }
                ++item_i;
            } else {
                PlaceNode(c, line, indent);
            }
        }
        return;
    }
    int y = line;
    const int child_indent = n.kind == kNItem ? indent + 2 : indent;
    for (int c = n.child0; c >= 0; c = g_nodes[c].sib) {
        PlaceNode(c, y, child_indent);
        y += MeasureNode(c);
        if (y - 1 > g_layout_max_line) {
            g_layout_max_line = y - 1;
        }
    }
}

void FlushItemRun(const int* ids, int n, int line) {
    if (n <= 0 || g_group_n >= kMaxItems) {
        return;
    }
    const int gi = g_group_n;
    auto& g = g_groups[gi];
    g.n = n > kMaxOptions ? kMaxOptions : n;
    g.sel = 0;
    g.line = line;
    g.block = false;
    g_session.items[gi][0] = 0;
    if (ids[0] >= 0 && g_nodes[ids[0]].text[0]) {
        CopyLabel(g_session.items[gi], g_nodes[ids[0]].text);
    }
    if (!g_session.items[gi][0]) {
        CopyLabel(g_session.items[gi], "*");
    }
    g_session.opt_count[gi] = g.n;
    g_session.selected[gi] = 0;
    g_item_line[gi] = line;
    for (int i = 0; i < g.n; ++i) {
        g.item_node[i] = ids[i];
        g.width[i] = g_nodes[ids[i]].width;
        CopyLabel(g_session.options[gi][i], g_nodes[ids[i]].text);
        g_nodes[ids[i]].group = gi;
    }
    ++g_group_n;
}

void CollectGroups(int id) {
    if (id < 0) {
        return;
    }
    const auto& n = g_nodes[id];
    if (n.kind == kNItem && n.child0 >= 0) {
        if (g_group_n >= kMaxItems) {
            return;
        }
        const int gi = g_group_n;
        auto& g = g_groups[gi];
        g.n = 1;
        g.sel = 0;
        g.line = n.line;
        g.block = true;
        g.item_node[0] = id;
        g.width[0] = 0;
        g_session.items[gi][0] = 0;
        FirstLabelText(id, g_session.items[gi], kMaxLabel);
        if (!g_session.items[gi][0]) {
            CopyLabel(g_session.items[gi], "*");
        }
        CopyLabel(g_session.options[gi][0], " ");
        g_session.opt_count[gi] = 1;
        g_session.selected[gi] = 0;
        g_item_line[gi] = n.line;
        g_nodes[id].group = gi;
        ++g_group_n;
        return;
    }
    if (n.kind == kNItem) {
        int one = id;
        FlushItemRun(&one, 1, n.line);
        return;
    }
    if (n.kind == kNRow) {
        int run[kMaxOptions]{};
        int rn = 0;
        for (int c = n.child0; c >= 0; c = g_nodes[c].sib) {
            if (g_nodes[c].kind == kNItem && g_nodes[c].child0 < 0) {
                if (rn < kMaxOptions) {
                    run[rn++] = c;
                }
            } else {
                FlushItemRun(run, rn, rn > 0 ? g_nodes[run[0]].line : n.line);
                rn = 0;
                CollectGroups(c);
            }
        }
        FlushItemRun(run, rn, rn > 0 ? g_nodes[run[0]].line : n.line);
        return;
    }
    if (n.kind == kNLab) {
        return;
    }
    for (int c = n.child0; c >= 0; c = g_nodes[c].sib) {
        CollectGroups(c);
    }
}

void PlaceLayoutRoots() {
    g_window_n = 0;
    g_layout_max_line = 0;
    int line = 1;
    int box_y = 0x28;
    for (int i = 0; i < g_root_n; ++i) {
        const int id = g_roots[i];
        const int lines = MeasureNode(id);
        if (g_nodes[id].kind == kNBox && g_window_n < kMaxWindows) {
            const int h = lines * 16 + 16;
            const int w = lines >= 8 ? 0x150 : 0x12C;
            g_windows[g_window_n].y = box_y;
            g_windows[g_window_n].w = w;
            g_windows[g_window_n].h = h;
            g_windows[g_window_n].style = g_nodes[id].style ? g_nodes[id].style : 5;
            ++g_window_n;
            PlaceNode(id, line, 0);
            line += lines;
            box_y += h + 4;
        } else {
            PlaceNode(id, line, 0);
            line += lines;
            box_y += lines * 16;
        }
    }
}

void DrawLayoutNode(int id, int focus) {
    if (id < 0) {
        return;
    }
    const auto& n = g_nodes[id];
    if (n.kind == kNLab) {
        if (n.text[0]) {
            DrawMenuCell(n.line, n.text, n.width, 0xF0);
        }
        return;
    }
    if (n.kind == kNItem && n.child0 < 0) {
        std::uint8_t color = 0xC0;
        if (n.group == focus && n.group >= 0) {
            const auto& g = g_groups[n.group];
            if (g.item_node[g.sel] == id) {
                color = 0xF0;
            }
        }
        if (n.text[0]) {
            DrawMenuCell(n.line, n.text, n.width, color);
        }
        return;
    }
    if (n.kind == kNRow) {
        int prev_item = -1;
        for (int c = n.child0; c >= 0; c = g_nodes[c].sib) {
            if (g_nodes[c].kind == kNItem && g_nodes[c].child0 < 0) {
                if (prev_item >= 0) {
                    static const char kSlash[] = "/";
                    const int sw = g_nodes[c].width > 0 ? g_nodes[c].width - 1 : 0x0F;
                    DrawMenuCell(g_nodes[c].line, kSlash, sw, 0xC0);
                }
                prev_item = c;
            } else {
                prev_item = -1;
            }
            DrawLayoutNode(c, focus);
        }
        return;
    }
    for (int c = n.child0; c >= 0; c = g_nodes[c].sib) {
        DrawLayoutNode(c, focus);
    }
}

void DrawLayoutTree() {
    std::uint8_t cursor = 0;
    SafeReadByte(At(kMenuCursorRva), &cursor);
    const int focus = VisibleRow(cursor);
    int bottom = (g_layout_max_line + 2) * 16;
    if (bottom < 0xF0) {
        bottom = 0xF0;
    }
    // Drop the vanilla title 0x70 box so ecx>4 is not scissored.
    RewindGlyphs();
    ExpandTextClip(g_layout_max_line);
    SubmitTextFrame(0, bottom);
    if (g_session.title[0]) {
        DrawMenuCell(0, g_session.title, 0, 0xF0);
    }
    for (int i = 0; i < g_root_n; ++i) {
        DrawLayoutNode(g_roots[i], focus);
    }
    ExpandTextClip(g_layout_max_line);
    static int logged = 0;
    if (logged < 2) {
        ++logged;

    }
}

void PlaceAllLayoutFingers() {
    std::uint8_t cursor = 0;
    SafeReadByte(At(kMenuCursorRva), &cursor);
    const int focus = VisibleRow(cursor);
    for (int slot = 0; slot < kVisibleRows; ++slot) {
        const auto dest = At(kCursorSlotRva) + static_cast<std::uintptr_t>(slot) * kCursorStride;
        if (slot != cursor || focus < 0 || focus >= g_group_n) {
            WriteU16(dest, 0);
            continue;
        }
        const auto& g = g_groups[focus];
        int sel = g_session.selected[focus];
        if (sel < 0 || sel >= g.n) {
            sel = 0;
        }
        const std::uint16_t finger_y = static_cast<std::uint16_t>((g.line + 2) * 16);
        if (g.block) {
            WriteU16(dest, 1);
            WriteU16(dest + 2, 0x48);
            WriteU16(dest + 4, finger_y);
            WriteU16(dest + 6, 0x80);
            WriteU16(dest + 8, 0x10);
            SafeWriteByte(dest + 0xA, 0);
            WriteU16(dest + 0xB, 0);
            continue;
        }
        const int wparam = g.width[sel];
        const int sl = CStringLen(g_session.options[focus][sel]);
        const auto x = static_cast<std::uint16_t>(0x48 + wparam * 8);
        const auto w = static_cast<std::uint16_t>(sl > 0 ? sl * 8 : 0x10);
        WriteU16(dest, 1);
        WriteU16(dest + 2, x);
        WriteU16(dest + 4, finger_y);
        WriteU16(dest + 6, w);
        WriteU16(dest + 8, 0x10);
        SafeWriteByte(dest + 0xA, 0);
        WriteU16(dest + 0xB, 0);
    }
}

void ParseLayout(const char* body) {
    ResetWidgets();
    g_layout = true;
    g_kit = true;
    g_session.count = 0;
    const char* p = body ? body : "";
    int stack[8];
    int sp = 0;
    int parent = -1;
    while (*p) {
        while (*p == '\n') {
            ++p;
        }
        if (!*p) {
            break;
        }
        if (p[0] == '-' ) {
            if (sp > 0) {
                --sp;
                parent = sp > 0 ? stack[sp - 1] : -1;
            }
            SkipLine(p);
            continue;
        }
        if (p[0] == '+' && p[1]) {
            const char kindc = p[1];
            p += 2;
            std::uint8_t kind = 0;
            std::uint8_t style = 5;
            if (kindc == 'C') {
                kind = kNCol;
            } else if (kindc == 'R') {
                kind = kNRow;
            } else if (kindc == 'X') {
                kind = kNBox;
                style = static_cast<std::uint8_t>(NextInt(p));
                if (style == 0) {
                    style = 5;
                }
            } else if (kindc == 'I') {
                kind = kNItem;
            }
            const int id = kind ? AllocNode(kind, style) : -1;
            AttachNode(parent, id);
            if (id >= 0 && sp < 8) {
                stack[sp++] = id;
                parent = id;
            }
            SkipLine(p);
            continue;
        }
        if (p[0] == 'L' || p[0] == 'I') {
            const std::uint8_t kind = p[0] == 'L' ? kNLab : kNItem;
            ++p;
            const int id = AllocNode(kind, 5);
            if (id >= 0) {
                NextField(p, g_nodes[id].text, kMaxMessage);
                AttachNode(parent, id);
            }
            SkipLine(p);
            continue;
        }
        SkipLine(p);
    }
    PlaceLayoutRoots();
    std::memset(g_session.items, 0, sizeof(g_session.items));
    std::memset(g_session.options, 0, sizeof(g_session.options));
    std::memset(g_session.opt_count, 0, sizeof(g_session.opt_count));
    std::memset(g_session.selected, 0, sizeof(g_session.selected));
    g_group_n = 0;
    for (int i = 0; i < g_root_n; ++i) {
        CollectGroups(g_roots[i]);
    }
    g_session.count = g_group_n;
    if (g_window_n <= 0) {
        AddDefaultWindow();
    }
}

void ParseKit(const char* body) {
    ResetWidgets();
    g_kit = true;
    g_session.count = 0;
    const char* p = body ? body : "";
    while (*p) {
        while (*p == '\n') {
            ++p;
        }
        if (!*p) {
            break;
        }
        const char kind = *p++;
        if (kind == 'W' && g_window_n < kMaxWindows) {
            g_windows[g_window_n].y = NextInt(p);
            g_windows[g_window_n].w = NextInt(p);
            g_windows[g_window_n].h = NextInt(p);
            g_windows[g_window_n].style = NextInt(p);
            ++g_window_n;
        } else if (kind == 'L' && g_label_n < kMaxLabels) {
            g_labels[g_label_n].line = ClampLine(NextInt(p));
            NextField(p, g_labels[g_label_n].text, kMaxMessage);
            ++g_label_n;
        } else if ((kind == 'B' || kind == 'C') && g_session.count < kMaxItems) {
            const int i = g_session.count;
            g_item_line[i] = ClampLine(NextInt(p));
            NextField(p, g_session.items[i], kMaxLabel);
            g_session.opt_count[i] = 0;
            g_session.selected[i] = 0;
            if (kind == 'B') {
                CopyLabel(g_session.options[i][0], " ");
                g_session.opt_count[i] = 1;
            } else {
                while (*p == '\t' && g_session.opt_count[i] < kMaxOptions) {
                    const int oi = g_session.opt_count[i];
                    NextField(p, g_session.options[i][oi], kMaxLabel);
                    ++g_session.opt_count[i];
                }
                if (g_session.opt_count[i] <= 0) {
                    DefaultYesNo(i);
                }
            }
            ++g_session.count;
        }
        SkipLine(p);
    }
    AddDefaultWindow();
}

}  // namespace grandia_mod

namespace grandia_mod {

int MenuOpenImpl(const char* title, const char* joined, int count) {
    if (!joined || count <= 0) {
        return 0;
    }
    std::lock_guard<std::mutex> lock(g_mu);
    g_session = Session{};
    SetLive(false);
    ResetWidgets();
    CopyLabel(g_session.title, title ? title : "");
    if (static_cast<unsigned char>(joined[0]) == 3 && joined[1] == '\n') {
        ParseLayout(joined + 2);
    } else if (static_cast<unsigned char>(joined[0]) == 2 && joined[1] == '\n') {
        ParseKit(joined + 2);
    } else {
        SplitItems(joined, count);
        AddDefaultWindow();
    }
    if (g_session.count <= 0) {
        return 0;
    }
    g_have_saved_toggles = false;
    g_scroll = 0;
    g_need_redraw = false;
    g_restore_after_draw = false;
    g_keep_cursor = 0;
    g_session.pending = true;
    g_session.choice = -1;
    g_session.choice_side = 0;
    g_session.cancel = 0;
    g_open_fail_log = 0;
    g_lookup_log = 0;

    return 1;
}

int MenuCloseImpl() {
    std::lock_guard<std::mutex> lock(g_mu);
    if (g_session.live) {
        CloseNativeUnlocked();
    }
    g_session.pending = false;
    SetLive(false);
    return 1;
}

int MenuIsOpenImpl() {
    std::lock_guard<std::mutex> lock(g_mu);
    return (g_session.live || g_session.pending) ? 1 : 0;
}

int MenuCursorImpl() {
    std::uint8_t cursor = 0;
    SafeReadByte(At(kMenuCursorRva), &cursor);
    if (g_kit) {
        return static_cast<int>(cursor);
    }
    return g_scroll + static_cast<int>(cursor);
}

int MenuOptionImpl() {
    if (g_live.load(std::memory_order_acquire)) {
        std::uint8_t cursor = 0;
        SafeReadByte(At(kMenuCursorRva), &cursor);
        const int row = VisibleRow(cursor);
        if (row >= 0 && row < g_session.count) {
            if (g_session.opt_count[row] <= 1) {
                return 0;
            }
            if (!g_layout && g_session.opt_count[row] <= 2 && cursor < kStockRows) {
                std::uint8_t bit = 0;
                SafeReadByte(At(kMenuToggleRva) + static_cast<std::uintptr_t>(cursor), &bit);
                return bit ? 1 : 0;
            }
            int sel = g_session.selected[row];
            return sel < 0 ? 0 : sel;
        }
    }
    return g_session.choice_side;
}

int MenuTakeChoiceImpl() {
    std::lock_guard<std::mutex> lock(g_mu);
    const int n = g_session.choice;
    g_session.choice = -1;
    return n;
}

int MenuTakeCancelImpl() {
    std::lock_guard<std::mutex> lock(g_mu);
    const int n = g_session.cancel;
    g_session.cancel = 0;
    return n;
}

const char* MenuLookupImpl(const char* key) {
    const char* text = LookupCustom(key);
    if (text && g_lookup_log == 0) {
        g_lookup_log = 1;

    }
    return text;
}

}  // namespace grandia_mod

extern "C" int ModMenuOpen(const char* title, const char* joined, int count) {
    return grandia_mod::MenuOpenImpl(title, joined, count);
}

extern "C" int ModMenuClose() {
    return grandia_mod::MenuCloseImpl();
}

extern "C" int ModMenuIsOpen() {
    return grandia_mod::MenuIsOpenImpl();
}

extern "C" int ModMenuCursor() {
    return grandia_mod::MenuCursorImpl();
}

extern "C" int ModMenuTakeChoice() {
    return grandia_mod::MenuTakeChoiceImpl();
}

extern "C" int ModMenuTakeCancel() {
    return grandia_mod::MenuTakeCancelImpl();
}

extern "C" int ModMenuOption() {
    return grandia_mod::MenuOptionImpl();
}

extern "C" const char* ModMenuLookupOverride(const char* key) {
    return grandia_mod::MenuLookupImpl(key);
}
