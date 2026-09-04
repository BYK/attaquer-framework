$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$certificatePath = Join-Path $root "AttaquerTaskbar.cer"
$packagePath = Join-Path $root "AttaquerTaskbar.msix"

if (-not (Test-Path $certificatePath)) {
    throw "AttaquerTaskbar.cer is missing from the extracted package."
}
if (-not (Test-Path $packagePath)) {
    throw "AttaquerTaskbar.msix is missing from the extracted package."
}

Import-Certificate -FilePath $certificatePath -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
Add-AppxPackage -Path $packagePath -ForceApplicationShutdown

$installed = Get-AppxPackage -Name BYK.AttaquerTaskbar
if ($null -eq $installed) {
    throw "The package was not registered after installation."
}

Start-Process "shell:AppsFolder\$($installed.PackageFamilyName)!App"
