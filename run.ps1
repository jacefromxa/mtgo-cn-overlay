# MTGO CN Overlay 快捷启动脚本
Set-Location $PSScriptRoot

$env:DOTNET_ROOT = "$env:LocalAppData\Microsoft\dotnet"
$env:PATH = "$env:LocalAppData\Microsoft\dotnet;$env:PATH"

$exePath = Join-Path $PSScriptRoot "src\MTGOOverlay\bin\Debug\net8.0-windows10.0.19041.0\MTGOOverlay.exe"

if (Test-Path $exePath) {
    Write-Host "[MTGO CN Overlay] 启动应用程序..." -ForegroundColor Green
    Start-Process $exePath
} else {
    Write-Host "[MTGO CN Overlay] 正在编译并启动..." -ForegroundColor Cyan
    & "$env:LocalAppData\Microsoft\dotnet\dotnet.exe" run --project src\MTGOOverlay\MTGOOverlay.csproj
}
