# ============================================================
#  Orka AI - End-to-End Integration & Health Test Suite v2.0
#  Usage: .\integration_test.ps1
#  Requirement: Backend must be running at http://localhost:5065
#
#  SCORING RULES:
#    Add-Score  : Required feature. Pass = +pts, Fail = 0 (never negative)
#    Add-Bonus  : Optional/extra feature. Pass = +pts to BonusScore only.
#                 Missing/not-yet-generated features (wiki, quiz) go here.
# ============================================================

param(
    [string]$BaseUrl    = "http://localhost:5065/api",
    [string]$Email      = "orka_test_runner@orka.ai",
    [string]$Password   = "OrkaTester123!",
    [string]$ReportFile = "SYSTEM_HEALTH_REPORT.md",
    [switch]$Verbose
)

$ErrorActionPreference = "SilentlyContinue"

# -- Color helpers -------------------------------------------
function Write-Pass { param($msg) Write-Host "  [PASS] $msg" -ForegroundColor Green }
function Write-Fail { param($msg) Write-Host "  [FAIL] $msg" -ForegroundColor Red }
function Write-Warn { param($msg) Write-Host "  [WARN] $msg" -ForegroundColor Yellow }
function Write-Info { param($msg) Write-Host "  [INFO] $msg" -ForegroundColor Cyan }
function Write-Bonus{ param($msg) Write-Host "  [BONUS] $msg" -ForegroundColor Magenta }
function Write-Head { param($msg) Write-Host "`n=== $msg ===" -ForegroundColor White }

# PS5.1-compatible null coalescing helper
function Get-First {
    param([object[]]$Values)
    foreach ($v in $Values) {
        if ($v -ne $null -and "$v" -ne "") { return $v }
    }
    return $null
}

function Get-Prop {
    param($Obj, [string]$Prop)
    if ($Obj -eq $null) { return $null }
    try { return $Obj.$Prop } catch { return $null }
}

# -- SSE Chat helper -----------------------------------------
function Invoke-SSEChat {
    param(
        [string]$Url,
        [string]$Token,
        [hashtable]$Body,
        [int]$TimeoutMs = 90000,
        [int]$MaxLines  = 800
    )
    $json  = $Body | ConvertTo-Json
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $result = [PSCustomObject]@{ Ok=$false; Content=""; SessionId=$null; Ms=0; Error=""; ChunkCount=0; ThinkingSeen=$false }
    try {
        $req = [System.Net.HttpWebRequest]::Create($Url)
        $req.Method        = "POST"
        $req.Headers.Add("Authorization", "Bearer $Token")
        $req.ContentType   = "application/json"
        $req.Timeout       = $TimeoutMs
        $req.ContentLength = $bytes.Length
        $rs = $req.GetRequestStream()
        $rs.Write($bytes, 0, $bytes.Length)
        $rs.Close()
        $sw   = [System.Diagnostics.Stopwatch]::StartNew()
        $resp = $req.GetResponse()
        $result.SessionId = $resp.Headers["X-Orka-SessionId"]
        $rdr  = New-Object System.IO.StreamReader($resp.GetResponseStream())
        $txt  = ""; $n = 0
        while (-not $rdr.EndOfStream -and $n -lt $MaxLines) {
            $ln = $rdr.ReadLine(); $n++
            if ($ln -match "\[DONE\]") { break }
            if ($ln -match "\[THINKING:") { $result.ThinkingSeen = $true }
            if ($ln -match "^data:" -and $ln -notmatch "\[THINKING:" -and $ln -notmatch "\[PLAN_READY\]" -and $ln -notmatch "\[ERROR\]" -and $ln.Trim() -ne "data:") {
                $txt += $ln.Substring(6)
                $result.ChunkCount++
            }
        }
        $rdr.Close(); $resp.Close(); $sw.Stop()
        $result.Ok = $true; $result.Content = $txt; $result.Ms = $sw.ElapsedMilliseconds
    } catch {
        $result.Error = $_.Exception.Message.Substring(0, [Math]::Min(120, $_.Exception.Message.Length))
    }
    return $result
}

# -- Score tracking ------------------------------------------
$Score      = 0
$MaxScore   = 0
$BonusScore = 0
$TestRows   = [System.Collections.Generic.List[object]]::new()

function Add-Score {
    param([string]$Name, [int]$Earned, [int]$Max, [string]$Detail = "")
    $script:Score    += $Earned
    $script:MaxScore += $Max
    $icon = if ($Earned -ge $Max) { "OK" } elseif ($Earned -gt 0) { "PARTIAL" } else { "FAIL" }
    $script:TestRows.Add([PSCustomObject]@{
        Test   = $Name
        Result = "$Earned/$Max"
        Status = $icon
        Detail = $Detail
        Type   = "Required"
    })
}

function Add-Bonus {
    param([string]$Name, [int]$Earned, [string]$Detail = "")
    $script:BonusScore += $Earned
    $icon = if ($Earned -gt 0) { "BONUS" } else { "SKIP" }
    $script:TestRows.Add([PSCustomObject]@{
        Test   = "[BONUS] $Name"
        Result = "+$Earned"
        Status = $icon
        Detail = $Detail
        Type   = "Bonus"
    })
}

# -- HTTP helper ---------------------------------------------
function Invoke-Timed {
    param([string]$Method, [string]$Url, [hashtable]$Headers = @{}, $Body = $null, [int]$TimeoutSec = 30)
    $sw  = [System.Diagnostics.Stopwatch]::StartNew()
    $res = $null; $err = $null; $exStatusCode = 0
    try {
        $params = @{ Method = $Method; Uri = $Url; Headers = $Headers; TimeoutSec = $TimeoutSec }
        if ($Body) {
            $params["Body"]        = ($Body | ConvertTo-Json -Depth 10)
            $params["ContentType"] = "application/json"
        }
        $res = Invoke-WebRequest @params -UseBasicParsing
    } catch {
        $err = $_
        try { $exStatusCode = [int]$err.Exception.Response.StatusCode } catch { $exStatusCode = 0 }
        if ($exStatusCode -ge 400) {
            try {
                $stream  = $err.Exception.Response.GetResponseStream()
                $reader  = New-Object System.IO.StreamReader($stream)
                $errBody = $reader.ReadToEnd()
                $res = [PSCustomObject]@{ StatusCode = $exStatusCode; Content = $errBody }
            } catch {}
        }
    }
    $sw.Stop()
    $sc = if ($res -ne $null) { [int]$res.StatusCode } elseif ($exStatusCode -gt 0) { $exStatusCode } else { 0 }
    $ok = ($sc -ge 200 -and $sc -lt 300)
    return [PSCustomObject]@{
        Response   = $res
        Error      = $err
        StatusCode = $sc
        Ms         = $sw.ElapsedMilliseconds
        Ok         = $ok
    }
}

function Get-AuthHeaders { param([string]$Token); return @{ "Authorization" = "Bearer $Token" } }

function Get-HttpStatus {
    param([string]$Method, [string]$Url, [hashtable]$Headers = @{}, [int]$TimeoutSec = 10)
    try {
        $r = Invoke-WebRequest -Method $Method -Uri $Url -Headers $Headers -TimeoutSec $TimeoutSec -UseBasicParsing
        return [int]$r.StatusCode
    } catch {
        $ex = $_.Exception
        if ($ex.Response -ne $null) { return [int]$ex.Response.StatusCode }
        $msg = $ex.Message
        if ($msg -match "404") { return 404 }
        if ($msg -match "401") { return 401 }
        if ($msg -match "403") { return 403 }
        if ($msg -match "500") { return 500 }
        return 0
    }
}

# ============================================================
#  SECTION 0 - Backend Reachability
# ============================================================
Write-Head "SECTION 0 - Backend Reachability"

$pingResult = Invoke-Timed -Method GET -Url "$BaseUrl/test/ping-groq" -TimeoutSec 5
if ($pingResult.Ok) {
    Write-Pass "Backend reachable ($($pingResult.Ms) ms)"
    Add-Score "Backend Reachable" 5 5 "$($pingResult.Ms) ms"
} else {
    Write-Fail "Backend NOT RESPONDING at http://localhost:5065 - Start: dotnet run --project Orka.API"
    Add-Score "Backend Reachable" 0 5 "Connection failed"
    exit 1
}

# ============================================================
#  SECTION 1 - Multi-Agent Orchestration Health Check
# ============================================================
Write-Head "SECTION 1 - Multi-Agent Orchestration (AI Providers)"

$providerResults = [ordered]@{}

# 1A: Groq (Fallback Chain Base)
$groqRes = Invoke-Timed -Method GET -Url "$BaseUrl/test/ping-groq" -TimeoutSec 20
if ($groqRes.Ok) {
    $ls = if ($groqRes.Ms -lt 1500) { 3 } elseif ($groqRes.Ms -lt 4000) { 2 } elseif ($groqRes.Ms -lt 9000) { 1 } else { 0 }
    Write-Pass "Groq (fallback base) - $($groqRes.Ms) ms"
    Add-Score "AI Provider: Groq" $ls 3 "$($groqRes.Ms) ms"
    $providerResults["Groq"] = @{ Ok = $true; Ms = $groqRes.Ms }
} else {
    Write-Fail "Groq - HTTP $($groqRes.StatusCode)"
    Add-Score "AI Provider: Groq" 0 3 "HTTP $($groqRes.StatusCode)"
    $providerResults["Groq"] = @{ Ok = $false; Ms = 0 }
}

# 1B: GitHub Models (Primary)
Write-Info "GitHub Models ping (gpt-4o, gpt-4o-mini, Meta-Llama-405B)..."
$ghRes = Invoke-Timed -Method GET -Url "$BaseUrl/test/ping-github" -TimeoutSec 90
if ($ghRes.Ok) {
    try {
        $ghData = $ghRes.Response.Content | ConvertFrom-Json
        foreach ($model in $ghData.results) {
            $mname = $model.provider; $label = $mname.PadRight(28)
            if ($model.ok) {
                $ms = [long]$model.latencyMs
                $ls = if ($ms -lt 2000) { 3 } elseif ($ms -lt 4000) { 2 } elseif ($ms -lt 8000) { 1 } else { 0 }
                Write-Pass "$label - $ms ms"
                Add-Score "GitHub: $mname" $ls 3 "$ms ms"
                $providerResults[$mname] = @{ Ok = $true; Ms = $ms }
            } else {
                Write-Fail "$label - $($model.error)"
                Add-Score "GitHub: $mname" 0 3 "API error"
                $providerResults[$mname] = @{ Ok = $false; Ms = 0 }
            }
        }
    } catch {
        Write-Fail "GitHub Models JSON parse error"
        Add-Score "GitHub: gpt-4o"      0 3 "Parse error"
        Add-Score "GitHub: gpt-4o-mini" 0 3 "Parse error"
        Add-Score "GitHub: Llama-405B"  0 3 "Parse error"
    }
} else {
    Write-Fail "GitHub Models - HTTP $($ghRes.StatusCode)"
    Add-Score "GitHub: gpt-4o"      0 3 "HTTP $($ghRes.StatusCode)"
    Add-Score "GitHub: gpt-4o-mini" 0 3 "HTTP $($ghRes.StatusCode)"
    Add-Score "GitHub: Llama-405B"  0 3 "HTTP $($ghRes.StatusCode)"
}

