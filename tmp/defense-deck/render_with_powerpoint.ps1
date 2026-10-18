param(
    [Parameter(Mandatory=$true)][string]$InputPath,
    [Parameter(Mandatory=$true)][string]$PdfPath
)
$wasRunning = $null -ne (Get-Process -Name POWERPNT -ErrorAction SilentlyContinue)
$app = $null
$presentation = $null
try {
    $app = New-Object -ComObject PowerPoint.Application
    $app.DisplayAlerts = 1
    $presentation = $app.Presentations.Open($InputPath, -1, 0, 0)
    $presentation.SaveAs($PdfPath, 32)
    Write-Output ('Rendered slides: ' + $presentation.Slides.Count)
    Write-Output $PdfPath
} finally {
    if ($null -ne $presentation) {
        $presentation.Close()
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($presentation) | Out-Null
    }
    if ($null -ne $app) {
        if (-not $wasRunning) {
            $app.Quit()
        }
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($app) | Out-Null
    }
}
