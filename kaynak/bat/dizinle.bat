@echo off
chcp 65001 >nul
title Veri Firini
set "ROOT=%~dp0"
set "HF_HUB_OFFLINE=1"
set "TRANSFORMERS_OFFLINE=1"
set "HF_HUB_DISABLE_TELEMETRY=1"
set "PYTHONNOUSERSITE=1"
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
set "VIDEOHAT_KOK=%ROOT%"
set "PATH=%ROOT%ffmpeg\bin;%ROOT%cuda;%ROOT%python;%SystemRoot%\System32;%SystemRoot%"

"%ROOT%python\python.exe" -m videohat dizinle %*
exit /b %ERRORLEVEL%
