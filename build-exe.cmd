@echo off
rem Rebuilds MarkdownStudio.exe from MarkdownStudio.cs, with the current
rem index.html baked in. Needs nothing installed - csc.exe ships with Windows.
rem From PowerShell run it as  .\build-exe.cmd
rem
rem Safe to run while Markdown Studio is open. Windows refuses to overwrite a
rem running exe, which used to fail the whole build, but it does allow renaming
rem one - so the old exe is moved aside and the open window keeps running from
rem it. That copy is deleted by the next build, once the app has been closed.
setlocal
cd /d "%~dp0"

if not exist index.html (
  echo index.html was not found next to this script.
  exit /b 1
)

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find the C# compiler that ships with .NET Framework.
  exit /b 1
)

rem leftovers from earlier builds; any still in use just stay until next time
del /q MarkdownStudio.old*.exe >nul 2>&1
del /q MarkdownStudio.new.exe >nul 2>&1

rem compile to a side file first, so a failed build never touches the working exe
"%CSC%" /nologo /target:winexe /optimize+ ^
  /out:MarkdownStudio.new.exe ^
  /win32icon:icon.ico ^
  /resource:index.html,app.html ^
  /reference:System.dll ^
  /reference:System.Management.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  MarkdownStudio.cs
if errorlevel 1 goto failed

set OLD=
if not exist MarkdownStudio.exe goto place
rem the .old name is still held when a window from two builds ago is open
set OLD=MarkdownStudio.old.exe
if exist %OLD% set OLD=MarkdownStudio.old-%RANDOM%%RANDOM%.exe
ren MarkdownStudio.exe %OLD% >nul 2>&1
if errorlevel 1 (
  echo MarkdownStudio.exe is locked and could not be replaced. Close Markdown Studio and build again.
  del /q MarkdownStudio.new.exe >nul 2>&1
  exit /b 1
)

:place
move /y MarkdownStudio.new.exe MarkdownStudio.exe >nul
if errorlevel 1 (
  echo Could not put the new MarkdownStudio.exe in place.
  if defined OLD ren %OLD% MarkdownStudio.exe >nul 2>&1
  exit /b 1
)
rem gone straight away unless a window is still running from it
if defined OLD del /q %OLD% >nul 2>&1
echo Built MarkdownStudio.exe
if defined OLD if exist %OLD% echo Markdown Studio is still open on the previous build - restart it to use the new one.
endlocal
exit /b 0

:failed
del /q MarkdownStudio.new.exe >nul 2>&1
echo Build FAILED.
exit /b 1
