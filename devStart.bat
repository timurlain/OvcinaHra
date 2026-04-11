@echo off
echo ========================================
echo   OvcinaHra - Starting Dev Environment
echo ========================================

:: Kill existing processes on ports 5280 (API) and 5290 (Client)
echo Stopping existing processes...
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5280 ^| findstr LISTENING 2^>nul') do taskkill /F /PID %%a >nul 2>&1
for /f "tokens=5" %%a in ('netstat -ano ^| findstr :5290 ^| findstr LISTENING 2^>nul') do taskkill /F /PID %%a >nul 2>&1

:: Start Docker services (postgres + azurite)
echo Starting Docker services (postgres, azurite)...
docker compose up -d
if errorlevel 1 (
    echo DOCKER FAILED - is Docker Desktop running?
    pause
    exit /b 1
)
:: Wait for postgres + azurite to be ready
echo Waiting for services...
timeout /t 3 /nobreak >nul

:: Ensure blob container exists
az storage container create --name ovcinahra-images --connection-string "UseDevelopmentStorage=true" >nul 2>&1

:: Build solution
echo Building solution...
dotnet build OvcinaHra.slnx
if errorlevel 1 (
    echo BUILD FAILED
    pause
    exit /b 1
)

:: Start API
echo Starting API on port 5280...
start "OvcinaHra API" cmd /c "dotnet run --project src/OvcinaHra.Api --urls https://localhost:5280"

:: Wait for API to start
timeout /t 3 /nobreak >nul

:: Start Client
echo Starting Client on port 5290...
start "OvcinaHra Client" cmd /c "dotnet run --project src/OvcinaHra.Client --urls https://localhost:5290"

:: Wait and open browser
timeout /t 5 /nobreak >nul
echo Opening browser...
start chrome https://localhost:5290

echo ========================================
echo   Docker:  postgres:5434, azurite:10000
echo   API:     https://localhost:5280
echo   Client:  https://localhost:5290
echo   OpenAPI: https://localhost:5280/openapi/v1.json
echo ========================================
