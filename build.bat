@echo off
setlocal enabledelayedexpansion

echo ============================================
echo  ColorCop - .NET WinForms Build Script
echo ============================================

REM Find the latest csc.exe (C# 5 / .NET 4.8 compiler)
set CSC=
set FRAMEWORK_DIR=%windir%\Microsoft.NET\Framework\v4.0.30319
if exist "%FRAMEWORK_DIR%\csc.exe" (
    set CSC="%FRAMEWORK_DIR%\csc.exe"
) else (
    set FRAMEWORK_DIR=%windir%\Microsoft.NET\Framework64\v4.0.30319
    if exist "%FRAMEWORK_DIR%\csc.exe" (
        set CSC="%FRAMEWORK_DIR%\csc.exe"
    )
)

if "%CSC%"=="" (
    echo Error: C# compiler (csc.exe) not found. Please install .NET Framework 4.x SDK.
    exit /b 1
)

echo Using compiler: %CSC%

REM Source files
set SOURCES=
set SOURCES=%SOURCES% ColorCop.cs
REM References
set REFERENCES=
set REFERENCES=%REFERENCES% /reference:System.dll
set REFERENCES=%REFERENCES% /reference:System.Core.dll
set REFERENCES=%REFERENCES% /reference:System.Drawing.dll
set REFERENCES=%REFERENCES% /reference:System.Windows.Forms.dll
set REFERENCES=%REFERENCES% /reference:System.Xml.dll

REM Build
echo.
echo Compiling...
%CSC% /nologo /target:winexe /platform:anycpu /langversion:5 ^
    /out:ColorCop.exe ^
    %REFERENCES% ^
    %SOURCES%

if %ERRORLEVEL% equ 0 (
    echo.
    echo Build successful! Output: ColorCop.exe
    echo.
    dir ColorCop.exe
) else (
    echo.
    echo Build failed with error code %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)

endlocal
