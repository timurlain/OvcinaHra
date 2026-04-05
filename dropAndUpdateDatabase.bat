@echo off
echo ========================================
echo   OvcinaHra - Drop and Recreate Database
echo ========================================

:: Kill API process if running
echo Stopping API...
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5280 ^| findstr LISTENING 2^>nul') do taskkill /F /PID %%a >nul 2>&1

echo Dropping database...
dotnet ef database drop --project src/OvcinaHra.Api --force
if errorlevel 1 (
    echo DROP FAILED
    pause
    exit /b 1
)

echo Applying migrations...
dotnet ef database update --project src/OvcinaHra.Api
if errorlevel 1 (
    echo MIGRATION FAILED
    pause
    exit /b 1
)

echo Database recreated successfully.
