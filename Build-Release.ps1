param(
    [string]$KeystorePath = "$PSScriptRoot\stockmate-release.keystore",
    [string]$Alias = "stockmate",
    [switch]$SkipClean
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "StockMate\StockMate.csproj"
$output = Join-Path $PSScriptRoot "artifacts\release"

if (-not (Test-Path $project)) { throw "StockMate.csproj tidak ditemukan." }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw ".NET SDK belum terpasang." }
if (-not (Get-Command keytool -ErrorAction SilentlyContinue)) { throw "keytool tidak ditemukan. Install JDK 17 atau gunakan terminal Visual Studio." }

$sdk = (& dotnet --version).Trim()
if (-not $sdk.StartsWith("10.")) { throw ".NET SDK 10 diperlukan. Aktif: $sdk" }

function Read-PlainPassword([string]$Prompt) {
    $secure = Read-Host $Prompt -AsSecureString
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

if (-not (Test-Path $KeystorePath)) {
    Write-Host "Keystore release belum ada. Membuat keystore baru..." -ForegroundColor Yellow
    $password = Read-PlainPassword "Buat password keystore (minimal 6 karakter)"
    if ($password.Length -lt 6) { throw "Password minimal 6 karakter." }
    & keytool -genkeypair -v -keystore $KeystorePath -alias $Alias -keyalg RSA -keysize 2048 -validity 10000 -storepass $password -keypass $password -dname "CN=StockMate, OU=Personal, O=StockMate, L=Jakarta, ST=Jakarta, C=ID"
    if ($LASTEXITCODE -ne 0) { throw "Gagal membuat keystore." }
} else {
    $password = Read-PlainPassword "Password keystore release"
}

if (-not $SkipClean) {
    Remove-Item (Join-Path $PSScriptRoot "StockMate\bin") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $PSScriptRoot "StockMate\obj") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path $output | Out-Null

& dotnet workload restore $project
if ($LASTEXITCODE -ne 0) { throw "Workload Android gagal dipulihkan." }
& dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "Restore gagal." }

& dotnet publish $project -f net10.0-android -c Release -o $output --no-restore `
    -p:AndroidPackageFormats=apk `
    -p:AndroidKeyStore=true `
    -p:AndroidSigningKeyStore="$KeystorePath" `
    -p:AndroidSigningKeyAlias="$Alias" `
    -p:AndroidSigningKeyPass="$password" `
    -p:AndroidSigningStorePass="$password"
if ($LASTEXITCODE -ne 0) { throw "Publish Release gagal." }

$apk = Get-ChildItem $output -Filter "*-Signed.apk" -Recurse | Select-Object -First 1
if (-not $apk) { $apk = Get-ChildItem $output -Filter "*.apk" -Recurse | Select-Object -First 1 }
if (-not $apk) { throw "Build selesai tetapi APK tidak ditemukan." }

[xml]$projectXml = Get-Content $project
$displayVersion = $projectXml.Project.PropertyGroup.ApplicationDisplayVersion |
    Where-Object { $_ } | Select-Object -First 1
$final = Join-Path $output "StockMate-v$displayVersion-release.apk"
Copy-Item $apk.FullName $final -Force

Write-Host ""
Write-Host "APK Release installable berhasil dibuat:" -ForegroundColor Green
Write-Host $final -ForegroundColor Green
Write-Host "Simpan keystore dan password. APK update berikutnya wajib ditandatangani dengan keystore yang sama." -ForegroundColor Yellow
