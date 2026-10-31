@echo off
chcp 65001 >nul
title Sarnıç Atölye
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0SarnicAtolye\araclar\baslat.ps1"
if errorlevel 1 pause
