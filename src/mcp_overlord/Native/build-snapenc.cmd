@echo off
setlocal
set "ROOT=%~dp0"
set "OUT=%ROOT%win-x64"
if not exist "%OUT%" mkdir "%OUT%"

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
  echo snapenc: vswhere was not found. The screenshot encoder DLL was not built.
  exit /b 0
)

rem The vswhere path contains "Program Files (x86)". A for /f in (`...`) loop
rem treats that closing parenthesis as the end of the loop, so cmd runs "C:\Program".
set "VSINSTALL="
set "VSWHERE_OUT=%TEMP%\snapenc-vswhere.txt"
"%VSWHERE%" -latest -products "*" -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath > "%VSWHERE_OUT%"
for /f "usebackq tokens=*" %%i in ("%VSWHERE_OUT%") do set "VSINSTALL=%%i"
del "%VSWHERE_OUT%" >nul 2>&1
if not defined VSINSTALL (
  echo snapenc: the Visual C++ toolset was not found. The screenshot encoder DLL was not built.
  exit /b 0
)

rem vcvars64 can return 1 after it has initialized the compiler environment.
call "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat" >nul

rem The linker writes snapenc.exp beside the DLL. On the Windows Sandbox share
rem that export file comes back corrupt, so compile on a local disk and copy the DLL.
del /q "%OUT%\*.obj" "%OUT%\*.exp" "%OUT%\*.lib" "%OUT%\*.ilk" "%OUT%\*.pdb" "%OUT%\snapenc.dll" >nul 2>&1
set "WORK=%TEMP%\snapenc-build"
if exist "%WORK%" rmdir /s /q "%WORK%"
mkdir "%WORK%"
echo snapenc: compiling in %WORK%

pushd "%WORK%"
cl /nologo /O2 /LD /EHsc /DNDEBUG /DFPNG_NO_STDIO /std:c++17 "%ROOT%snap.cpp" "%ROOT%fpng.cpp" /Fesnapenc.dll
set "BUILD_ERROR=%ERRORLEVEL%"
popd
if not "%BUILD_ERROR%"=="0" (
  rmdir /s /q "%WORK%"
  exit /b %BUILD_ERROR%
)

copy /y "%WORK%\snapenc.dll" "%OUT%\snapenc.dll" >nul
set "COPY_ERROR=%ERRORLEVEL%"
rmdir /s /q "%WORK%"
if not "%COPY_ERROR%"=="0" (
  echo snapenc: could not copy snapenc.dll to "%OUT%".
  exit /b %COPY_ERROR%
)

del /q "%OUT%\*.obj" "%OUT%\*.exp" "%OUT%\*.lib" "%OUT%\*.ilk" "%OUT%\*.pdb" >nul 2>&1
exit /b 0
