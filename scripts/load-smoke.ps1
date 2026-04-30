param(
    [string]$BaseUrl = "http://localhost:5065",
    [int]$Users = 4,
    [int]$TimeoutSeconds = 90,
    [int]$RampDelayMs = 300,
    [switch]$IncludeAi
)

$ErrorActionPreference = "Stop"

if ($Users -lt 1) {
    throw "Users must be at least 1."
}

$liveUserSmoke = Join-Path $PSScriptRoot "live-user-smoke.ps1"
if (-not (Test-Path $liveUserSmoke)) {
    throw "Missing live-user-smoke.ps1 at $liveUserSmoke"
}

$includeAiValue = $IncludeAi.IsPresent
Write-Host "[load-smoke] Starting $Users concurrent live-user smoke workers. IncludeAi=$includeAiValue"
$startedAt = Get-Date
$jobs = New-Object System.Collections.Generic.List[object]

for ($i = 1; $i -le $Users; $i++) {
    $job = Start-Job -Name "orka-load-smoke-$i" -ArgumentList $liveUserSmoke, $BaseUrl, $includeAiValue, $i -ScriptBlock {
        param(
            [string]$ScriptPath,
            [string]$WorkerBaseUrl,
            [object]$WorkerIncludeAi,
            [int]$WorkerId
        )

        $ErrorActionPreference = "Stop"
        $workerStartedAt = Get-Date

        $includeAiForWorker = [System.Convert]::ToBoolean($WorkerIncludeAi)

        try {
            if ($includeAiForWorker) {
                & $ScriptPath -BaseUrl $WorkerBaseUrl -IncludeAi | Out-String | Write-Output
            }
            else {
                & $ScriptPath -BaseUrl $WorkerBaseUrl | Out-String | Write-Output
            }

            [pscustomobject]@{
                workerId = $WorkerId
                status = "passed"
                durationMs = [int]((Get-Date) - $workerStartedAt).TotalMilliseconds
                error = $null
            }
        }
        catch {
            [pscustomobject]@{
                workerId = $WorkerId
                status = "failed"
                durationMs = [int]((Get-Date) - $workerStartedAt).TotalMilliseconds
                error = $_.Exception.Message
            }
            exit 1
        }
    }

    $jobs.Add($job)
    Start-Sleep -Milliseconds $RampDelayMs
}

$completed = Wait-Job -Job $jobs -Timeout $TimeoutSeconds
$timedOut = @($jobs | Where-Object { $_.State -eq "Running" })
if ($timedOut.Count -gt 0) {
    $timedOut | Stop-Job
}

$results = @()
foreach ($job in $jobs) {
    $output = Receive-Job -Job $job -Keep
    $summary = $output | Where-Object { $_ -is [pscustomobject] -and $_.PSObject.Properties.Name -contains "workerId" } | Select-Object -Last 1
    if ($null -eq $summary) {
        $summary = [pscustomobject]@{
            workerId = $job.Name
            status = if ($job.State -eq "Completed") { "passed" } else { "failed" }
            durationMs = $null
            error = "Worker finished without summary. State=$($job.State)"
        }
    }
    $results += $summary
}

$jobs | Remove-Job -Force

if ($timedOut.Count -gt 0) {
    throw "[load-smoke] Timed out workers: $($timedOut.Name -join ', ')"
}

$failed = @($results | Where-Object { $_.status -ne "passed" })
$durationMs = [int]((Get-Date) - $startedAt).TotalMilliseconds

$results |
    Sort-Object workerId |
    Format-Table workerId, status, durationMs, error -AutoSize

if ($failed.Count -gt 0) {
    throw "[load-smoke] Failed workers: $($failed.workerId -join ', ')"
}

Write-Host "[load-smoke] Passed. Workers=$Users DurationMs=$durationMs IncludeAi=$includeAiValue"
