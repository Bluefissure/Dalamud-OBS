@echo off
echo Building OBSPlugin with dotnet CLI...
cd /d "%~dp0"
dotnet clean OBSPlugin.csproj -c %1
dotnet build OBSPlugin.csproj -c %1 -p:Platform=x64
echo Build complete. Check bin\%1 for output files.