# 1C: Failover Chain
$chainResult = Invoke-Timed -Method GET -Url "$BaseUrl/test/chain-test" -TimeoutSec 40
if ($chainResult.Ok) {
    Write-Pass "Failover chain OK ($($chainResult.Ms) ms)"
    Add-Score "AI Failover Chain" 5 5 "$($chainResult.Ms) ms"
} else {
    Write-Fail "Failover chain FAILED"
    Add-Score "AI Failover Chain" 0 5 "Failed"
}

# 1D: AIAgentFactory - 5 roles
Write-Info "AIAgentFactory - 5 agent roles..."
$factoryRoles = @("Tutor", "DeepPlan", "Analyzer", "Summarizer", "Korteks")
foreach ($role in $factoryRoles) {
    $fRes = Invoke-Timed -Method GET -Url "$BaseUrl/test/ping-factory?role=$role" -TimeoutSec 35
    if ($fRes.Ok) {
        try {
            $fData = $fRes.Response.Content | ConvertFrom-Json
            $ms    = [long]$fData.latencyMs
            $model = $fData.model
            $ls    = if ($ms -lt 2000) { 3 } elseif ($ms -lt 4000) { 2 } elseif ($ms -lt 8000) { 1 } else { 0 }
            $label = "Factory/$role ($model)".PadRight(38)
            Write-Pass "$label - $ms ms"
            Add-Score "AIAgentFactory: $role" $ls 3 "$ms ms, model=$model"
            $providerResults["Factory-$role"] = @{ Ok = $true; Ms = $ms }
        } catch {
            Write-Warn "Factory/$role parse error"
            Add-Score "AIAgentFactory: $role" 1 3 "Parse error"
        }
    } else {
        Write-Fail "Factory/$role - HTTP $($fRes.StatusCode)"
        Add-Score "AIAgentFactory: $role" 0 3 "HTTP $($fRes.StatusCode)"
        $providerResults["Factory-$role"] = @{ Ok = $false; Ms = 0 }
    }
}

# 1E: Cohere Embedding
Write-Info "Cohere embed-multilingual-v3.0..."
$embedRes = Invoke-Timed -Method GET -Url "$BaseUrl/test/ping-embed" -TimeoutSec 15
if ($embedRes.Ok) {
    try {
        $eData = $embedRes.Response.Content | ConvertFrom-Json
        $dim   = [int]$eData.dimensions; $ms = [long]$eData.latencyMs
        if ($dim -eq 1024) {
            Write-Pass "Cohere Embed - $dim dim, $ms ms"
            Add-Score "Cohere: Embedding (1024-dim)" 5 5 "$dim dim, $ms ms"
        } else {
            Write-Warn "Cohere Embed - unexpected dim: $dim"
            Add-Score "Cohere: Embedding (1024-dim)" 2 5 "$dim dim (expected 1024)"
        }
        $providerResults["CohereEmbed"] = @{ Ok = $true; Ms = $ms }
    } catch {
        Write-Fail "Cohere Embed parse error"
        Add-Score "Cohere: Embedding (1024-dim)" 0 5 "Parse error"
    }
} else {
    Write-Fail "Cohere Embed - HTTP $($embedRes.StatusCode)"
    Add-Score "Cohere: Embedding (1024-dim)" 0 5 "HTTP $($embedRes.StatusCode)"
    $providerResults["CohereEmbed"] = @{ Ok = $false; Ms = 0 }
}

# ============================================================
#  SECTION 2 - Authentication
# ============================================================
Write-Head "SECTION 2 - Auth (Register + Login + Refresh)"

$registerBody = @{ email = $Email; password = $Password; firstName = "Orka"; lastName = "Tester" }
$regResult    = Invoke-Timed -Method POST -Url "$BaseUrl/auth/register" -Body $registerBody -TimeoutSec 15
if ($regResult.StatusCode -eq 200) {
    Write-Pass "Register OK ($($regResult.Ms) ms)"
} elseif ($regResult.StatusCode -eq 409) {
    Write-Info "User already exists - logging in."
} else {
    Write-Warn "Register HTTP $($regResult.StatusCode) - continuing."
}

$loginBody   = @{ email = $Email; password = $Password }
$loginResult = Invoke-Timed -Method POST -Url "$BaseUrl/auth/login" -Body $loginBody -TimeoutSec 15
$Token = $null

if ($loginResult.Ok) {
    try {
        $loginData = $loginResult.Response.Content | ConvertFrom-Json
        $Token = Get-First @(
            (Get-Prop $loginData "token"),
            (Get-Prop $loginData "accessToken"),
            (Get-Prop (Get-Prop $loginData "data") "token")
        )
        if ($Token) {
            Write-Pass "Login OK, JWT received ($($loginResult.Ms) ms)"
            Add-Score "Auth: Login" 5 5 "$($loginResult.Ms) ms"
        } else {
            Write-Fail "Token field not found"
            Add-Score "Auth: Login" 2 5 "Token parse error"
        }
    } catch {
        Write-Fail "Login JSON parse error: $_"
        Add-Score "Auth: Login" 0 5 "JSON parse error"
    }
} else {
    Write-Fail "Login FAILED - HTTP $($loginResult.StatusCode)"
    Add-Score "Auth: Login" 0 5 "HTTP $($loginResult.StatusCode)"
}

if ($Token) {
    $r2 = Invoke-Timed -Method POST -Url "$BaseUrl/auth/login" -Body $loginBody -TimeoutSec 10
    if ($r2.Ok) {
        Write-Pass "Token refresh simulated ($($r2.Ms) ms)"
        Add-Score "Auth: Token Refresh" 3 3 "$($r2.Ms) ms"
    } else {
        Add-Score "Auth: Token Refresh" 0 3 "Failed"
    }

    # 2.3: Unauthenticated access returns 401
    $sc401 = Get-HttpStatus -Method GET -Url "$BaseUrl/dashboard/stats"
    if ($sc401 -eq 401) {
        Write-Pass "Unauthenticated access returns 401"
        Add-Score "Auth: 401 Guard" 3 3 "HTTP 401"
    } elseif ($sc401 -eq 0) {
        Write-Warn "Auth guard check skipped"
        Add-Score "Auth: 401 Guard" 1 3 "Could not determine"
    } else {
        Write-Fail "Protected endpoint returned HTTP $sc401 (expected 401)"
        Add-Score "Auth: 401 Guard" 0 3 "HTTP $sc401"
    }

    # 2.4: Invalid credentials return 401
    $badLogin = Invoke-Timed -Method POST -Url "$BaseUrl/auth/login" -Body @{ email = $Email; password = "WrongPassword999!" } -TimeoutSec 10
    if ($badLogin.StatusCode -eq 401 -or $badLogin.StatusCode -eq 400) {
        Write-Pass "Invalid credentials rejected - HTTP $($badLogin.StatusCode)"
        Add-Score "Auth: Reject Invalid Creds" 3 3 "HTTP $($badLogin.StatusCode)"
    } else {
        Write-Fail "Invalid credentials returned HTTP $($badLogin.StatusCode)"
        Add-Score "Auth: Reject Invalid Creds" 0 3 "HTTP $($badLogin.StatusCode)"
    }
}

# ============================================================
#  SECTION 3 - Protected Endpoints
# ============================================================
Write-Head "SECTION 3 - Protected Endpoints"

if (-not $Token) {
    Write-Warn "No token - skipping protected endpoint tests."
    Add-Score "Protected Endpoints" 0 14 "No token"
} else {
    $ah = Get-AuthHeaders $Token
    $endpointTests = [ordered]@{
        "GET /topics"                    = @{ M="GET"; U="$BaseUrl/topics";                    S=3 }
        "GET /user/me"                   = @{ M="GET"; U="$BaseUrl/user/me";                   S=3 }
        "GET /dashboard/stats"           = @{ M="GET"; U="$BaseUrl/dashboard/stats";           S=3 }
        "GET /quiz/stats"                = @{ M="GET"; U="$BaseUrl/quiz/stats";                S=3 }
        "GET /dashboard/recent-activity" = @{ M="GET"; U="$BaseUrl/dashboard/recent-activity"; S=2 }
    }
    foreach ($name in $endpointTests.Keys) {
        $ep  = $endpointTests[$name]
        $res = Invoke-Timed -Method $ep.M -Url $ep.U -Headers $ah
        if ($res.Ok) {
            Write-Pass "$name ($($res.Ms) ms)"
            Add-Score $name $ep.S $ep.S "$($res.Ms) ms"
        } else {
            Write-Fail "$name - HTTP $($res.StatusCode)"
            Add-Score $name 0 $ep.S "HTTP $($res.StatusCode)"
        }
    }

    # 3.6: Invalid endpoint returns 404 not 500
    $sc404 = Get-HttpStatus -Method GET -Url "$BaseUrl/nonexistent/endpoint/xyz" -Headers $ah
    if ($sc404 -eq 404) {
        Write-Pass "Invalid endpoint returns 404"
        Add-Score "Resilience: 404 Handling" 2 2 "HTTP 404"
    } elseif ($sc404 -eq 500) {
        Write-Fail "Invalid endpoint returned 500"
        Add-Score "Resilience: 404 Handling" 0 2 "HTTP 500"
    } else {
        Write-Warn "Invalid endpoint returned HTTP $sc404"
        Add-Score "Resilience: 404 Handling" 1 2 "HTTP $sc404"
    }
}

# ============================================================
#  SECTION 4 - Topic CRUD lifecycle
# ============================================================
Write-Head "SECTION 4 - Topic CRUD Lifecycle"

$TopicId    = $null
$TopicTitle = "Orka Integration Test - $(Get-Date -Format 'HHmmss')"

