@echo off
rem Starts Speech Cruiser for the next participant: a new career from the tutorial, full screen.
rem Settings (microphone, keys, volume) are kept. Put this file next to SpeechCruiser.exe.
cd /d "%~dp0"
rem Forget a remembered window size, so full screen uses the screen's own resolution.
"%SystemRoot%\System32\reg.exe" delete "HKCU\Software\DefaultCompany\SpeechCruiser" /v "Screenmanager Resolution Width_h182942802" /f >nul 2>&1
"%SystemRoot%\System32\reg.exe" delete "HKCU\Software\DefaultCompany\SpeechCruiser" /v "Screenmanager Resolution Height_h2627697771" /f >nul 2>&1
start "" SpeechCruiser.exe -fresh -screen-fullscreen 1 -window-mode borderless
