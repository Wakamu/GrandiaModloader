#pragma once

namespace grandia_mod {

void InitializeLogging();
void ShutdownLogging();

void LogInfo(const char* fmt, ...);
void LogWarn(const char* fmt, ...);

}  // namespace grandia_mod