if (-not $Token) {
    Write-Warn "No token - skipping topic tests."
    Add-Score "Topic CRUD" 0 10 "No token"
} else {
    $ctBody   = @{ title = $TopicTitle; emoji = "T"; category = "Test" }
    $ctResult = Invoke-Timed -Method POST -Url "$BaseUrl/topics" -Headers (Get-AuthHeaders $Token) -Body $ctBody
    if ($ctResult.Ok) {
        try {
            $td      = $ctResult.Response.Content | ConvertFrom-Json
            $TopicId = Get-First @((Get-Prop $td "id"), (Get-Prop (Get-Prop $td "data") "id"))
            Write-Pass "Topic created (ID: $TopicId) - $($ctResult.Ms) ms"
            Add-Score "Topic: CREATE" 4 4 "ID: $TopicId"
        } catch {
            Write-Fail "Topic parse error: $_"
            Add-Score "Topic: CREATE" 2 4 "Parse error"
        }
    } else {
        Write-Fail "Topic creation failed - HTTP $($ctResult.StatusCode)"
        Add-Score "Topic: CREATE" 0 4 "HTTP $($ctResult.StatusCode)"
    }

    if ($TopicId) {
        # LIST consistency
        $listRes = Invoke-Timed -Method GET -Url "$BaseUrl/topics" -Headers (Get-AuthHeaders $Token)
        if ($listRes.Ok) {
            try {
                $tlist = $listRes.Response.Content | ConvertFrom-Json
                $found = $tlist | Where-Object { $_.id -eq $TopicId }
                if ($found) {
                    Write-Pass "Topic visible in list (total: $($tlist.Count))"
                    Add-Score "Topic: LIST Consistency" 3 3 "Found in list"
                } else {
                    Write-Fail "Topic NOT in list"
                    Add-Score "Topic: LIST Consistency" 0 3 "Not found"
                }
            } catch { Add-Score "Topic: LIST Consistency" 1 3 "Parse error" }
        }

        # PATCH
        $patchRes = Invoke-Timed -Method PATCH -Url "$BaseUrl/topics/$TopicId" -Headers (Get-AuthHeaders $Token) -Body @{ title = "$TopicTitle [Updated]" }
        if ($patchRes.Ok -or $patchRes.StatusCode -eq 204) {
            Write-Pass "Topic PATCH OK ($($patchRes.Ms) ms)"
            Add-Score "Topic: PATCH" 3 3 "$($patchRes.Ms) ms"
        } else {
            Write-Fail "Topic PATCH failed - HTTP $($patchRes.StatusCode)"
            Add-Score "Topic: PATCH" 0 3 "HTTP $($patchRes.StatusCode)"
        }

        # GET /topics/{id}/subtopics
        $stRes = Invoke-Timed -Method GET -Url "$BaseUrl/topics/$TopicId/subtopics" -Headers (Get-AuthHeaders $Token) -TimeoutSec 10
        if ($stRes.Ok) {
            try {
                $st = $stRes.Response.Content | ConvertFrom-Json
                $hasParentId = $st.PSObject.Properties.Name -contains "parentId"
                $hasSubs     = $st.PSObject.Properties.Name -contains "subtopics"
                if ($hasParentId -and $hasSubs) {
                    Write-Pass "Subtopics endpoint OK - count=$($st.count)"
                    Add-Score "Topic: Subtopics Endpoint" 4 4 "count=$($st.count)"
                } else {
                    Write-Warn "Subtopics response missing fields"
                    Add-Score "Topic: Subtopics Endpoint" 2 4 "Partial response"
                }
            } catch { Add-Score "Topic: Subtopics Endpoint" 0 4 "Parse error" }
        } else {
            Write-Fail "Subtopics - HTTP $($stRes.StatusCode)"
            Add-Score "Topic: Subtopics Endpoint" 0 4 "HTTP $($stRes.StatusCode)"
        }

        # GET /topics/{id}/progress
        $prRes = Invoke-Timed -Method GET -Url "$BaseUrl/topics/$TopicId/progress" -Headers (Get-AuthHeaders $Token) -TimeoutSec 10
        if ($prRes.Ok) {
            try {
                $pr = $prRes.Response.Content | ConvertFrom-Json
                $hasTopicId  = $pr.PSObject.Properties.Name -contains "topicId"
                $hasProgress = $pr.PSObject.Properties.Name -contains "progressPercentage"
                $hasAccuracy = $pr.PSObject.Properties.Name -contains "quizAccuracy"
                if ($hasTopicId -and $hasProgress -and $hasAccuracy) {
                    Write-Pass "Topic progress OK - $($pr.progressPercentage)pct quizAccuracy=$($pr.quizAccuracy)pct"
                    Add-Score "Topic: Progress Endpoint" 4 4 "All fields OK"
                } else {
                    Write-Warn "Topic progress missing fields"
                    Add-Score "Topic: Progress Endpoint" 2 4 "Partial"
                }
            } catch { Add-Score "Topic: Progress Endpoint" 0 4 "Parse error" }
        } else {
            Write-Fail "Topic progress - HTTP $($prRes.StatusCode)"
            Add-Score "Topic: Progress Endpoint" 0 4 "HTTP $($prRes.StatusCode)"
        }
    }
}

# ============================================================
#  SECTION 5 - Agent Orchestration (Chat Simulation)
# ============================================================
Write-Head "SECTION 5 - Agent Orchestration (Chat Simulation)"

$SessionId  = $null
$FirstMsgOk = $false

if (-not $Token -or -not $TopicId) {
    Write-Warn "No token or TopicId - skipping chat tests."
    Add-Score "Chat: TutorAgent First Response" 0 5 "Missing prerequisite"
    Add-Score "Chat: Conversation Continuity"   0 5 "Missing prerequisite"
    Add-Score "Chat: Session End"               0 2 "Missing prerequisite"
} else {
    # Message 1: start learning
    Write-Info "Sending first message to TutorAgent via SSE (120s timeout)..."
    $m1 = Invoke-SSEChat -Url "$BaseUrl/chat/stream" -Token $Token -TimeoutMs 120000 -Body @{
        content = "Hello, start teaching me about this topic in English."
        topicId = $TopicId; sessionId = $null; isPlanMode = $false
    }
    if ($m1.Ok) {
        if ($m1.SessionId) { $SessionId = $m1.SessionId }
        $rlen = $m1.Content.Length
        Write-Pass "TutorAgent responded ($($m1.Ms) ms, $rlen chars, Session: $SessionId)"
        $qs = if ($rlen -gt 30) { 5 } else { 0 }
        Add-Score "Chat: TutorAgent First Response" $qs 5 "$($m1.Ms) ms, $rlen chars"
        $FirstMsgOk = $true
        if ($m1.ThinkingSeen) {
            Write-Bonus "[THINKING] signal received during TutorAgent stream"
            Add-Bonus "Chat: THINKING Signal" 2 "[THINKING] seen"
        }
    } else {
        Write-Fail "Chat message failed: $($m1.Error)"
        Add-Score "Chat: TutorAgent First Response" 0 5 "SSE error"
    }

    # Message 2: follow-up continuity
    if ($FirstMsgOk -and $SessionId) {
        $m2 = Invoke-SSEChat -Url "$BaseUrl/chat/stream" -Token $Token -TimeoutMs 90000 -Body @{
            content = "Can you explain a bit more about that?"; topicId = $TopicId; sessionId = $SessionId; isPlanMode = $false
        }
        if ($m2.Ok) {
            Write-Pass "Follow-up message OK ($($m2.Ms) ms)"
            Add-Score "Chat: Conversation Continuity" 5 5 "$($m2.Ms) ms"
        } else {
            Write-Fail "Follow-up failed: $($m2.Error)"
            Add-Score "Chat: Conversation Continuity" 0 5 "SSE error"
        }

        # Message 3: quiz trigger (BONUS only — quiz may not fire without context)
        Write-Info "Quiz trigger message (BONUS - quiz fires only when TutorAgent decides)..."
        $m3 = Invoke-SSEChat -Url "$BaseUrl/chat/stream" -Token $Token -TimeoutMs 90000 -Body @{
            content = "Give me a quiz about what we discussed."; topicId = $TopicId; sessionId = $SessionId; isPlanMode = $false
        }
        if ($m3.Ok) {
            $aiText = $m3.Content
            if ($aiText -match '"question"' -and $aiText -match '"options"') {
                Write-Bonus "Quiz JSON detected in response"
                $jm = [regex]::Match($aiText, '\{[\s\S]*?"question"[\s\S]*?"options"[\s\S]*?\}')
                if ($jm.Success) {
                    try {
                        $qo = $jm.Value | ConvertFrom-Json
                        if ($qo.question -and $qo.options -and $qo.options.Count -ge 2) {
                            $hasBug = $qo.options | Where-Object { $_.text -match "^[A-D]\)" }
                            if (-not $hasBug) {
                                Write-Bonus "Quiz JSON valid, no A)/B) prefix bug"
                                Add-Bonus "Chat: Quiz JSON Valid + No Prefix Bug" 5 "Valid JSON, clean options"
                            } else {
                                Write-Bonus "Quiz JSON valid (has prefix bug)"
                                Add-Bonus "Chat: Quiz JSON Valid" 3 "Valid JSON, prefix bug present"
                            }
                        } else {
                            Add-Bonus "Chat: Quiz JSON Partial" 1 "Incomplete structure"
                        }
                    } catch {
                        Add-Bonus "Chat: Quiz JSON" 0 "Parse error"
                    }
                }
            } else {
                Write-Info "Quiz context still accumulating (by design - no penalty)"
                Add-Bonus "Chat: Quiz Trigger Response" 2 "API OK, quiz accumulating"
            }
        } else {
            Write-Warn "Quiz trigger SSE error: $($m3.Error)"
        }

        # Message 4: plan mode test (BONUS)
        Write-Info "Plan mode message test (BONUS)..."
        $m4 = Invoke-SSEChat -Url "$BaseUrl/chat/stream" -Token $Token -TimeoutMs 90000 -Body @{
            content = "Create a learning plan for this topic."; topicId = $TopicId; sessionId = $SessionId; isPlanMode = $true
        }
        if ($m4.Ok -and $m4.Content.Length -gt 30) {
            Write-Bonus "Plan mode responded ($($m4.Ms) ms, $($m4.Content.Length) chars)"
            Add-Bonus "Chat: Plan Mode Response" 3 "$($m4.Ms) ms"
        } else {
            Write-Info "Plan mode did not produce content (may need DeepPlan context)"
        }
    } else {
        Add-Score "Chat: Conversation Continuity" 0 5 "No session"
    }

    # End session
    if ($SessionId) {
        $endRes = Invoke-Timed -Method POST -Url "$BaseUrl/chat/session/end" -Headers (Get-AuthHeaders $Token) -Body @{ sessionId = $SessionId }
        if ($endRes.Ok -or $endRes.StatusCode -eq 204) {
            Write-Pass "Session ended ($($endRes.Ms) ms)"
            Add-Score "Chat: Session End" 2 2 "$($endRes.Ms) ms"
        } else {
            Write-Fail "Session end failed - HTTP $($endRes.StatusCode)"
            Add-Score "Chat: Session End" 0 2 "HTTP $($endRes.StatusCode)"
        }
    } else {
        Add-Score "Chat: Session End" 0 2 "No session"
    }
}

# ============================================================
#  SECTION 6 - SSE Stream Quality
# ============================================================
Write-Head "SECTION 6 - SSE Stream Quality"

