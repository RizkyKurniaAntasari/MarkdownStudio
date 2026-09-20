@echo off
rem Rebuilds MarkdownStudio.exe from MarkdownStudio.cs, with the current
rem index.html baked in. Needs nothing installed - csc.exe ships with Windows.
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find the C# compiler that ships with .NET Framework.
  exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ ^
  /out:MarkdownStudio.exe ^
  /win32icon:icon.ico ^
  /resource:index.html,app.html ^
  /reference:System.dll ^
  /reference:System.Management.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  MarkdownStudio.cs

if errorlevel 1 (
  echo Build FAILED.
  exit /b 1
)
echo Built MarkdownStudio.exe
endlocal
