@echo off
echo ========================================
echo   OvcinaHra - Blazor WASM Client
echo ========================================

echo Starting Client on port 5190...
dotnet run --project src/OvcinaHra.Client --urls https://localhost:5190