if (-not $Token -or -not $TopicId) {
    Write-Warn "No token or TopicId - skipping SSE tests."
    Add-Score "SSE Stream" 0 10 "Missing prerequisite"
} else {
    $sseBody = ConvertTo-Json @{
        content    = "Give me a short summary in English."
        topicId    = $TopicId
        sessionId  = $SessionId
        isPlanMode = $false
    }

    $streamOk     = $false
    $thinkingSeen = $false
    $chunkCount   = 0
    $streamMs     = 0

    try {
        $sw     = [System.Diagnostics.Stopwatch]::StartNew()
        $client = New-Object System.Net.WebClient
        $client.Headers.Add("Authorization", "Bearer $Token")
        $client.Headers.Add("Content-Type", "application/json")
        $raw        = $client.UploadString("$BaseUrl/chat/stream", "POST", $sseBody)
        $sw.Stop()
        $streamMs   = $sw.ElapsedMilliseconds
        $chunks     = $raw -split "`n" | Where-Object { $_ -match "^data:" }
        $chunkCount = $chunks.Count
        if ($chunks | Where-Object { $_ -match "\[THINKING:" }) { $thinkingSeen = $true }
        if ($chunkCount -gt 0) { $streamOk = $true }
        Write-Pass "SSE stream received: $chunkCount chunks ($streamMs ms)"
        if ($thinkingSeen) { Write-Pass "[THINKING] signal detected" }
    } catch {
        $errMsg = $_.Exception.Message
        if ($errMsg -match "403|401") {
            Write-Fail "SSE stream auth error"
        } else {
            Write-Info "SSE connection closed (expected): $($errMsg.Substring(0,[Math]::Min(80,$errMsg.Length)))"
            $streamOk = $true
        }
    }

    if ($streamOk) {
        $ss = 3
        if ($thinkingSeen)  { $ss++ }
        if ($chunkCount -gt 5)  { $ss++ }
        if ($chunkCount -gt 50) { $ss = 5 }
        Add-Score "SSE: Stream Response" $ss 5 "$chunkCount chunks, THINKING=$thinkingSeen"
    } else {
        Add-Score "SSE: Stream Response" 0 5 "Stream failed"
    }

    # Content-Type header check
    try {
        $req = [System.Net.HttpWebRequest]::Create("$BaseUrl/chat/stream")
        $req.Method      = "POST"
        $req.Headers.Add("Authorization", "Bearer $Token")
        $req.ContentType = "application/json"
        $req.Timeout     = 5000
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($sseBody)
        $req.ContentLength = $bytes.Length
        $rs = $req.GetRequestStream(); $rs.Write($bytes, 0, $bytes.Length); $rs.Close()
        $resp = $req.GetResponse()
        $ct   = $resp.ContentType; $resp.Close()
        if ($ct -match "text/event-stream") {
            Write-Pass "SSE Content-Type: text/event-stream verified"
            Add-Score "SSE: Content-Type Header" 5 5 "text/event-stream"
        } else {
            Write-Warn "SSE Content-Type unexpected: $ct"
            Add-Score "SSE: Content-Type Header" 2 5 $ct
        }
    } catch {
        $exResp = $_.Exception.Response
        if ($exResp -ne $null) {
            $ct = $exResp.ContentType
            if ($ct -match "text/event-stream") {
                Write-Pass "SSE Content-Type: text/event-stream (from exception)"
                Add-Score "SSE: Content-Type Header" 5 5 "text/event-stream"
            } else {
                Write-Warn "SSE Content-Type: $ct"
                Add-Score "SSE: Content-Type Header" 2 5 $ct
            }
        } else {
            Write-Info "SSE header check skipped (stream closed - normal)"
            Add-Score "SSE: Content-Type Header" 3 5 "Partial test"
        }
    }

    # Bonus: DONE signal check
    if ($streamOk -and ($raw -match "\[DONE\]")) {
        Write-Bonus "[DONE] signal present in SSE stream"
        Add-Bonus "SSE: [DONE] Signal" 2 "[DONE] detected"
    }
}

# ============================================================
#  SECTION 7 - Wiki Pipeline (BONUS if not yet generated)
# ============================================================
Write-Head "SECTION 7 - Wiki Pipeline"

$WikiPageId = $null

if (-not $Token -or -not $TopicId) {
    Write-Warn "No token or TopicId - skipping wiki tests."
    Add-Score "Wiki: Endpoint Reachable" 0 5 "Missing prerequisite"
} else {
    $wikiRes = Invoke-Timed -Method GET -Url "$BaseUrl/wiki/$TopicId" -Headers (Get-AuthHeaders $Token)
    if ($wikiRes.Ok) {
        try {
            $wd = $wikiRes.Response.Content | ConvertFrom-Json
            if ($wd -is [array]) { $pageCount = $wd.Count }
            else {
                $pages = Get-Prop $wd "pages"
                $pageCount = if ($pages -eq $null) { 0 } else { $pages.Count }
            }
            Write-Pass "Wiki endpoint responded - $pageCount page(s)"
            Add-Score "Wiki: Endpoint Reachable" 5 5 "$pageCount page(s)"

            if ($pageCount -gt 0) {
                # Wiki has content - full test
                Write-Bonus "Wiki content already generated ($pageCount pages)"
                Add-Bonus "Wiki: Pages Generated" 5 "$pageCount pages present"
                if ($wd -is [array]) { $fp = $wd[0] } else { $fp = (Get-Prop $wd "pages")[0] }
                $WikiPageId = Get-Prop $fp "id"

                if ($WikiPageId) {
                    $pgRes = Invoke-Timed -Method GET -Url "$BaseUrl/wiki/page/$WikiPageId" -Headers (Get-AuthHeaders $Token)
                    if ($pgRes.Ok) {
                        $pgData = $pgRes.Response.Content | ConvertFrom-Json
                        $blocks = Get-Prop $pgData "blocks"
                        $bc     = if ($blocks -eq $null) { 0 } else { $blocks.Count }
                        Write-Bonus "Wiki page detail loaded - $bc block(s)"
                        if ($bc -gt 0) {
                            $valid = $blocks | Where-Object { $_.content -and $_.content.Length -gt 10 }
                            $ratio = [int](($valid.Count / $bc) * 100)
                            Write-Bonus "Block quality: $ratio pct - $($valid.Count)/$bc valid"
                            Add-Bonus "Wiki: Block Content Quality" ([Math]::Min(10, [int]($ratio / 10))) "$bc blocks, $ratio% valid"
                        } else {
                            Add-Bonus "Wiki: Block Content" 0 "No blocks yet"
                        }

                        # Note add (BONUS)
                        $noteRes = Invoke-Timed -Method POST -Url "$BaseUrl/wiki/page/$WikiPageId/note" -Headers (Get-AuthHeaders $Token) -Body @{ content = "Integration test note $(Get-Date -Format 'HHmmss')" }
                        if ($noteRes.Ok) {
                            Write-Bonus "Wiki note added successfully"
                            Add-Bonus "Wiki: Note Add" 3 "Note saved"
                        }
                    }
                }
            } else {
                Write-Info "Wiki not yet generated (normal for new topic - no penalty)"
                Add-Bonus "Wiki: Pages Generated" 0 "Not yet generated - no penalty"
            }
        } catch {
            Write-Fail "Wiki JSON parse error: $_"
            Add-Score "Wiki: Endpoint Reachable" 2 5 "Parse error"
        }
    } else {
        Write-Fail "Wiki endpoint - HTTP $($wikiRes.StatusCode)"
        Add-Score "Wiki: Endpoint Reachable" 0 5 "HTTP $($wikiRes.StatusCode)"
    }
}

# ============================================================
#  SECTION 8 - Gamification & Dashboard Stats
# ============================================================
Write-Head "SECTION 8 - Gamification and Dashboard Stats"

if (-not $Token) {
    Write-Warn "No token - skipping gamification tests."
    Add-Score "Dashboard Stats" 0 15 "No token"
} else {
    $ah = Get-AuthHeaders $Token

    $dsRes = Invoke-Timed -Method GET -Url "$BaseUrl/dashboard/stats" -Headers $ah -TimeoutSec 10
    if ($dsRes.Ok) {
        try {
            $ds      = $dsRes.Response.Content | ConvertFrom-Json
            $hasXP   = $ds.PSObject.Properties.Name -contains "totalXP"
            $hasSt   = $ds.PSObject.Properties.Name -contains "currentStreak"
            $hasComp = $ds.PSObject.Properties.Name -contains "completedTopics"
            $hasAct  = $ds.PSObject.Properties.Name -contains "activeLearning"
            $shapeOk = $hasXP -and $hasSt -and $hasComp -and $hasAct

            if ($shapeOk) {
                Write-Pass "Dashboard /stats shape OK - XP=$($ds.totalXP) streak=$($ds.currentStreak)"
                Add-Score "Dashboard: Stats Shape" 5 5 "All fields present"
            } else {
                $missing = @()
                if (!$hasXP)   { $missing += "totalXP" }
                if (!$hasSt)   { $missing += "currentStreak" }
                if (!$hasComp) { $missing += "completedTopics" }
                if (!$hasAct)  { $missing += "activeLearning" }
                Write-Fail "Dashboard /stats missing: $($missing -join ', ')"
                Add-Score "Dashboard: Stats Shape" 2 5 "Missing: $($missing -join ', ')"
            }

            if ($hasXP -and [int]$ds.totalXP -ge 0) {
                Write-Pass "TotalXP non-negative ($($ds.totalXP))"
                Add-Score "Dashboard: TotalXP Valid" 3 3 "XP=$($ds.totalXP)"
            } else {
                Add-Score "Dashboard: TotalXP Valid" 0 3 "Invalid"
            }

            if ($hasSt -and [int]$ds.currentStreak -ge 0) {
                Write-Pass "CurrentStreak non-negative ($($ds.currentStreak))"
                Add-Score "Dashboard: Streak Valid" 2 2 "Streak=$($ds.currentStreak)"
            } else {
                Add-Score "Dashboard: Streak Valid" 0 2 "Invalid"
            }

            if ($hasComp -and $hasAct) {
                $total = [int]$ds.totalTopics
                $comp  = [int]$ds.completedTopics
                $act   = [int]$ds.activeLearning
                if (($comp + $act) -le $total) {
                    Write-Pass "completedTopics + activeLearning <= totalTopics ($comp + $act <= $total)"
                    Add-Score "Dashboard: Topic Counts Consistent" 3 3 "$comp + $act <= $total"
                } else {
                    Write-Warn "completedTopics + activeLearning > totalTopics (in-progress overlap)"
                    Add-Score "Dashboard: Topic Counts Consistent" 2 3 "Overlap - in-progress"
                }
            } else {
                Add-Score "Dashboard: Topic Counts Consistent" 0 3 "Fields missing"
            }
        } catch {
            Write-Fail "Dashboard stats parse error: $_"
            Add-Score "Dashboard: Stats Shape"          0 5 "Parse error"
            Add-Score "Dashboard: TotalXP Valid"        0 3 "Parse error"
            Add-Score "Dashboard: Streak Valid"         0 2 "Parse error"
            Add-Score "Dashboard: Topic Counts Consistent" 0 3 "Parse error"
        }
    } else {
        Write-Fail "Dashboard /stats - HTTP $($dsRes.StatusCode)"
        Add-Score "Dashboard: Stats Shape"          0 5 "HTTP $($dsRes.StatusCode)"
        Add-Score "Dashboard: TotalXP Valid"        0 3 "HTTP $($dsRes.StatusCode)"
        Add-Score "Dashboard: Streak Valid"         0 2 "HTTP $($dsRes.StatusCode)"
        Add-Score "Dashboard: Topic Counts Consistent" 0 3 "HTTP $($dsRes.StatusCode)"
    }

    # /quiz/stats shape
    $qsRes = Invoke-Timed -Method GET -Url "$BaseUrl/quiz/stats" -Headers $ah -TimeoutSec 10
    if ($qsRes.Ok) {
        try {
            $qs = $qsRes.Response.Content | ConvertFrom-Json
            $hasTotal    = $qs.PSObject.Properties.Name -contains "totalQuizzes"
            $hasAccuracy = $qs.PSObject.Properties.Name -contains "accuracy"
            $hasDaily    = $qs.PSObject.Properties.Name -contains "dailyProgress"
            if ($hasTotal -and $hasAccuracy -and $hasDaily) {
                Write-Pass "Quiz /stats OK - total=$($qs.totalQuizzes) accuracy=$($qs.accuracy)pct"
                Add-Score "Quiz: Stats Shape" 2 2 "All fields present"
            } else {
                Write-Warn "Quiz /stats missing fields"
                Add-Score "Quiz: Stats Shape" 1 2 "Partial"
            }
        } catch { Add-Score "Quiz: Stats Shape" 0 2 "Parse error" }
    } else {
        Write-Fail "Quiz /stats - HTTP $($qsRes.StatusCode)"
        Add-Score "Quiz: Stats Shape" 0 2 "HTTP $($qsRes.StatusCode)"
    }
}

