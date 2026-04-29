param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [Parameter(Mandatory = $true)]
    [string[]]$ArgumentList,

    [string]$WorkingDirectory = "",

    [int]$TimeoutSeconds = 35
)

$ErrorActionPreference = "Stop"

$argsText = $ArgumentList -join " "

Write-Host "[timeout:$TimeoutSeconds] $FilePath $argsText"

try {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FilePath
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $psi.WorkingDirectory = $WorkingDirectory
    }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    $psi.StandardOutputEncoding = $utf8NoBom
    $psi.StandardErrorEncoding = $utf8NoBom

    $psi.Arguments = ($ArgumentList | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + ($_ -replace '"', '\"') + '"'
        }
        else {
            $_
        }
    }) -join " "

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi

    [void]$process.Start()
    $outTask = $process.StandardOutput.ReadToEndAsync()
    $errTask = $process.StandardError.ReadToEndAsync()

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Write-Host "[timeout] Command exceeded ${TimeoutSeconds}s. Killing process tree PID=$($process.Id)."
        taskkill /PID $process.Id /T /F | Out-Null

        $outText = $outTask.GetAwaiter().GetResult()
        $errText = $errTask.GetAwaiter().GetResult()
        if ($outText) { Write-Host $outText }
        if ($errText) { Write-Host $errText }

        throw "Command timed out after ${TimeoutSeconds}s: $FilePath $argsText"
    }

    $outText = $outTask.GetAwaiter().GetResult()
    $errText = $errTask.GetAwaiter().GetResult()
    if ($outText) { Write-Host $outText }
    if ($errText) { Write-Host $errText }

    if ($process.ExitCode -ne 0) {
        throw "Command failed with exit code $($process.ExitCode): $FilePath $argsText"
    }
}
finally {
    if ($process) {
        $process.Dispose()
    }
}
