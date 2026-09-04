$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdministrator = $principal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)

if (-not $isAdministrator) {
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    $elevated = Start-Process powershell.exe `
        -Verb RunAs `
        -ArgumentList $arguments `
        -Wait `
        -PassThru
    exit $elevated.ExitCode
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$certificatePath = Join-Path $root "AttaquerTaskbar.cer"
$packagePath = Join-Path $root "AttaquerTaskbar.msix"

if (-not (Test-Path $certificatePath)) {
    throw "AttaquerTaskbar.cer is missing from the extracted package."
}
if (-not (Test-Path $packagePath)) {
    throw "AttaquerTaskbar.msix is missing from the extracted package."
}

# AppX deployment validates test-signing certificates against the local-machine
# Trusted People store. CurrentUser\TrustedPeople is insufficient on Windows 11.
Import-Certificate `
    -FilePath $certificatePath `
    -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Add-AppxPackage -Path $packagePath -ForceApplicationShutdown

$installed = Get-AppxPackage -Name BYK.AttaquerTaskbar
if ($null -eq $installed) {
    throw "The package was not registered after installation."
}

Start-Process "shell:AppsFolder\$($installed.PackageFamilyName)!App"
