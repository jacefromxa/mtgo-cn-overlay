@echo off
cd /d "%~dp0"
title MTGO CN Overlay

set "EXE_PATH=%~dp0src\MTGOOverlay\bin\Debug\net8.0-windows10.0.19041.0\MTGOOverlay.exe"

if exist "%EXE_PATH%" (
    echo [MTGO CN Overlay] Launching MTGO CN Overlay...
    start "" "%EXE_PATH%"
    exit /b 0
)

echo [MTGO CN Overlay] Compiling and starting...
set "DOTNET_ROOT=%LocalAppData%\Microsoft\dotnet"
set "PATH=%LocalAppData%\Microsoft\dotnet;%PATH%"
"%LocalAppData%\Microsoft\dotnet\dotnet.exe" run --project "%~dp0src\MTGOOverlay\MTGOOverlay.csproj"