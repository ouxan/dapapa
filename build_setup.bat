@echo off
setlocal
cd /d "%~dp0"
set "DAPAPA_DOTNET=dotnet"
if exist "%LOCALAPPDATA%\dapapa-build\sdk\dotnet.exe" set "DAPAPA_DOTNET=%LOCALAPPDATA%\dapapa-build\sdk\dotnet.exe"

rem Validate and clean only the release output under this project.
powershell -NoProfile -Command "$root = [IO.Path]::GetFullPath((Get-Location).Path); $target = [IO.Path]::GetFullPath((Join-Path $root 'artifacts\release')); if (!$target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid release path' }; if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop }"
if not "%errorlevel%"=="0" exit /b %errorlevel%

"%DAPAPA_DOTNET%" run --project Tests\dapapa.Tests.csproj -c Release
if not "%errorlevel%"=="0" exit /b %errorlevel%
"%DAPAPA_DOTNET%" run --project Tests\Ui\dapapa.UiTests.csproj -c Release
if not "%errorlevel%"=="0" exit /b %errorlevel%
"%DAPAPA_DOTNET%" publish dapapa.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o artifacts\release
if not "%errorlevel%"=="0" exit /b %errorlevel%

set "DAPAPA_ISCC="
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "DAPAPA_ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "DAPAPA_ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "DAPAPA_ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined DAPAPA_ISCC (
    echo Release files: artifacts\release. Install Inno Setup 6 to build the installer.
    exit /b 1
)
"%DAPAPA_ISCC%" installer.iss
if not "%errorlevel%"=="0" exit /b %errorlevel%

echo Installer: setup_output\dapapa_Setup.exe
exit /b 0
