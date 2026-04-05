@echo off
echo ========================================
echo   OvcinaHra - Stop All Processes
echo ========================================

echo Stopping API (port 5280)...
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5280 ^| findstr LISTENING 2^>nul') do taskkill /F /PID %%a >nul 2>&1

echo Stopping Client (port 5290)...
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5290 ^| findstr LISTENING 2^>nul') do taskkill /F /PID %%a >nul 2>&1

echo All processes stopped.
