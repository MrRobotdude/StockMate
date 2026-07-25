$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot "StockMate\StockMate.csproj"
$binFolder = Join-Path $projectRoot "StockMate\bin"
$objFolder = Join-Path $projectRoot "StockMate\obj"

if (-not (Test-Path $projectFile)) {
    throw "StockMate.csproj tidak ditemukan di: $projectFile"
}

$sdkVersion = (& dotnet --version).Trim()
if (-not $sdkVersion.StartsWith("10.")) {
    throw ".NET SDK 10 diperlukan. SDK aktif saat ini: $sdkVersion"
}

Write-Host "Menyiapkan StockMate dengan .NET SDK $sdkVersion..." -ForegroundColor Cyan

if (Test-Path $binFolder) {
    Remove-Item $binFolder -Recurse -Force
}
if (Test-Path $objFolder) {
    Remove-Item $objFolder -Recurse -Force
}

Write-Host "Memeriksa workload Android..." -ForegroundColor Cyan
& dotnet workload restore $projectFile
if ($LASTEXITCODE -ne 0) {
    throw "Workload Android belum siap. Jalankan: dotnet workload install maui-android"
}

Write-Host "Melakukan restore net10.0-android..." -ForegroundColor Cyan
& dotnet restore $projectFile --force --no-cache
if ($LASTEXITCODE -ne 0) {
    throw "NuGet restore gagal. Periksa output di atas."
}

Write-Host "Memvalidasi build Android..." -ForegroundColor Cyan
& dotnet build $projectFile -f net10.0-android --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "Build Android gagal. Periksa error pertama pada output di atas."
}

Write-Host ""
Write-Host "StockMate siap. Buka StockMate.slnx di Visual Studio dan jalankan target Android." -ForegroundColor Green
