@echo off
setlocal

set "ROOT=%~dp0"
set "GAME_EXE=%ROOT%Builds\Windows\AnimeFighterPrototype.exe"

if not exist "%GAME_EXE%" (
    echo Anime Fighter Prototype build was not found.
    echo Expected:
    echo   %GAME_EXE%
    echo.
    echo Run the Unity build first, or ask Codex to rebuild the project.
    pause
    exit /b 1
)

start "" "%GAME_EXE%"
exit /b 0
