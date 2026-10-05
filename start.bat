@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo [MTGO CN Overlay] 正在启动程序...

set "DOTNET_ROOT=%LocalAppData%\Microsoft\dotnet"
set "PATH=%LocalAppData%\Microsoft\dotnet;%PATH%"

set "EXE_PATH=%~dp0src\MTGOOverlay\bin\Debug\net8.0-windows10.0.19041.0\MTGOOverlay.exe"

if exist "%EXE_PATH%" (
    echo [MTGO CN Overlay] 启动已编译程序: %EXE_PATH%
    start "" "%EXE_PATH%"
) else (
    echo [MTGO CN Overlay] 首次运行，正在构建并启动...
    "%LocalAppData%\Microsoft\dotnet\dotnet.exe" run --project src\MTGOOverlay\MTGOOverlay.csproj
)
