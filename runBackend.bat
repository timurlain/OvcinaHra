@echo off
echo ========================================
echo   OvcinaHra - API Server
echo ========================================

set ASPNETCORE_ENVIRONMENT=Development

echo Building...
dotnet build src/OvcinaHra.Api/OvcinaHra.Api.csproj
if errorlevel 1 (
    echo BUILD FAILED
    pause
    exit /b 1
)

echo Starting API on port 5180...
dotnet run --project src/OvcinaHra.Api --urls https://localhost:5180
