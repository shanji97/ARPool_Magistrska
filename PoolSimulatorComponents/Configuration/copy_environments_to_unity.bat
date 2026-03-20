@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem Source folder = this script folder
set "SRC_DIR=%~dp0"

rem Destination can be passed as first argument. If not set, use default.
set "DEST_DIR=%~1"
if "%DEST_DIR%"=="" set "DEST_DIR=%SRC_DIR%..\PTS_2\Assets\Resources\TableConfigurations"

rem Optional explicit list. Leave empty to copy all *.json files.
rem Example:
rem set "FILES=last_environment.json predator_9ft_virtual_debug.json"
set "FILES="

rem Excluded files (applies for both explicit list and wildcard mode)
set "EXCLUDED_FILES=torch_state.json"

if not exist "%DEST_DIR%" (
    mkdir "%DEST_DIR%"
)

if defined FILES (
    for %%F in (%FILES%) do (
        call :copy_if_allowed "%%~F"
    )
) else (
    for %%F in ("%SRC_DIR%*.json") do (
        call :copy_if_allowed "%%~nxF"
    )
)

echo.
echo Copy finished.
exit /b 0

:copy_if_allowed
set "FILE_NAME=%~1"

for %%E in (%EXCLUDED_FILES%) do (
    if /I "%%~E"=="!FILE_NAME!" (
        echo Skipped ^(excluded^): !FILE_NAME!
        exit /b 0
    )
)

if not exist "%SRC_DIR%!FILE_NAME!" (
    echo Skipped ^(not found^): !FILE_NAME!
    exit /b 0
)

copy /Y "%SRC_DIR%!FILE_NAME!" "%DEST_DIR%\!FILE_NAME!" >nul
if errorlevel 1 (
    echo Failed: !FILE_NAME!
) else (
    echo Copied: !FILE_NAME!
)
exit /b 0
