[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$ExistingTrustFile
)

$ErrorActionPreference = 'Stop'
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$issuerPath = Join-Path $outputPath 'issuer-trust.json'
$kioskPath = Join-Path $outputPath 'kiosk-trust.json'
if ((Test-Path -LiteralPath $issuerPath) -or (Test-Path -LiteralPath $kioskPath)) {
    throw 'Choose a new output directory. Existing trust files will not be overwritten.'
}
$publicCertificates = @{}
if ($ExistingTrustFile) {
    $existing = Get-Content -LiteralPath $ExistingTrustFile -Raw | ConvertFrom-Json
    foreach ($entry in $existing.PublicCertificates.PSObject.Properties) {
        $publicCertificates[$entry.Name] = $entry.Value
    }
}

# The private key stays in this enrollment Windows account's certificate store.
$certificate = New-SelfSignedCertificate -Type Custom -Subject 'CN=NFC System QR Issuer' `
    -KeyAlgorithm ECDSA_nistP256 -HashAlgorithm SHA256 -KeyUsage DigitalSignature `
    -KeyExportPolicy NonExportable -Provider 'Microsoft Software Key Storage Provider' `
    -CertStoreLocation 'Cert:\CurrentUser\My' -NotAfter (Get-Date).AddYears(5)
$publicCertificates[$certificate.Thumbprint] = [Convert]::ToBase64String($certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
@{ ActiveSigningKeyId = $certificate.Thumbprint; PublicCertificates = $publicCertificates } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $issuerPath -Encoding UTF8
@{ PublicCertificates = $publicCertificates } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $kioskPath -Encoding UTF8
Write-Output "Created non-exportable signing certificate: $($certificate.Thumbprint)"
Write-Output "Enrollment configuration: $issuerPath"
Write-Output "Public-only kiosk configuration: $kioskPath"
Write-Output 'Install the appropriate file as %ProgramData%\NFC_System\QrKeys\trust.json with administrator-only write access.'
