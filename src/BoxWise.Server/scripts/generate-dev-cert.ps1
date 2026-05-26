# BoxWise 开发证书生成脚本
# 用途：为手机测试创建自签名 HTTPS 证书（Root CA + Server 证书）
# 使用：以管理员身份运行 PowerShell，执行此脚本
# 依赖：PowerShell 5.1+ (Windows 10/11 自带)
#
# 生成的文件：
#   certs/boxwise-root-ca.cer  → 安装到手机以信任 HTTPS
#   certs/boxwise-dev.pfx      → Kestrel 使用的服务器证书（已 gitignore）
#   certs/boxwise-dev.pwd      → 密码通过 dotnet user-secrets 存储（不在此脚本中）

param(
    [string]$IpAddress = "192.168.83.183",
    [string]$Password = "boxwise123"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $scriptDir
$certDir = Join-Path $projectDir "certs"

Write-Host "=== BoxWise 开发证书生成 ===" -ForegroundColor Cyan
Write-Host "IP 地址: $IpAddress"
Write-Host "输出目录: $certDir"
Write-Host ""

# 创建输出目录
New-Item -ItemType Directory -Force -Path $certDir | Out-Null

# 1. 查找或创建根 CA
$rootCA = Get-ChildItem -Path "Cert:\CurrentUser\My" |
    Where-Object { $_.Subject -eq "CN=BoxWise Dev Root CA" } |
    Select-Object -First 1

if (-not $rootCA) {
    Write-Host "[1/4] 创建根 CA..." -ForegroundColor Yellow
    $rootCA = New-SelfSignedCertificate `
        -Subject "CN=BoxWise Dev Root CA" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -KeyUsageProperty All `
        -KeyUsage CertSign, CRLSign `
        -TextExtension @("2.5.29.19={text}CA=true&pathlength=0") `
        -NotAfter (Get-Date).AddYears(5)
    Write-Host "  根 CA 已创建: $($rootCA.Thumbprint)" -ForegroundColor Green
}
else {
    Write-Host "[1/4] 使用已有根 CA: $($rootCA.Thumbprint)" -ForegroundColor Green
}

# 2. 导出根 CA 公钥（供手机安装）
Write-Host "[2/4] 导出根 CA 公钥..." -ForegroundColor Yellow
$rootCertPath = Join-Path $certDir "boxwise-root-ca.cer"
Export-Certificate -Cert $rootCA -FilePath $rootCertPath -Type CERT
Write-Host "  已导出: $rootCertPath" -ForegroundColor Green

# 3. 安装根 CA 到电脑受信任的根（需要管理员权限）
Write-Host "[3/4] 安装根 CA 到电脑受信任存储..." -ForegroundColor Yellow
$rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "CurrentUser")
$rootStore.Open("ReadWrite")
try {
    $rootStore.Add($rootCA)
    Write-Host "  根 CA 已安装到 CurrentUser\Root" -ForegroundColor Green
}
catch [System.Security.Cryptography.CryptographicException] {
    if ($_.Exception.Message -match "already exists|已存在") {
        Write-Host "  根 CA 已存在于受信任存储，跳过" -ForegroundColor Green
    }
    else { throw }
}
finally {
    $rootStore.Close()
}

# 4. 创建服务器证书
Write-Host "[4/4] 创建服务器证书..." -ForegroundColor Yellow

# 删除旧服务器证书（如果存在）
Get-ChildItem -Path "Cert:\CurrentUser\My" |
    Where-Object { $_.Subject -eq "CN=boxwise.local" } |
    ForEach-Object { Remove-Item -Path $_.PSPath -Force }

$serverCert = New-SelfSignedCertificate `
    -Subject "CN=boxwise.local" `
    -TextExtension @("2.5.29.17={text}DNS=localhost&DNS=boxwise.local&IPAddress=$IpAddress") `
    -Signer $rootCA `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyUsage DigitalSignature, KeyEncipherment `
    -NotAfter (Get-Date).AddYears(1)

Write-Host "  服务器证书: $($serverCert.Thumbprint)" -ForegroundColor Green

# 导出 PFX
$pfxPath = Join-Path $certDir "boxwise-dev.pfx"
$pfxPassword = ConvertTo-SecureString -String $Password -Force -AsPlainText
Export-PfxCertificate -Cert $serverCert -FilePath $pfxPath -Password $pfxPassword
Write-Host "  已导出 PFX: $pfxPath" -ForegroundColor Green

# 存储密码到 user-secrets
Write-Host ""
Write-Host "=== 存储密码到 user-secrets ===" -ForegroundColor Cyan
Push-Location $projectDir
try {
    dotnet user-secrets set "Kestrel:Certificates:Default:Password" "$Password"
    Write-Host "  密码已存储到 user-secrets" -ForegroundColor Green
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "=== 完成 ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "下一步：" -ForegroundColor White
Write-Host "  1. 将 $rootCertPath 传输到手机并安装："
Write-Host "     Android: 点击 .cer → 用途选'VPN和应用' → 确定"
Write-Host "     iPhone:  点击 .cer → 设置→通用→VPN与设备管理→安装"
Write-Host "              → 设置→通用→关于本机→证书信任设置→开启信任"
Write-Host "  2. 电脑防火墙放行: New-NetFirewallRule -DisplayName 'BoxWise 5000' -Direction Inbound -LocalPort 5000 -Protocol TCP -Action Allow"
Write-Host "  3. 启动: dotnet run"
Write-Host "  4. 手机访问: https://${IpAddress}:5000"
Write-Host ""
Write-Host "注意: 手机热点分配的 IP 可能变化，如果 IP 变了需重新运行此脚本。" -ForegroundColor DarkYellow
