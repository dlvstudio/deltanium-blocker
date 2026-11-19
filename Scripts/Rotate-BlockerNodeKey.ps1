<#
.SYNOPSIS
  Give a Block Creator a brand-new signing key without the private key ever being shown or written in plaintext.

.DESCRIPTION
  Run as Administrator on the IIS server that hosts the Blocker site.

  1. Generates a random secp256k1 private key in memory (never printed, never written in plaintext).
  2. Merges it into %ProgramData%\Deltanium\<Site>\env.dpapi.json as NODE_PRIVATE_KEY (DPAPI, LocalMachine),
     together with the one-time registration voucher. Other secrets in that file are kept.
  3. Stops the app pool, deletes the site's node_info.json (it holds the old, compromised key), starts the pool.
     On startup the node finds no node_info.json, registers with Central using NODE_PRIVATE_KEY and writes a new
     node_info.json that holds only the public key.
  4. Prints the new PUBLIC key.

  Afterwards, deactivate the old key on Central (POST /api/blockcreatornode/admin/deactivate).

.EXAMPLE
  .\Rotate-BlockerNodeKey.ps1 -Site blocker-1.deltanium.com -Voucher (Read-Host "Voucher")
#>
#requires -RunAsAdministrator
param(
    [Parameter(Mandatory = $true)]
    [string] $Site,

    [Parameter(Mandatory = $true)]
    [string] $Voucher,

    # IIS physical path of the site; read from IIS when omitted.
    [string] $SitePath = "",

    # Public URL the node registers with Central (default https://<Site>). Kept as NODE_ENDPOINT so a
    # re-registration never falls back to the local listen address.
    [string] $Endpoint = "",

    [int] $WaitSeconds = 90
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Security
Import-Module WebAdministration

function Protect-Dpapi([string]$Plain) {
    $b = [Text.Encoding]::UTF8.GetBytes($Plain)
    $p = [Security.Cryptography.ProtectedData]::Protect($b, $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
    [Array]::Clear($b, 0, $b.Length)
    return "dpapi:" + [Convert]::ToBase64String($p)
}

# secp256k1 group order: a valid private key k satisfies 0 < k < n.
$order = [System.Numerics.BigInteger]::Parse("0FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141", "AllowHexSpecifier")
function New-PrivateKeyHex {
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        while ($true) {
            $bytes = New-Object byte[] 32
            $rng.GetBytes($bytes)
            $hex = -join ($bytes | ForEach-Object { $_.ToString("x2") })
            [Array]::Clear($bytes, 0, $bytes.Length)
            $k = [System.Numerics.BigInteger]::Parse("0" + $hex, "AllowHexSpecifier")
            if ($k -gt 0 -and $k -lt $order) { return $hex }
        }
    } finally { $rng.Dispose() }
}

if (-not $SitePath) { $SitePath = (Get-Website -Name $Site).PhysicalPath }
$SitePath = [Environment]::ExpandEnvironmentVariables($SitePath)
if (-not (Test-Path $SitePath)) { throw "Site path not found: $SitePath" }

# 1-2. New key straight into DPAPI, merged with the site's existing secrets.
$dir = Join-Path $env:ProgramData "Deltanium\$Site"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$secretsPath = Join-Path $dir "env.dpapi.json"
$map = [ordered]@{}
if (Test-Path $secretsPath) {
    $existing = Get-Content $secretsPath -Raw | ConvertFrom-Json
    foreach ($p in $existing.PSObject.Properties) { $map[$p.Name] = $p.Value }
}
$keyHex = New-PrivateKeyHex
$map["NODE_PRIVATE_KEY"] = Protect-Dpapi $keyHex
$keyHex = $null
$map["VOUCHER"] = Protect-Dpapi $Voucher
$map["NodeRegistration__Voucher"] = Protect-Dpapi $Voucher
if (-not $Endpoint) { $Endpoint = "https://$Site" }
$map["NODE_ENDPOINT"] = $Endpoint
($map | ConvertTo-Json -Depth 4) | Set-Content -Path $secretsPath -Encoding UTF8
$pool = "IIS AppPool\$Site"
icacls $dir /grant "${pool}:(OI)(CI)R" /T | Out-Null
Write-Host "OK: new NODE_PRIVATE_KEY stored in $secretsPath (DPAPI). The value was never displayed."

# 3. Restart without the old node_info.json so the node re-registers with the new key.
Stop-WebAppPool -Name $Site -ErrorAction SilentlyContinue
$deadline = (Get-Date).AddSeconds(30)
while ((Get-WebAppPoolState -Name $Site).Value -ne "Stopped" -and (Get-Date) -lt $deadline) { Start-Sleep 1 }
$nodeInfo = Join-Path $SitePath "node_info.json"
if (Test-Path $nodeInfo) { Remove-Item $nodeInfo -Force; Write-Host "Deleted old node_info.json (held the compromised key)." }
Start-WebAppPool -Name $Site
Invoke-WebRequest -Uri "https://$Site/api/version" -UseBasicParsing -TimeoutSec 30 | Out-Null

# 4. Wait for registration and show the new public key only.
$deadline = (Get-Date).AddSeconds($WaitSeconds)
while (-not (Test-Path $nodeInfo) -and (Get-Date) -lt $deadline) { Start-Sleep 2 }
if (-not (Test-Path $nodeInfo)) { throw "No new node_info.json after $WaitSeconds s. Check the site's logs (voucher used? Central reachable?)." }
$info = Get-Content $nodeInfo -Raw | ConvertFrom-Json
$hasPlainKey = -not [string]::IsNullOrWhiteSpace($info.PrivateKey) -or -not [string]::IsNullOrWhiteSpace($info.Mnemonic)
Write-Host ""
Write-Host "New public key: $($info.PublicKey)"
if ($hasPlainKey) { Write-Warning "node_info.json still holds a private key in plaintext - NODE_PRIVATE_KEY was not picked up. Do not continue; check the site." }
else { Write-Host "node_info.json holds no private key." }
Write-Host ""
Write-Host "Next: deactivate the old key on Central:"
Write-Host "  POST https://api.deltanium.com/api/blockcreatornode/admin/deactivate  {""publicKey"":""<old key>"",""reason"":""key rotation""}"