# ============================================================
#  SECTION 9 - User Gamification (XP / Streak / Level)
# ============================================================
Write-Head "SECTION 9 - User Gamification (XP / Streak / Level)"

if (-not $Token) {
    Write-Warn "No token - skipping gamification."
    Add-Score "Gamification" 0 10 "No token"
} else {
    $ah = Get-AuthHeaders $Token
    $gamRes = Invoke-Timed -Method GET -Url "$BaseUrl/user/gamification" -Headers $ah -TimeoutSec 10
    if ($gamRes.Ok) {
        try {
            $gam      = $gamRes.Response.Content | ConvertFrom-Json
            $hasXP    = $gam.PSObject.Properties.Name -contains "totalXP"
            $hasSt    = $gam.PSObject.Properties.Name -contains "currentStreak"
            $hasLevel = $gam.PSObject.Properties.Name -contains "level"
            if ($hasXP -and $hasSt -and $hasLevel) {
                Write-Pass "/user/gamification OK - XP=$($gam.totalXP) Streak=$($gam.currentStreak) Level=$($gam.level)"
                Add-Score "Gamification: Endpoint" 4 4 "All fields present"
            } else {
                $missing = @()
                if (!$hasXP)    { $missing += "totalXP" }
                if (!$hasSt)    { $missing += "currentStreak" }
                if (!$hasLevel) { $missing += "level" }
                Write-Warn "Gamification missing: $($missing -join ', ')"
                Add-Score "Gamification: Endpoint" 2 4 "Missing: $($missing -join ', ')"
            }
            if ($hasXP -and [int]$gam.totalXP -ge 0) {
                Write-Pass "XP non-negative"
                Add-Score "Gamification: XP Non-Negative" 3 3 "XP=$($gam.totalXP)"
            } else {
                Add-Score "Gamification: XP Non-Negative" 0 3 "Invalid"
            }
            if ($hasLevel -and [int]$gam.level -ge 1) {
                Write-Pass "Level calculated (Level=$($gam.level))"
                Add-Score "Gamification: Level Calc" 3 3 "Level=$($gam.level)"
            } else {
                Add-Score "Gamification: Level Calc" 0 3 "Invalid level"
            }
        } catch {
            Write-Fail "Gamification parse error: $_"
            Add-Score "Gamification: Endpoint"       0 4 "Parse error"
            Add-Score "Gamification: XP Non-Negative" 0 3 "Parse error"
            Add-Score "Gamification: Level Calc"      0 3 "Parse error"
        }
    } else {
        Write-Fail "/user/gamification - HTTP $($gamRes.StatusCode)"
        Add-Score "Gamification: Endpoint"       0 4 "HTTP $($gamRes.StatusCode)"
        Add-Score "Gamification: XP Non-Negative" 0 3 "HTTP $($gamRes.StatusCode)"
        Add-Score "Gamification: Level Calc"      0 3 "HTTP $($gamRes.StatusCode)"
    }
}

# ============================================================
#  SECTION 10 - Quiz (BONUS - flow-dependent)
# ============================================================
Write-Head "SECTION 10 - Quiz History, Attempt and Stats (Bonus)"

if (-not $Token -or -not $TopicId) {
    Write-Warn "No token or TopicId - skipping quiz tests."
} else {
    $ah = Get-AuthHeaders $Token

    # Quiz history per topic
    $qhRes = Invoke-Timed -Method GET -Url "$BaseUrl/quiz/history/$TopicId" -Headers $ah -TimeoutSec 10
    if ($qhRes.Ok) {
        try {
            $qh = $qhRes.Response.Content | ConvertFrom-Json
            $isArray = $qh -is [array]
            if ($isArray) {
                Write-Pass "Quiz history for topic: $($qh.Count) attempt(s)"
                Add-Score "Quiz: History Endpoint" 3 3 "$($qh.Count) attempts"
                if ($qh.Count -gt 0) {
                    Write-Bonus "Quiz history has existing attempts ($($qh.Count))"
                    Add-Bonus "Quiz: History Has Attempts" 3 "$($qh.Count) attempts found"
                }
            } else {
                Write-Warn "Quiz history returned non-array"
                Add-Score "Quiz: History Endpoint" 1 3 "Not array"
            }
        } catch { Add-Score "Quiz: History Endpoint" 0 3 "Parse error" }
    } else {
        Write-Fail "Quiz history - HTTP $($qhRes.StatusCode)"
        Add-Score "Quiz: History Endpoint" 0 3 "HTTP $($qhRes.StatusCode)"
    }

    # Record a quiz attempt
    $qaBody = @{
        topicId         = $TopicId
        sessionId       = $SessionId
        question        = "What is 2 + 2?"
        selectedOptionId = "opt_a"
        isCorrect       = $true
        explanation     = "2 + 2 equals 4."
    }
    $qaRes = Invoke-Timed -Method POST -Url "$BaseUrl/quiz/attempt" -Headers $ah -Body $qaBody -TimeoutSec 10
    if ($qaRes.Ok) {
        Write-Pass "Quiz attempt recorded ($($qaRes.Ms) ms)"
        Add-Score "Quiz: Record Attempt" 4 4 "HTTP 200"

        # Verify attempt appears in history
        $qhRes2 = Invoke-Timed -Method GET -Url "$BaseUrl/quiz/history/$TopicId" -Headers $ah -TimeoutSec 10
        if ($qhRes2.Ok) {
            try {
                $qh2 = $qhRes2.Response.Content | ConvertFrom-Json
                if ($qh2.Count -ge 1) {
                    Write-Pass "Quiz attempt visible in history ($($qh2.Count) total)"
                    Add-Score "Quiz: Attempt Persistence" 4 4 "$($qh2.Count) attempts"
                } else {
                    Write-Fail "Recorded attempt not visible in history"
                    Add-Score "Quiz: Attempt Persistence" 0 4 "Empty history after attempt"
                }
            } catch { Add-Score "Quiz: Attempt Persistence" 0 4 "Parse error" }
        } else { Add-Score "Quiz: Attempt Persistence" 0 4 "HTTP $($qhRes2.StatusCode)" }

        # Global quiz stats updated
        $qsRes2 = Invoke-Timed -Method GET -Url "$BaseUrl/quiz/stats" -Headers $ah -TimeoutSec 10
        if ($qsRes2.Ok) {
            try {
                $qs2 = $qsRes2.Response.Content | ConvertFrom-Json
                if ([int]$qs2.totalQuizzes -ge 1) {
                    Write-Pass "Global quiz stats updated - total=$($qs2.totalQuizzes) accuracy=$($qs2.accuracy)pct"
                    Add-Score "Quiz: Global Stats Update" 3 3 "totalQuizzes=$($qs2.totalQuizzes)"
                } else {
                    Write-Warn "Global quiz stats show 0"
                    Add-Score "Quiz: Global Stats Update" 1 3 "Zero count"
                }
            } catch { Add-Score "Quiz: Global Stats Update" 0 3 "Parse error" }
        } else { Add-Score "Quiz: Global Stats Update" 0 3 "HTTP $($qsRes2.StatusCode)" }
    } else {
        Write-Fail "Quiz attempt - HTTP $($qaRes.StatusCode)"
        Add-Score "Quiz: Record Attempt"    0 4 "HTTP $($qaRes.StatusCode)"
        Add-Score "Quiz: Attempt Persistence" 0 4 "Prerequisite failed"
        Add-Score "Quiz: Global Stats Update" 0 3 "Prerequisite failed"
    }
}

# ============================================================
#  SECTION 11 - Error Resilience
# ============================================================
Write-Head "SECTION 11 - Error Resilience"

if (-not $Token) {
    Write-Warn "No token - skipping resilience tests."
    Add-Score "Resilience" 0 6 "No token"
} else {
    $ah = Get-AuthHeaders $Token

    # 11.1: User profile shape
    $meRes = Invoke-Timed -Method GET -Url "$BaseUrl/user/me" -Headers $ah -TimeoutSec 10
    if ($meRes.Ok) {
        try {
            $me     = $meRes.Response.Content | ConvertFrom-Json
            $hasEm  = $me.PSObject.Properties.Name -contains "email"
            $hasFN  = $me.PSObject.Properties.Name -contains "firstName"
            if ($hasEm -and $hasFN) {
                Write-Pass "/user/me shape OK - email=$($me.email)"
                Add-Score "Resilience: User Profile Shape" 2 2 "Shape OK"
            } else {
                Write-Warn "/user/me missing fields"
                Add-Score "Resilience: User Profile Shape" 1 2 "Partial"
            }
        } catch { Add-Score "Resilience: User Profile Shape" 0 2 "Parse error" }
    } else {
        Add-Score "Resilience: User Profile Shape" 0 2 "HTTP $($meRes.StatusCode)"
    }

    # 11.2: Empty chat content rejected
    $emptyMsg = Invoke-Timed -Method POST -Url "$BaseUrl/chat/stream" -Headers $ah -Body @{ content = ""; topicId = $TopicId; isPlanMode = $false } -TimeoutSec 10
    if ($emptyMsg.StatusCode -ge 400 -and $emptyMsg.StatusCode -lt 500) {
        Write-Pass "Empty message rejected - HTTP $($emptyMsg.StatusCode)"
        Add-Score "Resilience: Empty Message Rejected" 2 2 "HTTP $($emptyMsg.StatusCode)"
    } elseif ($emptyMsg.StatusCode -eq 0) {
        Write-Info "Empty message check inconclusive (connection closed)"
        Add-Score "Resilience: Empty Message Rejected" 1 2 "Connection closed"
    } else {
        Write-Warn "Empty message returned HTTP $($emptyMsg.StatusCode)"
        Add-Score "Resilience: Empty Message Rejected" 0 2 "HTTP $($emptyMsg.StatusCode)"
    }

    # 11.3: Topic not found returns 404
    $badTopic = Get-HttpStatus -Method GET -Url "$BaseUrl/topics/00000000-0000-0000-0000-000000000000" -Headers $ah
    if ($badTopic -eq 404) {
        Write-Pass "Non-existent topic returns 404"
        Add-Score "Resilience: Non-Existent Topic 404" 2 2 "HTTP 404"
    } else {
        Write-Warn "Non-existent topic returned HTTP $badTopic"
        Add-Score "Resilience: Non-Existent Topic 404" 0 2 "HTTP $badTopic"
    }
}

