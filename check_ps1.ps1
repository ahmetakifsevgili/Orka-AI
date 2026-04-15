$text = Get-Content 'integration_test.ps1' -Raw
try {
    $null = [scriptblock]::Create($text)
    Write-Host "PARSE OK - file is syntactically valid"
} catch [System.Management.Automation.ParseException] {
    $ex = $_
    Write-Host "Parse errors: $($ex.Exception.Errors.Count)"
    $ex.Exception.Errors | Select-Object -First 5 | ForEach-Object {
        Write-Host "  Line $($_.Extent.StartLineNumber): $($_.Message)"
    }
}
