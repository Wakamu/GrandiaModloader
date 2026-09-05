cmake_minimum_required(VERSION 3.20)
project(GrandiaMod LANGUAGES CXX)

# Grandia HD Remaster is a 32-bit native port (grandia.exe). Build Win32 only.
if(NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
    message(WARNING "Configure with -A Win32 (32-bit). Grandia is not a 64-bit process.")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(GrandiaMod SHARED
    src/dllmain.cpp
    src/log.cpp
    src/overlay.cpp
    src/virt_file.cpp
    src/map_apply.cpp
    src/script_redirect.cpp
    src/dialogue.cpp
    src/setup.cpp
    src/clr_host.cpp
    src/hook_util.cpp
    src/events.cpp
    src/game.cpp
    src/travel.cpp
    src/wm_picture.cpp
    src/save.cpp
    src/party.cpp
    src/party_pdat.cpp
    src/qol.cpp
    src/d3d_hud.cpp
    src/title.cpp
    src/catalog.cpp
    src/movie_skip.cpp
)

target_include_directories(GrandiaMod PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src)

if(MSVC)
    target_compile_options(GrandiaMod PRIVATE /W4 /utf-8)
    set_property(TARGET GrandiaMod PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

set_target_properties(GrandiaMod PROPERTIES OUTPUT_NAME "GrandiaMod")

# Stage next to the WinForms app so Launch can find the DLL (Debug and Release).
set(_APP_DEBUG "${CMAKE_SOURCE_DIR}/modloader/bin/Debug/net8.0-windows")
set(_APP_RELEASE "${CMAKE_SOURCE_DIR}/modloader/bin/Release/net8.0-windows")
set(_DIST "${CMAKE_SOURCE_DIR}/dist")
set(_DOTNET_CFG "$<IF:$<CONFIG:Debug>,Debug,Release>")
set(_RUNTIME_OUT "${CMAKE_SOURCE_DIR}/runtime/bin/${_DOTNET_CFG}/net8.0")
add_custom_target(GrandiaRuntime ALL
    COMMAND dotnet build "${CMAKE_SOURCE_DIR}/runtime/Grandia.Runtime.csproj" -c ${_DOTNET_CFG} -p:PlatformTarget=x86
    COMMENT "Build Grandia.Runtime (x86 net8)"
)
add_dependencies(GrandiaMod GrandiaRuntime)

add_custom_command(TARGET GrandiaMod POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory "${_APP_DEBUG}"
    COMMAND ${CMAKE_COMMAND} -E make_directory "${_APP_RELEASE}"
    COMMAND ${CMAKE_COMMAND} -E make_directory "${_DIST}"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "$<TARGET_FILE:GrandiaMod>" "${_APP_DEBUG}/GrandiaMod.dll"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "$<TARGET_FILE:GrandiaMod>" "${_APP_RELEASE}/GrandiaMod.dll"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "$<TARGET_FILE:GrandiaMod>" "${_DIST}/GrandiaMod.dll"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${_RUNTIME_OUT}/Grandia.Runtime.dll" "${_APP_DEBUG}/Grandia.Runtime.dll"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${_RUNTIME_OUT}/Grandia.Runtime.dll" "${_APP_RELEASE}/Grandia.Runtime.dll"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${_RUNTIME_OUT}/Grandia.Runtime.dll" "${_DIST}/Grandia.Runtime.dll"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${_RUNTIME_OUT}/Grandia.Runtime.runtimeconfig.json" "${_APP_DEBUG}/Grandia.Runtime.runtimeconfig.json"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${_RUNTIME_OUT}/Grandia.Runtime.runtimeconfig.json" "${_APP_RELEASE}/Grandia.Runtime.runtimeconfig.json"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${_RUNTIME_OUT}/Grandia.Runtime.runtimeconfig.json" "${_DIST}/Grandia.Runtime.runtimeconfig.json"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${_RUNTIME_OUT}/Grandia.Runtime.deps.json" "${_APP_DEBUG}/Grandia.Runtime.deps.json"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${_RUNTIME_OUT}/Grandia.Runtime.deps.json" "${_APP_RELEASE}/Grandia.Runtime.deps.json"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${_RUNTIME_OUT}/Grandia.Runtime.deps.json" "${_DIST}/Grandia.Runtime.deps.json"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${_RUNTIME_OUT}/Grandia.Sdk.dll" "${_APP_DEBUG}/Grandia.Sdk.dll"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${_RUNTIME_OUT}/Grandia.Sdk.dll" "${_APP_RELEASE}/Grandia.Sdk.dll"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${_RUNTIME_OUT}/Grandia.Sdk.dll" "${_DIST}/Grandia.Sdk.dll"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${CMAKE_SOURCE_DIR}/sdk/Grandia.Mod.targets" "${_APP_DEBUG}/Grandia.Mod.targets"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${CMAKE_SOURCE_DIR}/sdk/Grandia.Mod.targets" "${_APP_RELEASE}/Grandia.Mod.targets"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${CMAKE_SOURCE_DIR}/sdk/Grandia.Mod.targets" "${_DIST}/Grandia.Mod.targets"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${CMAKE_SOURCE_DIR}/data/pdat_charpack.bin" "${_APP_DEBUG}/pdat_charpack.bin"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${CMAKE_SOURCE_DIR}/data/pdat_charpack.bin" "${_APP_RELEASE}/pdat_charpack.bin"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "${CMAKE_SOURCE_DIR}/data/pdat_charpack.bin" "${_DIST}/pdat_charpack.bin"
    COMMAND ${CMAKE_COMMAND} -E copy_directory "${CMAKE_SOURCE_DIR}/vendor/field_tools" "${_APP_DEBUG}/field_tools"
    COMMAND ${CMAKE_COMMAND} -E copy_directory "${CMAKE_SOURCE_DIR}/vendor/field_tools" "${_APP_RELEASE}/field_tools"
    COMMAND ${CMAKE_COMMAND} -E copy_directory "${CMAKE_SOURCE_DIR}/vendor/field_tools" "${_DIST}/field_tools"
    COMMENT "Copy GrandiaMod.dll + CLR runtime + field_tools next to GrandiaModloader / dist"
)