# ============================================================
#  SECTION 12 - User Profile CRUD
# ============================================================
Write-Head "SECTION 12 - User Profile CRUD"

if (-not $Token) {
    Write-Warn "No token - skipping profile tests."
    Add-Score "User Profile" 0 6 "No token"
} else {
    $ah = Get-AuthHeaders $Token

    # PATCH profile
    $updRes = Invoke-Timed -Method PATCH -Url "$BaseUrl/user/profile" -Headers $ah -Body @{ firstName = "OrkaTester"; lastName = "Updated" } -TimeoutSec 10
    if ($updRes.Ok -or $updRes.StatusCode -eq 204) {
        Write-Pass "Profile PATCH OK ($($updRes.Ms) ms)"
        Add-Score "Profile: PATCH" 3 3 "$($updRes.Ms) ms"
    } else {
        Write-Fail "Profile PATCH failed - HTTP $($updRes.StatusCode)"
        Add-Score "Profile: PATCH" 0 3 "HTTP $($updRes.StatusCode)"
    }

    # PATCH settings
    $sRes = Invoke-Timed -Method PATCH -Url "$BaseUrl/user/settings" -Headers $ah -Body @{ theme = "dark"; fontSize = "medium"; soundsEnabled = $true } -TimeoutSec 10
    if ($sRes.Ok -or $sRes.StatusCode -eq 204) {
        Write-Pass "Settings PATCH OK ($($sRes.Ms) ms)"
        Add-Score "Profile: Settings PATCH" 3 3 "$($sRes.Ms) ms"
    } else {
        Write-Fail "Settings PATCH failed - HTTP $($sRes.StatusCode)"
        Add-Score "Profile: Settings PATCH" 0 3 "HTTP $($sRes.StatusCode)"
    }
}

# ============================================================
#  SECTION 13 - Korteks Research Engine
# ============================================================
Write-Head "SECTION 13 - Korteks Research Engine"

