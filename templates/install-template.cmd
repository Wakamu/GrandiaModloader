@echo off
setlocal
set "ROOT=%~dp0"

echo Installing Grandia Mod template for Visual Studio, Rider, and dotnet new...
dotnet new uninstall Grandia.Mod.CSharp >nul 2>&1
dotnet new install "%ROOT%GrandiaMod"
if errorlevel 1 (
  echo Failed. Install the .NET 8 SDK and try again.
  exit /b 1
)

set "ICON="
if exist "%ROOT%..\app.ico" set "ICON=%ROOT%..\app.ico"
if exist "%ROOT%..\modloader\app.ico" set "ICON=%ROOT%..\modloader\app.ico"
if defined ICON copy /y "%ICON%" "%ROOT%GrandiaMod\__TemplateIcon.ico" >nul

call :CopyVs 2022
call :CopyVs 2026

echo.
echo Visual Studio: File - New - Project, search "Grandia Mod"
echo Rider: New Solution, search "Grandia Mod"
echo CLI: dotnet new grandiamod -n MyMod -o MyMod
exit /b 0

:CopyVs
set "DEST=%USERPROFILE%\Documents\Visual Studio %~1\Templates\ProjectTemplates"
if not exist "%DEST%" exit /b 0
powershell -NoProfile -Command "Compress-Archive -Path '%ROOT%GrandiaMod\*' -DestinationPath '%DEST%\GrandiaMod.zip' -Force"
echo Copied GrandiaMod.zip to Visual Studio %~1 project templates.
exit /b 0
