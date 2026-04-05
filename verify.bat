@echo off
echo ========================================
echo   OvcinaHra - Verification Pipeline
echo ========================================

echo [1/3] Building (Release)...
dotnet build OvcinaHra.slnx -c Release
if errorlevel 1 (
    echo BUILD FAILED
    exit /b 1
)

echo [2/3] Running API integration tests...
dotnet test tests/OvcinaHra.Api.Tests -c Release --no-build
if errorlevel 1 (
    echo API TESTS FAILED
    exit /b 1
)

echo [3/3] Running E2E tests...
dotnet test tests/OvcinaHra.E2E -c Release --no-build
if errorlevel 1 (
    echo E2E TESTS FAILED
    exit /b 1
)

echo ========================================
echo   ALL CHECKS PASSED
echo ========================================
