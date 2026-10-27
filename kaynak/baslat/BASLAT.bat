@echo off
chcp 65001 >nul
title Video Transkriptör
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0VideoPerisi\araclar\baslat.ps1"
if errorlevel 1 pause
