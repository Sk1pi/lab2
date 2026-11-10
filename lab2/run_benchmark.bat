@echo off
chcp 65001 >nul
cd /d "%~dp0"

pushd src\SportLive.Benchmark
dotnet run -c Release -- --out "%~dp0results" --data "%~dp0data" %*
if errorlevel 1 (
    popd
    echo.
    echo ПОМИЛКА. Зробіть скріншот цього вікна.
    pause
    exit /b 1
)
popd

echo.
echo Готово. Результати в папці results, графіки: results\report.html
pause
