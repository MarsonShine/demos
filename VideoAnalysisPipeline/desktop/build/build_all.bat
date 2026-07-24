@echo off
REM ============================================================
REM VideoAnalysisDesktop release build
REM ============================================================
REM The generated engine is a release artifact, not source control.  This
REM script stages it before Inno Setup packages one offline installer.
REM
REM Offline example:
REM   build_all.bat --python-zip C:\artifacts\python-3.12.10-embed-amd64.zip ^
REM     --wheelhouse C:\artifacts\wheels-win_amd64-cp312 ^
REM     --models-dir C:\artifacts\models --ffmpeg-dir C:\artifacts\ffmpeg
REM
REM Connected build-machine example:
REM   build_all.bat --allow-network --ffmpeg-dir C:\tools\ffmpeg\bin
REM
REM Options:
REM   --skip-installer       Build/test only; do not invoke Inno Setup.
REM   --skip-models          Development-only engine. Requires --skip-installer.
REM   --clean-engine         Explicitly replace an existing desktop\engine.
REM   --allow-network        Explicitly permit Python/dependency/model downloads.
REM ============================================================

setlocal EnableExtensions DisableDelayedExpansion
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..") do set "DESKTOP_DIR=%%~fI"
for %%I in ("%DESKTOP_DIR%\..") do set "REPO_DIR=%%~fI"

set "ENGINE_DIR=%DESKTOP_DIR%\engine"
set "APP_DIR=%DESKTOP_DIR%\VideoAnalysisDesktop.App"
set "TESTS_DIR=%DESKTOP_DIR%\VideoAnalysisDesktop.Tests"
set "INSTALLER_SCRIPT=%DESKTOP_DIR%\installer\installer.iss"
set "INSTALLER_DIR=%DESKTOP_DIR%\installer\output"

set "SKIP_INSTALLER=0"
set "SKIP_MODELS=0"
set "CLEAN_ENGINE=0"
set "ALLOW_NETWORK=0"
set "FFMPEG_DIR="
set "PYTHON_ZIP="
set "WHEELHOUSE="
set "MODELS_DIR="

:parse_args
if "%~1"=="" goto :done_parsing
if /I "%~1"=="--skip-installer" (
    set "SKIP_INSTALLER=1"
    shift
    goto :parse_args
)
if /I "%~1"=="--skip-models" (
    set "SKIP_MODELS=1"
    shift
    goto :parse_args
)
if /I "%~1"=="--clean-engine" (
    set "CLEAN_ENGINE=1"
    shift
    goto :parse_args
)
if /I "%~1"=="--allow-network" (
    set "ALLOW_NETWORK=1"
    shift
    goto :parse_args
)
if /I "%~1"=="--ffmpeg-dir" (
    if "%~2"=="" goto :missing_value
    set "FFMPEG_DIR=%~2"
    shift
    shift
    goto :parse_args
)
if /I "%~1"=="--python-zip" (
    if "%~2"=="" goto :missing_value
    set "PYTHON_ZIP=%~2"
    shift
    shift
    goto :parse_args
)
if /I "%~1"=="--wheelhouse" (
    if "%~2"=="" goto :missing_value
    set "WHEELHOUSE=%~2"
    shift
    shift
    goto :parse_args
)
if /I "%~1"=="--models-dir" (
    if "%~2"=="" goto :missing_value
    set "MODELS_DIR=%~2"
    shift
    shift
    goto :parse_args
)
echo ERROR: Unknown argument: %~1
exit /b 2

:missing_value
echo ERROR: %~1 requires a value.
exit /b 2

:done_parsing
if "%SKIP_MODELS%"=="1" if not "%SKIP_INSTALLER%"=="1" (
    echo ERROR: --skip-models creates a development-only engine and requires --skip-installer.
    exit /b 2
)
if "%SKIP_MODELS%"=="1" if not "%MODELS_DIR%"=="" (
    echo ERROR: --skip-models cannot be combined with --models-dir.
    exit /b 2
)
where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK was not found on PATH.
    exit /b 1
)
where python >nul 2>&1
if errorlevel 1 (
    echo ERROR: A build-machine Python executable was not found on PATH.
    exit /b 1
)

echo.
echo ========================================
echo  VideoAnalysisDesktop Release Build
echo ========================================
echo.

REM ---- Step 1: C# tests must build and pass ----
echo [1/6] Running C# unit tests...
dotnet test "%TESTS_DIR%\VideoAnalysisDesktop.Tests.csproj" -c Release --nologo -v minimal
if errorlevel 1 (
    echo ERROR: C# unit tests failed.
    exit /b 1
)
echo   C# tests passed

REM ---- Step 2: Publish the self-contained WPF application ----
echo [2/6] Publishing WPF application...
dotnet publish "%APP_DIR%\VideoAnalysisDesktop.App.csproj" ^
    -r win-x64 ^
    --self-contained true ^
    -c Release ^
    -o "%APP_DIR%\bin\Release\net10.0-windows\win-x64\publish"
