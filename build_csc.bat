@echo off
setlocal EnableDelayedExpansion

rem Try 64-bit csc first, then 32-bit
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe niet gevonden. Installeer .NET Framework 4.8 Developer Pack of Visual Studio Build Tools.
  pause
  exit /b 1
)

set "REF=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319" set "REF=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"

echo Compileren met: %CSC%
"%CSC%" /nologo /target:winexe /out:Kruisjassen.exe /platform:anycpu ^
  /r:"%REF%\System.dll" /r:"%REF%\System.Windows.Forms.dll" /r:"%REF%\System.Drawing.dll" ^
  Program.cs MainForm.cs

if errorlevel 1 (
  echo Build mislukt.
  pause
  exit /b 1
) else (
  echo Build OK. Starten...
  start "" "%~dp0Kruisjassen.exe"
  exit /b 0
)
