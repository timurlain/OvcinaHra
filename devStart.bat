@echo off
echo ========================================
echo   OvcinaHra - Starting Dev Environment
echo ========================================

:: Kill existing processes on ports 5180 (API) and 5190 (Client)
echo Stopping existing processes...
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5180 ^| findstr LISTENING 2^>nul') do taskkill /F /PID %%a >nul 2>&1
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5190 ^| findstr LISTENING 2^>nul') do taskkill /F /PID %%a >nul 2>&1

:: Build solution
echo Building solution...
dotnet build OvcinaHra.slnx
if errorlevel 1 (
    echo BUILD FAILED
    pause
    exit /b 1
)

:: Start API
echo Starting API on port 5180...
start "OvcinaHra API" cmd /c "dotnet run --project src/OvcinaHra.Api --urls https://localhost:5180"

:: Wait for API to start
timeout /t 3 /nobreak >nul

:: Start Client
echo Starting Client on port 5190...
start "OvcinaHra Client" cmd /c "dotnet run --project src/OvcinaHra.Client --urls https://localhost:5190"

:: Wait and open browser
timeout /t 5 /nobreak >nul
echo Opening browser...
start chrome https://localhost:5190

echo ========================================
echo   API:    https://localhost:5180
echo   Client: https://localhost:5190
echo   OpenAPI: https://localhost:5180/openapi/v1.json
echo ========================================