if errorlevel 1 (
    echo ERROR: WPF publish failed.
    exit /b 1
)
echo   WPF publish passed

REM ---- Step 3: Stage the portable Python engine ----
echo [3/6] Staging Python engine...
set "STAGE_ARGS=--output-dir "%ENGINE_DIR%""
if "%CLEAN_ENGINE%"=="1" set "STAGE_ARGS=%STAGE_ARGS% --clean-output"
if not "%FFMPEG_DIR%"=="" set "STAGE_ARGS=%STAGE_ARGS% --ffmpeg-dir "%FFMPEG_DIR%""
if not "%PYTHON_ZIP%"=="" (
    set "STAGE_ARGS=%STAGE_ARGS% --python-zip "%PYTHON_ZIP%""
) else if "%ALLOW_NETWORK%"=="1" (
    set "STAGE_ARGS=%STAGE_ARGS% --allow-python-download"
) else (
    echo ERROR: Supply --python-zip for an offline build, or pass --allow-network.
    exit /b 2
)
if not "%WHEELHOUSE%"=="" (
    set "STAGE_ARGS=%STAGE_ARGS% --wheelhouse "%WHEELHOUSE%""
) else if "%ALLOW_NETWORK%"=="1" (
    set "STAGE_ARGS=%STAGE_ARGS% --allow-dependency-download"
) else (
    echo ERROR: Supply --wheelhouse for an offline build, or pass --allow-network.
    exit /b 2
)
if "%SKIP_MODELS%"=="1" (
    set "STAGE_ARGS=%STAGE_ARGS% --skip-models"
) else if not "%MODELS_DIR%"=="" (
    set "STAGE_ARGS=%STAGE_ARGS% --models-dir "%MODELS_DIR%""
) else if "%ALLOW_NETWORK%"=="1" (
    set "STAGE_ARGS=%STAGE_ARGS% --allow-model-download"
) else (
    echo ERROR: Supply --models-dir for an offline build, or pass --allow-network.
    exit /b 2
)

python "%SCRIPT_DIR%stage_engine.py" %STAGE_ARGS%
if errorlevel 1 (
    echo ERROR: Python engine staging failed.
    exit /b 1
)
set "PYTHON_EXE=%ENGINE_DIR%\python\python.exe"
if not exist "%PYTHON_EXE%" (
    echo ERROR: Staging reported success but %PYTHON_EXE% is missing.
    exit /b 1
)
echo   Python engine staged

REM ---- Step 4: Run all Python tests with the standard-library runner ----
REM Desktop contract tests are unittest.TestCase classes, so the production
REM engine does not need pytest merely to validate its own source tree.
echo [4/6] Running Python unit tests...
pushd "%REPO_DIR%"
"%PYTHON_EXE%" -m unittest discover -s "%REPO_DIR%\tests" -p "test_*.py" -v
if errorlevel 1 (
    popd
    echo ERROR: Python unittest suite failed.
    exit /b 1
)
popd
echo   Python tests passed

REM ---- Step 5: Verify the artifact that will be installed ----
echo [5/6] Running engine self-check...
set "MODEL_CHECK_ARG=--require-models"
if "%SKIP_MODELS%"=="1" set "MODEL_CHECK_ARG="
"%PYTHON_EXE%" "%ENGINE_DIR%\engine_check.py" --engine-dir "%ENGINE_DIR%" --verify-hashes %MODEL_CHECK_ARG%
if errorlevel 1 (
    echo ERROR: Engine self-check failed.
    exit /b 1
)
echo   Engine self-check passed

REM ---- Step 6: Compile the installer ----
if "%SKIP_INSTALLER%"=="1" (
    echo [6/6] Installer build skipped by request.
    goto :done
)

echo [6/6] Building offline installer...
where iscc >nul 2>&1
if errorlevel 1 (
    echo ERROR: Inno Setup Compiler ^(iscc^) was not found on PATH.
    echo        Install Inno Setup 6, then rerun this release build.
    exit /b 1
)
if not exist "%ENGINE_DIR%\engine.manifest.json" (
    echo ERROR: Engine manifest is missing: %ENGINE_DIR%\engine.manifest.json
    exit /b 1
)
iscc "%INSTALLER_SCRIPT%"
if errorlevel 1 (
    echo ERROR: Installer build failed.
    exit /b 1
)
echo   Installer built to: %INSTALLER_DIR%

:done
echo.
echo ========================================
echo  Build completed successfully
echo ========================================
echo   WPF app:   %APP_DIR%\bin\Release\net10.0-windows\win-x64\publish
echo   Engine:    %ENGINE_DIR%
if "%SKIP_INSTALLER%"=="0" echo   Installer: %INSTALLER_DIR%
exit /b 0
