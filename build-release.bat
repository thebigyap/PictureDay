@echo off
echo ========================================
echo PictureDay Release Build Script
echo ========================================
echo.

echo Building release...
dotnet build -c Release

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: Build failed!
    pause
    exit /b 1
)

echo.
:VERSION_PROMPT
set /p VERSION="Enter version number (e.g., 1.8.0): "
if "%VERSION%"=="" (
    echo Version is required!
    goto VERSION_PROMPT
)

set RELEASE_DIR=bin\Release\net8.0-windows
set OUTPUT_DIR=Release-Package
set ZIP_NAME=PictureDay-v%VERSION%-Release.zip

echo Version: %VERSION%
echo.

echo Cleaning previous package...
if exist "%OUTPUT_DIR%" rmdir /s /q "%OUTPUT_DIR%"
if exist "%ZIP_NAME%" del /q "%ZIP_NAME%"

echo.
echo Copying required files...
mkdir "%OUTPUT_DIR%"
copy "%RELEASE_DIR%\PictureDay.exe" "%OUTPUT_DIR%\" >nul
copy "%RELEASE_DIR%\PictureDay.dll" "%OUTPUT_DIR%\" >nul
copy "%RELEASE_DIR%\PictureDay.deps.json" "%OUTPUT_DIR%\" >nul
copy "%RELEASE_DIR%\PictureDay.runtimeconfig.json" "%OUTPUT_DIR%\" >nul
copy "%RELEASE_DIR%\Newtonsoft.Json.dll" "%OUTPUT_DIR%\" >nul

echo.
echo Creating ZIP archive...
powershell -Command "Compress-Archive -Path '%OUTPUT_DIR%\*' -DestinationPath '%ZIP_NAME%' -Force"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: Failed to create ZIP file!
    pause
    exit /b 1
)

echo.
echo Cleaning up...
rmdir /s /q "%OUTPUT_DIR%"

echo.
echo ========================================
echo Build completed successfully!
echo ========================================
echo.
echo Release package: %ZIP_NAME%
echo.
echo The ZIP file contains everything needed to run PictureDay.
echo Just extract and run PictureDay.exe
echo.
pause
