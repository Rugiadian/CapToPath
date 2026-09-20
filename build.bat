@echo off
chcp 65001 > nul
echo ========================================================
echo  CapToPath 빌드 스크립트 (.NET 10 / Windows)
echo ========================================================
echo.

set ROOT_DIR=%~dp0
cd /d "%ROOT_DIR%src"

echo [1/3] 독립 실행형 단일 exe 빌드 중 (Self-Contained)...
dotnet publish -c Release -o "%ROOT_DIR%dist"
if errorlevel 1 (
    echo [ERROR] 독립 실행형 빌드에 실패했습니다.
    pause
    exit /b 1
)
del "%ROOT_DIR%dist\CapToPath.pdb" > nul 2>&1
copy /y "%ROOT_DIR%dist\CapToPath.exe" "%ROOT_DIR%CapToPath.exe" > nul

echo [2/3] 초경량 단일 exe 빌드 중 (Framework-Dependent, 180KB)...
dotnet publish -c Release -p:SelfContained=false -p:EnableCompressionInSingleFile=false -o "%ROOT_DIR%dist_light"
if errorlevel 1 (
    echo [ERROR] 경량 빌드에 실패했습니다.
    pause
    exit /b 1
)
del "%ROOT_DIR%dist_light\CapToPath.pdb" > nul 2>&1

echo.
echo ========================================================
echo  빌드 완료!
echo  - 루트 실행 파일: CapToPath.exe
echo  - 무설치 독립 배포용: dist\CapToPath.exe
echo  - 런타임 보유 PC용 초경량: dist_light\CapToPath.exe
echo ========================================================
pause
