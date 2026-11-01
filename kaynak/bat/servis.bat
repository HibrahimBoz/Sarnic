@echo off
chcp 65001 >nul
title Sarnic Atolye Servisi
"%~dp0servis\SarnicServis.exe" %*
set "HATA_KODU=%ERRORLEVEL%"
if "%SARNICATOLYE_PAUSE%"=="0" exit /b %HATA_KODU%
pause
exit /b %HATA_KODU%
