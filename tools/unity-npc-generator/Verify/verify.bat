@echo off
rem Verify Runtime/ (rule-based generator, prompt assembler) without Unity.
rem Needs: the C# compiler bundled with Windows (.NET Framework 4.x) and Python 3.10+.
rem   PowerShell / cmd : .\verify.bat
rem   Git Bash         : ./verify.bat
rem Real model run (LM Studio server on): verify.bat --live [url] [model]
setlocal
cd /d "%~dp0"
set PYTHONIOENCODING=utf-8
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo C# compiler not found: %CSC%
    exit /b 2
)
if not exist bin mkdir bin

echo [1/3] expected prompts from run.py
python dump_expected_prompts.py ..\..\npc-prompt-test bin\expected_prompts.json
if errorlevel 1 exit /b 1

echo [2/3] compile (C# 5)
"%CSC%" -nologo -codepage:65001 -langversion:5 -r:System.Web.Extensions.dll -r:System.Net.Http.dll -out:bin\verify.exe Program.cs ..\Runtime\*.cs
if errorlevel 1 exit /b 1

echo [3/3] verify
bin\verify.exe .. ..\..\npc-prompt-test\data bin\expected_prompts.json %*
exit /b %errorlevel%
