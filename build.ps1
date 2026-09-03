# Build Win32 DLL + WinForms app (Release).
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

python "$PSScriptRoot\tools\pack_field_tools.py"
python "$PSScriptRoot\tools\build_field_tools.py"

cmake -S . -B build -A Win32
cmake --build build --config Release
dotnet build "$PSScriptRoot\modloader\GrandiaModloader.csproj" -c Release

Write-Host "Run: $PSScriptRoot\modloader\bin\Release\net8.0-windows\GrandiaModloader.exe"