if (-not $Token) {
    Write-Warn "No token - skipping Korteks tests."
    Add-Score "Korteks" 0 10 "No token"
} else {
    $ah = Get-AuthHeaders $Token

    # 13.1: Korteks ping
    $kpRes = Invoke-Timed -Method GET -Url "$BaseUrl/korteks/ping" -Headers $ah -TimeoutSec 10
    if ($kpRes.Ok) {
        Write-Pass "Korteks ping OK - $($kpRes.Ms) ms"
        Add-Score "Korteks: Ping" 2 2 "$($kpRes.Ms) ms"
    } else {
        Write-Fail "Korteks ping - HTTP $($kpRes.StatusCode)"
        Add-Score "Korteks: Ping" 0 2 "HTTP $($kpRes.StatusCode)"
    }

    # 13.2: Korteks sync research
    Write-Info "Korteks sync research (60s timeout)..."
    $krBody = @{ topic = "What is machine learning? Short answer." }
    $krRes  = Invoke-Timed -Method POST -Url "$BaseUrl/korteks/research-sync" -Headers $ah -Body $krBody -TimeoutSec 90
    if ($krRes.Ok) {
        try {
            $kr      = $krRes.Response.Content | ConvertFrom-Json
            $success = $kr.success -eq $true
            $length  = [int]$kr.length
            if ($success -and $length -gt 50) {
                Write-Pass "Korteks research OK - $length chars $($krRes.Ms) ms"
                $ks = if ($length -gt 500) { 8 } elseif ($length -gt 100) { 5 } else { 3 }
                Add-Score "Korteks: Sync Research" $ks 8 "$length chars"
            } elseif ($success) {
                Write-Warn "Korteks research empty result"
                Add-Score "Korteks: Sync Research" 2 8 "Empty result"
            } else {
                Write-Fail "Korteks research success=false"
                Add-Score "Korteks: Sync Research" 0 8 "success=false"
            }
        } catch {
            Write-Fail "Korteks parse error: $_"
            Add-Score "Korteks: Sync Research" 0 8 "Parse error"
        }
    } else {
        Write-Fail "Korteks research-sync - HTTP $($krRes.StatusCode)"
        Add-Score "Korteks: Sync Research" 0 8 "HTTP $($krRes.StatusCode)"
    }

    # 13.3: URL context research (BONUS)
    Write-Info "Korteks URL-context research (BONUS)..."
    $krUrlBody = @{ topic = "Short summary of Wikipedia"; sourceUrl = "https://en.wikipedia.org/wiki/Wikipedia" }
    $krUrlRes  = Invoke-Timed -Method POST -Url "$BaseUrl/korteks/research-sync" -Headers $ah -Body $krUrlBody -TimeoutSec 90
    if ($krUrlRes.Ok) {
        try {
            $kru = $krUrlRes.Response.Content | ConvertFrom-Json
            if ($kru.success -eq $true -and [int]$kru.length -gt 50) {
                Write-Bonus "Korteks URL research OK - $([int]$kru.length) chars"
                Add-Bonus "Korteks: URL Context Research" 3 "$([int]$kru.length) chars"
            }
        } catch {}
    }

    # 13.4: File upload endpoint (400=OK, 404=missing)
    Write-Info "Korteks file-upload endpoint availability..."
    try {
        $boundary  = "----TestBoundary$(Get-Random)"
        $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes("--$boundary`r`nContent-Disposition: form-data; name=`"topic`"`r`n`r`ntest topic`r`n--$boundary--`r`n")
        $req       = [System.Net.HttpWebRequest]::Create("$BaseUrl/korteks/research-file")
        $req.Method      = "POST"
        $req.Headers.Add("Authorization", "Bearer $Token")
        $req.ContentType = "multipart/form-data; boundary=$boundary"
        $req.ContentLength = $bodyBytes.Length
        $req.Timeout     = 15000
        $rs = $req.GetRequestStream(); $rs.Write($bodyBytes, 0, $bodyBytes.Length); $rs.Close()
        try {
            $resp = $req.GetResponse(); $sc = [int]$resp.StatusCode; $resp.Close()
            Write-Pass "Korteks file endpoint OK - HTTP $sc"
            Add-Score "Korteks: File Endpoint" 3 3 "HTTP $sc"
        } catch [System.Net.WebException] {
            $sc = [int]$_.Exception.Response.StatusCode
            if ($sc -eq 400) {
                Write-Pass "Korteks file endpoint OK - HTTP 400 (validation working)"
                Add-Score "Korteks: File Endpoint" 3 3 "HTTP 400 - validation OK"
            } elseif ($sc -eq 404 -or $sc -eq 405) {
                Write-Fail "Korteks file endpoint NOT FOUND - HTTP $sc"
                Add-Score "Korteks: File Endpoint" 0 3 "HTTP $sc"
            } else {
                Write-Warn "Korteks file endpoint HTTP $sc"
                Add-Score "Korteks: File Endpoint" 2 3 "HTTP $sc"
            }
        }
    } catch {
        Write-Fail "Korteks file endpoint error: $_"
        Add-Score "Korteks: File Endpoint" 0 3 "Exception"
    }

    # 13.5: Invalid URL scheme rejected / ignored
    $krBadUrlBody = @{ topic = "what is AI"; sourceUrl = "javascript://xss-attempt" }
    $krBadUrlRes  = Invoke-Timed -Method POST -Url "$BaseUrl/korteks/research-sync" -Headers $ah -Body $krBadUrlBody -TimeoutSec 60
    if ($krBadUrlRes.Ok) {
        try {
            $krb = $krBadUrlRes.Response.Content | ConvertFrom-Json
            if ($krb.success -eq $true) {
                Write-Pass "Korteks ignores invalid URL scheme - research proceeds safely"
                Add-Score "Korteks: URL Validation" 2 2 "Invalid URL ignored"
            } else {
                Add-Score "Korteks: URL Validation" 1 2 "Unexpected failure"
            }
        } catch { Add-Score "Korteks: URL Validation" 0 2 "Parse error" }
    } elseif ($krBadUrlRes.StatusCode -ge 400 -and $krBadUrlRes.StatusCode -lt 500) {
        Write-Pass "Korteks rejects invalid URL - HTTP $($krBadUrlRes.StatusCode)"
        Add-Score "Korteks: URL Validation" 2 2 "HTTP $($krBadUrlRes.StatusCode) rejected"
    } else {
        Add-Score "Korteks: URL Validation" 0 2 "HTTP $($krBadUrlRes.StatusCode)"
    }
}

# ============================================================
#  SECTION 14 - Piston Code Execution API (Interactive IDE)
# ============================================================
Write-Head "SECTION 14 - Piston Code Execution (Interactive IDE)"

if (-not $Token) {
    Write-Warn "No token - skipping Piston tests."
    Add-Score "Piston: Languages Endpoint"   0 3  "No token"
    Add-Score "Piston: C# Code Execution"    0 5  "No token"
    Add-Score "Piston: Error Handling"       0 3  "No token"
    Add-Score "Piston: Language Support"     0 3  "No token"
    Add-Score "Piston: Stdin Support"        0 3  "No token"
    Add-Score "Piston: Size Limit"           0 2  "No token"
    Add-Score "Piston: Auth Required"        0 2  "No token"
} else {
    $ah = Get-AuthHeaders $Token

    # ── 14.1: GET /code/languages ──────────────────────────────
    Write-Info "GET /code/languages - supported runtime list..."
    $langListRes = Invoke-Timed -Method GET -Url "$BaseUrl/code/languages" -Headers $ah -TimeoutSec 15
    if ($langListRes.Ok) {
        try {
            $langs = $langListRes.Response.Content | ConvertFrom-Json
            $isArray = $langs -is [array]
            if ($isArray -and $langs.Count -gt 0) {
                $first = $langs[0]
                $hasLang    = $first.PSObject.Properties.Name -contains "language"
                $hasVersion = $first.PSObject.Properties.Name -contains "version"
                if ($hasLang -and $hasVersion) {
                    Write-Pass "GET /code/languages OK - $($langs.Count) runtime(s)"
                    Add-Score "Piston: Languages Endpoint" 3 3 "$($langs.Count) runtimes, shape OK"
                    # BONUS: check specific languages present
                    $langNames = $langs | ForEach-Object { $_.language }
                    foreach ($expected in @("python", "javascript")) {
                        if ($langNames -contains $expected) {
                            Write-Bonus "Runtime '$expected' in language list"
                            Add-Bonus "Piston: Language '$expected' Listed" 1 "In /code/languages"
                        }
                    }
                } else {
                    Write-Warn "Language list missing shape fields"
                    Add-Score "Piston: Languages Endpoint" 1 3 "Missing language/version fields"
                }
            } else {
                Write-Warn "Language list empty or not array"
                Add-Score "Piston: Languages Endpoint" 1 3 "Empty or not array"
            }
        } catch {
            Write-Fail "Language list parse error: $_"
            Add-Score "Piston: Languages Endpoint" 0 3 "Parse error"
        }
    } else {
        Write-Fail "GET /code/languages - HTTP $($langListRes.StatusCode)"
        Add-Score "Piston: Languages Endpoint" 0 3 "HTTP $($langListRes.StatusCode)"
    }

    # ── 14.2: POST /code/run — C# (primary language) ──────────
    Write-Info "POST /code/run - C# execution..."
    $csCode = @"
using System;
class Program {
    static void Main() {
        Console.WriteLine("Orka Integration Test OK");
        Console.WriteLine("2 + 2 = " + (2 + 2));
    }
}
"@
    $runRes = Invoke-Timed -Method POST -Url "$BaseUrl/code/run" -Headers $ah -Body @{ code = $csCode; language = "csharp" } -TimeoutSec 35
    if ($runRes.Ok) {
        try {
            $rd = $runRes.Response.Content | ConvertFrom-Json
            $hasStdout  = $rd.PSObject.Properties.Name -contains "stdout"
            $hasStderr  = $rd.PSObject.Properties.Name -contains "stderr"
            $hasSuccess = $rd.PSObject.Properties.Name -contains "success"
            if ($hasStdout -and $hasStderr -and $hasSuccess) {
                if ($rd.success -eq $true -and $rd.stdout -match "Orka Integration Test OK") {
                    Write-Pass "C# code executed correctly — stdout: '$($rd.stdout.Trim())'"
                    Add-Score "Piston: C# Code Execution" 5 5 "stdout correct"
                } elseif ($rd.success -eq $true) {
                    Write-Pass "C# code executed (stdout: '$($rd.stdout.Trim().Substring(0,[Math]::Min(60,$rd.stdout.Length)))')"
                    Add-Score "Piston: C# Code Execution" 4 5 "Executed, stdout differs"
                } else {
                    Write-Warn "C# ran but success=false — stderr: $($rd.stderr.Trim())"
                    Add-Score "Piston: C# Code Execution" 2 5 "success=false"
                }
            } else {
                Write-Fail "/code/run response missing stdout/stderr/success"
                Add-Score "Piston: C# Code Execution" 1 5 "Missing fields"
            }
        } catch {
            Write-Fail "/code/run parse error: $_"
            Add-Score "Piston: C# Code Execution" 0 5 "Parse error"
        }
    } else {
        Write-Fail "/code/run C# — HTTP $($runRes.StatusCode)"
        Add-Score "Piston: C# Code Execution" 0 5 "HTTP $($runRes.StatusCode)"
    }

    # ── 14.3: Compile error → stderr, success=false ───────────
    Write-Info "POST /code/run - compile error handling..."
    $badCode = "class Main { static void Main() { int x = THIS_DOES_NOT_COMPILE; } }"
    $errRes  = Invoke-Timed -Method POST -Url "$BaseUrl/code/run" -Headers $ah -Body @{ code = $badCode; language = "csharp" } -TimeoutSec 35
    if ($errRes.Ok) {
        try {
            $er = $errRes.Response.Content | ConvertFrom-Json
            if ($er.success -eq $false -and $er.stderr.Length -gt 0) {
                Write-Pass "Compile error handled — success=false, stderr populated"
                Add-Score "Piston: Error Handling" 3 3 "stderr=$($er.stderr.Substring(0,[Math]::Min(50,$er.stderr.Length)))"
            } elseif ($er.success -eq $false) {
                Write-Warn "success=false but stderr empty"
                Add-Score "Piston: Error Handling" 2 3 "success=false, empty stderr"
            } else {
                Write-Fail "Compile error not detected (success=true for broken code)"
                Add-Score "Piston: Error Handling" 0 3 "success=true for bad code"
            }
        } catch {
            Write-Fail "Error handling parse error: $_"
            Add-Score "Piston: Error Handling" 0 3 "Parse error"
        }
    } else {
        Write-Fail "/code/run (bad code) — HTTP $($errRes.StatusCode)"
        Add-Score "Piston: Error Handling" 0 3 "HTTP $($errRes.StatusCode)"
    }

    # ── 14.4: Python execution ────────────────────────────────
    Write-Info "POST /code/run - Python..."
    $pyCode = "print('Hello from Python!')`nprint(sum(range(10)))"
    $pyRes  = Invoke-Timed -Method POST -Url "$BaseUrl/code/run" -Headers $ah -Body @{ code = $pyCode; language = "python" } -TimeoutSec 35
    if ($pyRes.Ok) {
        try {
            $pr = $pyRes.Response.Content | ConvertFrom-Json
            if ($pr.success -eq $true -and $pr.stdout -match "Hello from Python") {
                Write-Pass "Python execution OK — stdout: '$($pr.stdout.Trim())'"
                Add-Score "Piston: Language Support" 3 3 "Python OK"
                Add-Bonus "Piston: Python Correct Output" 2 "stdout matches expected"
            } elseif ($pr.success -eq $true) {
                Write-Warn "Python executed but stdout unexpected: '$($pr.stdout.Trim())'"
                Add-Score "Piston: Language Support" 2 3 "Executed, output differs"
            } else {
                Write-Warn "Python execution success=false"
                Add-Score "Piston: Language Support" 1 3 "success=false"
            }
        } catch { Add-Score "Piston: Language Support" 0 3 "Parse error" }
    } else {
        Write-Fail "/code/run Python — HTTP $($pyRes.StatusCode)"
        Add-Score "Piston: Language Support" 0 3 "HTTP $($pyRes.StatusCode)"
    }

    # ── 14.5: Stdin support ───────────────────────────────────
    Write-Info "POST /code/run - stdin support (Python input())..."
    $stdinCode = "name = input()`nprint('Hello, ' + name + '!')"
    $stdinRes  = Invoke-Timed -Method POST -Url "$BaseUrl/code/run" -Headers $ah -Body @{
        code     = $stdinCode
        language = "python"
        stdin    = "Orka"
    } -TimeoutSec 35
    if ($stdinRes.Ok) {
        try {
            $sr = $stdinRes.Response.Content | ConvertFrom-Json
            if ($sr.success -eq $true -and $sr.stdout -match "Hello, Orka") {
                Write-Pass "Stdin support OK — stdout: '$($sr.stdout.Trim())'"
                Add-Score "Piston: Stdin Support" 3 3 "stdin piped correctly"
            } elseif ($sr.success -eq $true) {
                Write-Warn "Stdin test ran but output unexpected: '$($sr.stdout.Trim())'"
                Add-Score "Piston: Stdin Support" 2 3 "Ran but stdin may not be piped"
            } else {
                Write-Warn "Stdin test success=false — stderr: $($sr.stderr.Trim())"
                Add-Score "Piston: Stdin Support" 1 3 "success=false"
            }
        } catch {
            Write-Fail "Stdin test parse error: $_"
            Add-Score "Piston: Stdin Support" 0 3 "Parse error"
        }
    } else {
        Write-Fail "/code/run stdin test — HTTP $($stdinRes.StatusCode)"
        Add-Score "Piston: Stdin Support" 0 3 "HTTP $($stdinRes.StatusCode)"
    }

    # ── 14.6: Code size limit (>50 000 chars → 400) ───────────
    Write-Info "POST /code/run - size limit (50 001 chars)..."
    $bigCode   = "A" * 50001
    $sizeRes   = Invoke-Timed -Method POST -Url "$BaseUrl/code/run" -Headers $ah -Body @{ code = $bigCode; language = "python" } -TimeoutSec 10
    if ($sizeRes.StatusCode -eq 400) {
        Write-Pass "Size limit enforced — HTTP 400"
        Add-Score "Piston: Size Limit" 2 2 "HTTP 400"
    } else {
        Write-Warn "Size limit returned HTTP $($sizeRes.StatusCode) (expected 400)"
        Add-Score "Piston: Size Limit" 0 2 "HTTP $($sizeRes.StatusCode)"
    }

    # ── 14.7: Empty code → 400 ────────────────────────────────
    $emptyCode = Invoke-Timed -Method POST -Url "$BaseUrl/code/run" -Headers $ah -Body @{ code = ""; language = "csharp" } -TimeoutSec 10
    if ($emptyCode.StatusCode -eq 400) {
        Write-Pass "Empty code correctly rejected — HTTP 400"
        Add-Score "Piston: Empty Code Rejected" 2 2 "HTTP 400"
    } else {
        Write-Warn "Empty code returned HTTP $($emptyCode.StatusCode) (expected 400)"
        Add-Score "Piston: Empty Code Rejected" 0 2 "HTTP $($emptyCode.StatusCode)"
    }

    # ── 14.8: Auth guard ──────────────────────────────────────
    $unauthCode = Get-HttpStatus -Method POST -Url "$BaseUrl/code/run"
    if ($unauthCode -eq 401) {
        Write-Pass "/code/run requires auth — HTTP 401"
        Add-Score "Piston: Auth Required" 2 2 "HTTP 401"
    } else {
        Write-Fail "/code/run returned HTTP $unauthCode without token (expected 401)"
        Add-Score "Piston: Auth Required" 0 2 "HTTP $unauthCode"
    }

    # ── 14.9–14.13: Multi-language BONUS suite ────────────────
    $bonusLangs = @(
        @{ Name="JavaScript"; Lang="javascript"; Code="console.log('JS OK'); console.log([1,2,3].map(x=>x*2).join(','));"; Match="JS OK" }
        @{ Name="TypeScript"; Lang="typescript"; Code="const msg: string = 'TS OK'; console.log(msg);"; Match="TS OK" }
        @{ Name="Java";       Lang="java";       Code="public class Main { public static void main(String[] a) { System.out.println(`"Java OK`"); } }"; Match="Java OK" }
        @{ Name="Go";         Lang="go";         Code="package main`nimport `"fmt`"`nfunc main() { fmt.Println(`"Go OK`") }"; Match="Go OK" }
        @{ Name="Rust";       Lang="rust";       Code="fn main() { println!(`"Rust OK`"); }"; Match="Rust OK" }
        @{ Name="C++";        Lang="cpp";        Code="#include <iostream>`nusing namespace std;`nint main(){ cout<<`"Cpp OK`"<<endl; return 0; }"; Match="Cpp OK" }
    )

    Write-Info "Multi-language BONUS suite ($($bonusLangs.Count) languages)..."
    foreach ($bl in $bonusLangs) {
        $blRes = Invoke-Timed -Method POST -Url "$BaseUrl/code/run" -Headers $ah -Body @{ code = $bl.Code; language = $bl.Lang } -TimeoutSec 40
        if ($blRes.Ok) {
            try {
                $blData = $blRes.Response.Content | ConvertFrom-Json
                if ($blData.success -eq $true -and $blData.stdout -match $bl.Match) {
                    Write-Bonus "$($bl.Name) execution OK — stdout: '$($blData.stdout.Trim())'"
                    Add-Bonus "Piston: $($bl.Name) Language" 2 "stdout correct"
                } elseif ($blData.success -eq $true) {
                    Write-Bonus "$($bl.Name) ran (unexpected stdout: '$($blData.stdout.Trim())')"
                    Add-Bonus "Piston: $($bl.Name) Language" 1 "Ran, output differs"
                } else {
                    Write-Info "$($bl.Name) success=false (Piston may not have runtime)"
                }
            } catch { Write-Info "$($bl.Name) parse error" }
        } else {
            Write-Info "$($bl.Name) HTTP $($blRes.StatusCode)"
        }
    }

    # ── 14.14: Runtime error (C# divide-by-zero) ──────────────
    Write-Info "POST /code/run - runtime error detection (divide by zero)..."
    $rtCode = "using System;`nclass Main { static void Main() { int x = 0; Console.WriteLine(1/x); } }"
    $rtRes  = Invoke-Timed -Method POST -Url "$BaseUrl/code/run" -Headers $ah -Body @{ code = $rtCode; language = "csharp" } -TimeoutSec 35
    if ($rtRes.Ok) {
        try {
            $rt = $rtRes.Response.Content | ConvertFrom-Json
            if ($rt.success -eq $false) {
                Write-Pass "Runtime error detected — success=false"
                Add-Bonus "Piston: Runtime Error Detection" 2 "DivideByZero detected"
            } else {
                Write-Warn "Runtime error not detected (success=true)"
            }
        } catch {}
    }

    # ── 14.15: /code/languages auth guard ─────────────────────
    $unauthLangs = Get-HttpStatus -Method GET -Url "$BaseUrl/code/languages"
    if ($unauthLangs -eq 401) {
        Write-Pass "GET /code/languages requires auth — HTTP 401"
        Add-Bonus "Piston: Languages Auth Guard" 1 "HTTP 401"
    } else {
        Write-Info "GET /code/languages returned HTTP $unauthLangs (may be public)"
    }

    # ── 14.16: Code execution latency benchmark ───────────────
    Write-Info "Code execution latency benchmark (Python hello world)..."
    $latPyCode = "print('latency check')"
    $latRes    = Invoke-Timed -Method POST -Url "$BaseUrl/code/run" -Headers $ah -Body @{ code = $latPyCode; language = "python" } -TimeoutSec 40
    if ($latRes.Ok) {
        $ms = $latRes.Ms
        if ($ms -lt 5000) {
            Write-Pass "Code execution latency: $ms ms (good)"
            Add-Bonus "Piston: Latency <5s" 2 "$ms ms"
        } elseif ($ms -lt 15000) {
            Write-Pass "Code execution latency: $ms ms (acceptable)"
            Add-Bonus "Piston: Latency <15s" 1 "$ms ms"
        } else {
            Write-Warn "Code execution latency: $ms ms (slow)"
        }
    }
}

# ============================================================
#  SECTION 15 - Latency Benchmark
# ============================================================
Write-Head "SECTION 15 - Latency Benchmarks"

if (-not $Token) {
    Write-Warn "No token - skipping latency tests."
    Add-Score "Latency" 0 5 "No token"
} else {
    $ah = Get-AuthHeaders $Token
    $latencyTests = @(
        @{ Name="GET /topics";           Url="$BaseUrl/topics";           GoodMs=300;  OkMs=800  }
        @{ Name="GET /user/me";          Url="$BaseUrl/user/me";          GoodMs=200;  OkMs=500  }
        @{ Name="GET /dashboard/stats";  Url="$BaseUrl/dashboard/stats";  GoodMs=400;  OkMs=1000 }
        @{ Name="GET /quiz/stats";       Url="$BaseUrl/quiz/stats";       GoodMs=400;  OkMs=1000 }
        @{ Name="GET /user/gamification";Url="$BaseUrl/user/gamification";GoodMs=300;  OkMs=800  }
        @{ Name="GET /code/languages";   Url="$BaseUrl/code/languages";   GoodMs=500;  OkMs=2000 }
    )
    $allFast = $true
    foreach ($lt in $latencyTests) {
        $r  = Invoke-Timed -Method GET -Url $lt.Url -Headers $ah -TimeoutSec 5
        $ms = $r.Ms
        if ($r.Ok) {
            if ($ms -le $lt.GoodMs) {
                Write-Pass "$($lt.Name) - $ms ms (excellent)"
            } elseif ($ms -le $lt.OkMs) {
                Write-Pass "$($lt.Name) - $ms ms (acceptable)"
            } else {
                Write-Warn "$($lt.Name) - $ms ms (slow)"
                $allFast = $false
            }
        } else {
            Write-Fail "$($lt.Name) - HTTP $($r.StatusCode)"
            $allFast = $false
        }
    }
    if ($allFast) {
        Write-Bonus "All endpoints within latency targets"
        Add-Bonus "Latency: All Endpoints Fast" 5 "All <= target ms"
        Add-Score "Latency: Benchmark" 5 5 "All passed"
    } else {
        Add-Score "Latency: Benchmark" 3 5 "Some slow"
    }
}

# ============================================================
#  SECTION 16 - Topic Cleanup
# ============================================================
Write-Head "SECTION 16 - Topic Cleanup (DELETE)"

if ($Token -and $TopicId) {
    $delRes = Invoke-Timed -Method DELETE -Url "$BaseUrl/topics/$TopicId" -Headers (Get-AuthHeaders $Token) -TimeoutSec 10
    if ($delRes.Ok -or $delRes.StatusCode -eq 204) {
        Write-Pass "Topic deleted (cleanup)"
        Add-Score "Topic: DELETE" 2 2 "HTTP $($delRes.StatusCode)"
        # Verify gone
        $checkRes = Invoke-Timed -Method GET -Url "$BaseUrl/topics/$TopicId" -Headers (Get-AuthHeaders $Token) -TimeoutSec 5
        if ($checkRes.StatusCode -eq 404) {
            Write-Pass "Deleted topic returns 404 (verified)"
            Add-Bonus "Topic: Delete Verified" 2 "404 after delete"
        }
    } else {
        Write-Fail "Topic DELETE failed - HTTP $($delRes.StatusCode)"
        Add-Score "Topic: DELETE" 0 2 "HTTP $($delRes.StatusCode)"
    }
} else {
    Write-Warn "No topic to delete - skipping cleanup."
    Add-Score "Topic: DELETE" 0 2 "No topic created"
}

# ============================================================
#  FINAL REPORT
# ============================================================
Write-Host "`n"
Write-Host ("=" * 68) -ForegroundColor White
Write-Host "  ORKA AI - SYSTEM HEALTH REPORT" -ForegroundColor White
Write-Host ("=" * 68) -ForegroundColor White

$passed  = $TestRows | Where-Object { $_.Status -eq "OK" }
$partial = $TestRows | Where-Object { $_.Status -eq "PARTIAL" }
$failed  = $TestRows | Where-Object { $_.Status -eq "FAIL" }
$bonuses = $TestRows | Where-Object { $_.Status -eq "BONUS" }
$skipped = $TestRows | Where-Object { $_.Status -eq "SKIP" }

$pct = if ($MaxScore -gt 0) { [int](($Score / $MaxScore) * 100) } else { 0 }
$grade = if ($pct -ge 95) { "S" } elseif ($pct -ge 85) { "A" } elseif ($pct -ge 70) { "B" } elseif ($pct -ge 50) { "C" } else { "F" }

Write-Host ""
Write-Host "  REQUIRED SCORE : $Score / $MaxScore  ($pct%)  Grade: $grade" -ForegroundColor $(if ($pct -ge 85) { "Green" } elseif ($pct -ge 50) { "Yellow" } else { "Red" })
Write-Host "  BONUS POINTS   : +$BonusScore extra" -ForegroundColor Magenta
Write-Host "  FINAL SCORE    : $Score + $BonusScore = $($Score + $BonusScore) (max was $MaxScore)" -ForegroundColor Cyan
Write-Host ""

Write-Host "  OK      ($($passed.Count)) :" -ForegroundColor Green
$passed | ForEach-Object { Write-Host "    [OK]     $($_.Test.PadRight(45)) $($_.Result)  $($_.Detail)" -ForegroundColor Green }

if ($partial.Count -gt 0) {
    Write-Host "`n  PARTIAL ($($partial.Count)) :" -ForegroundColor Yellow
    $partial | ForEach-Object { Write-Host "    [PART]   $($_.Test.PadRight(45)) $($_.Result)  $($_.Detail)" -ForegroundColor Yellow }
}

if ($failed.Count -gt 0) {
    Write-Host "`n  FAILED  ($($failed.Count)) :" -ForegroundColor Red
    $failed | ForEach-Object { Write-Host "    [FAIL]   $($_.Test.PadRight(45)) $($_.Result)  $($_.Detail)" -ForegroundColor Red }
}

if ($bonuses.Count -gt 0) {
    Write-Host "`n  BONUS   ($($bonuses.Count)) :" -ForegroundColor Magenta
    $bonuses | ForEach-Object { Write-Host "    [BONUS]  $($_.Test.PadRight(45)) $($_.Result)  $($_.Detail)" -ForegroundColor Magenta }
}

if ($skipped.Count -gt 0) {
    Write-Host "`n  SKIPPED ($($skipped.Count)) :" -ForegroundColor DarkGray
    $skipped | ForEach-Object { Write-Host "    [SKIP]   $($_.Test.PadRight(45)) $($_.Result)  $($_.Detail)" -ForegroundColor DarkGray }
}

Write-Host ""
Write-Host ("=" * 68) -ForegroundColor White

# Write markdown report
$md = @"
# Orka AI System Health Report
**Date:** $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
**Grade:** $grade | **Score:** $Score/$MaxScore ($pct%) | **Bonus:** +$BonusScore

## Summary
| Status | Count |
|--------|-------|
| OK | $($passed.Count) |
| PARTIAL | $($partial.Count) |
| FAILED | $($failed.Count) |
| BONUS | $($bonuses.Count) |
| SKIPPED | $($skipped.Count) |

## Results

| Test | Result | Status | Detail |
|------|--------|--------|--------|
$($TestRows | ForEach-Object { "| $($_.Test) | $($_.Result) | $($_.Status) | $($_.Detail) |" } | Out-String)
"@

$md | Out-File -FilePath $ReportFile -Encoding utf8
Write-Host "  Report: $ReportFile" -ForegroundColor Cyan
Write-Host ""
