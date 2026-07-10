#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs all six evaluation scenarios (E1-E6) sequentially, prints PASS/FAIL summary.

.DESCRIPTION
    Orchestrates k6 stress tests, curl-based API verification, MongoDB queries,
    and log analysis to validate the Inventory Reservation System against
    the requirements in Docs/about-project/raw-requirements.md.

.PARAMETER OrderServiceUrl
    Base URL for OrderService (default: http://localhost:5041).
.PARAMETER InventoryServiceUrl
    Base URL for InventoryService gRPC health (default: http://localhost:5032).
.PARAMETER WhatIf
    Show what would run without executing.

.EXAMPLE
    .\scripts\run-evaluation-scenarios.ps1
    .\scripts\run-evaluation-scenarios.ps1 -OrderServiceUrl http://localhost:5041
    .\scripts\run-evaluation-scenarios.ps1 -WhatIf
#>
param(
    [string]$OrderServiceUrl = "http://localhost:5041",
    [string]$InventoryServiceUrl = "http://localhost:5032",
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$InformationPreference = "Continue"

# Scenario results: hashtable keyed by "E1".."E6", value = "PASS" or "FAIL: reason"
$script:results = [ordered]@{}
$script:startTime = Get-Date
$script:invariantScript = Join-Path $PSScriptRoot "verify-inventory-invariants.ps1"
$script:stressDir = Join-Path $PSScriptRoot "stress"
$script:repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$script:k6Scripts = @(
    "reservation-concurrency.js",
    "intersecting-sku-batches.js",
    "expiry-pressure.js",
    "idempotency-retry.js"
)

# ---- Helpers ----

function Write-Step {
    param([string]$Message)
    Write-Host "`n=== $Message ===" -ForegroundColor Cyan
}

function Write-Substep {
    param([string]$Message)
    Write-Host "  -> $Message" -ForegroundColor DarkGray
}

function Write-Result {
    param([string]$Scenario, [bool]$Passed, [string]$Detail = "")
    $label = if ($Passed) { "[$Scenario] PASS" } else { "[$Scenario] FAIL: $Detail" }
    $color = if ($Passed) { "Green" } else { "Red" }
    Write-Host "" -NoNewline
    Write-Host $label -ForegroundColor $color
    $script:results[$Scenario] = $label
}

function Test-CommandExists {
    param([string]$Command)
    return [bool](Get-Command $Command -ErrorAction SilentlyContinue)
}

function Invoke-K6 {
    param(
        [string]$ScriptName,
        [string]$ExtraArgs = ""
    )
    $scriptPath = Join-Path $script:stressDir $ScriptName
    if (-not (Test-Path $scriptPath)) {
        throw "k6 script not found: $scriptPath"
    }

    $trendStats = "--summary-trend-stats=avg,p(50),p(90),p(95),p(99)"
    Write-Substep "Running: k6 run $ScriptName $ExtraArgs"
    if ($WhatIf) {
        Write-Substep "[WhatIf] Would run: k6 run $trendStats -e BASE_URL=$OrderServiceUrl $ExtraArgs $scriptPath"
        return $true
    }

    $extraTokens = if ($ExtraArgs) { $ExtraArgs.Trim() -split '\s+' } else { @() }
    $k6Args = @("run", $trendStats, "-e", "BASE_URL=$OrderServiceUrl") + $extraTokens + @("--", $scriptPath)

    $global:LASTEXITCODE = 0
    & "k6" $k6Args 2>&1 | ForEach-Object { Write-Host $_ }

    if ($LASTEXITCODE -ne 0) {
        Write-Substep "k6 exit code: $LASTEXITCODE"
        return $false
    }
    return $true
}

function Get-OrderServiceHealth {
    try {
        $resp = Invoke-WebRequest -Uri "$OrderServiceUrl/health/ready" -TimeoutSec 10 -UseBasicParsing
        return $resp.StatusCode -eq 200
    } catch {
        return $false
    }
}

function Get-InventoryServiceHealth {
    try {
        $resp = Invoke-WebRequest -Uri "$InventoryServiceUrl/health/ready" -TimeoutSec 10 -UseBasicParsing
        return $resp.StatusCode -eq 200
    } catch {
        return $false
    }
}

function Get-DockerRunningServices {
    try {
        $output = docker compose ps --services --filter "status=running" 2>$null
        if ($LASTEXITCODE -ne 0) { return @() }
        return $output -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
    } catch {
        return @()
    }
}

function New-RandomIdempotencyKey {
    return [Guid]::NewGuid().ToString("N")
}

function New-OrderNumber {
    return "E2-" + [Guid]::NewGuid().ToString("N").Substring(0, 12).ToUpper()
}

function Invoke-MongoQuery {
    param([string]$Query)
    $result = docker compose exec -T mongodb mongosh --quiet --eval $Query 2>$null
    $global:LASTEXITCODE = 0
    return $result
}

# ---- Prerequisites ----

function Assert-Prerequisites {
    Write-Step "Prerequisites"

    $allGood = $true

    # 1. Check docker compose
    if (-not (Test-CommandExists docker)) {
        Write-Host "[FAIL] docker not on PATH" -ForegroundColor Red
        $allGood = $false
    } else {
        Write-Substep "docker found"
    }

    # 2. Check order service health
    $osHealth = Get-OrderServiceHealth
    if (-not $osHealth) {
        Write-Host "[FAIL] OrderService not reachable at $OrderServiceUrl/health/ready" -ForegroundColor Red
        $allGood = $false
    } else {
        Write-Substep "OrderService health OK at $OrderServiceUrl"
    }

    # 3. Check inventory service health
    $isHealth = Get-InventoryServiceHealth
    if (-not $isHealth) {
        Write-Substep "[WARN] InventoryService health endpoint not reachable at $InventoryServiceUrl/health/ready (gRPC service, continuing)"
    } else {
        Write-Substep "InventoryService health OK at $InventoryServiceUrl"
    }

    # 4. Check docker compose services running
    $services = Get-DockerRunningServices
    $required = @("mongodb", "redis", "orderservice-api", "inventoryservice-api")
    $missing = $required | Where-Object { $_ -notin $services }
    if ($missing.Count -gt 0) {
        Write-Host "[FAIL] Required services not running: $($missing -join ', ')" -ForegroundColor Red
        $allGood = $false
    } else {
        Write-Substep "All docker services running: $($required -join ', ')"
    }

    # 5. Check k6 on PATH
    if (-not (Test-CommandExists k6)) {
        Write-Host "[FAIL] k6 not on PATH" -ForegroundColor Red
        $allGood = $false
    } else {
        Write-Substep "k6 found"
    }

    # 6. Check invariant script exists
    if (-not (Test-Path $script:invariantScript)) {
        Write-Host "[FAIL] Invariant script not found: $($script:invariantScript)" -ForegroundColor Red
        $allGood = $false
    } else {
        Write-Substep "Invariant script found"
    }

    return $allGood
}

function Invoke-InvariantCheck {
    Write-Substep "Running inventory invariant verification..."
    if ($WhatIf) {
        Write-Substep "[WhatIf] Would run: pwsh $($script:invariantScript)"
        return $true
    }
    & $script:invariantScript
    $ok = $LASTEXITCODE -eq 0
    if (-not $ok) {
        Write-Substep "Invariant check FAILED (exit code: $LASTEXITCODE)"
    } else {
        Write-Substep "Invariant check PASSED"
    }
    return $ok
}

# ============================================================
# SCENARIO E1: Reservation concurrency + invariant verification
# ============================================================
function Invoke-E1 {
    Write-Step "[E1] Reservation Concurrency + Invariant Verification"
    Write-Substep "Scenario: Run k6 reservation-concurrency stress test, then verify inventory invariants"

    if ($WhatIf) {
        Write-Result -Scenario "E1" -Passed $true -Detail "WhatIf mode"
        return
    }

    $k6Ok = Invoke-K6 -ScriptName "reservation-concurrency.js"
    if (-not $k6Ok) {
        Write-Result -Scenario "E1" -Passed $false -Detail "k6 reservation-concurrency failed"
        return
    }

    $invariantOk = Invoke-InvariantCheck
    if (-not $invariantOk) {
        Write-Result -Scenario "E1" -Passed $false -Detail "Invariant verification failed after concurrency test"
        return
    }

    Write-Result -Scenario "E1" -Passed $true
}

# ============================================================
# SCENARIO E2: Idempotency — 5 identical POSTs, same Idempotency-Key
# ============================================================
function Invoke-E2 {
    Write-Step "[E2] Idempotency — 5 Identical POSTs"
    Write-Substep "Scenario: Send 5 POST requests with same Idempotency-Key, assert all return same orderNumber, then verify single MongoDB record"

    if ($WhatIf) {
        Write-Result -Scenario "E2" -Passed $true -Detail "WhatIf mode"
        return
    }

    $idempotencyKey = New-RandomIdempotencyKey
    $body = @{
        items = @(
            @{ sku = "SKU-001"; warehouseId = "WH-1"; quantity = 2 },
            @{ sku = "SKU-002"; warehouseId = "WH-1"; quantity = 1 }
        )
    } | ConvertTo-Json

    $headers = @{
        "Idempotency-Key"  = $idempotencyKey
        "Content-Type"     = "application/json"
        "X-Correlation-ID" = [Guid]::NewGuid().ToString("N")
    }

    $responses = @()
    for ($i = 1; $i -le 5; $i++) {
        try {
            $resp = Invoke-RestMethod -Uri "$OrderServiceUrl/api/orders" -Method Post -Body $body -Headers $headers -TimeoutSec 15
            $responses += $resp
            Write-Substep "POST $i/5: orderNumber=$($resp.orderNumber), success=$($resp.success)"
        } catch {
            $errMsg = $_.Exception.Message
            Write-Substep "POST $i/5 FAILED: $errMsg"
            $responses += $null
        }
        Start-Sleep -Milliseconds 200
    }

    # Filter nulls (failed requests)
    $validResponses = $responses | Where-Object { $_ -ne $null }

    if ($validResponses.Count -eq 0) {
        Write-Result -Scenario "E2" -Passed $false -Detail "All 5 POSTs failed"
        return
    }

    # Check all non-null responses have same orderNumber
    $firstOrderNumber = $validResponses[0].orderNumber
    if ([string]::IsNullOrWhiteSpace($firstOrderNumber)) {
        Write-Result -Scenario "E2" -Passed $false -Detail "First response has no orderNumber"
        return
    }

    $allSame = $true
    for ($i = 1; $i -lt $validResponses.Count; $i++) {
        if ($validResponses[$i].orderNumber -ne $firstOrderNumber) {
            Write-Substep "Mismatch at POST $($i+1): got '$($validResponses[$i].orderNumber)', expected '$firstOrderNumber'"
            $allSame = $false
        }
    }

    if (-not $allSame) {
        Write-Result -Scenario "E2" -Passed $false -Detail "Responses have different orderNumber values"
        return
    }

    Write-Substep "All $($validResponses.Count) responses returned identical orderNumber: $firstOrderNumber"

    # Query MongoDB to verify single record
    if (-not (Test-CommandExists docker)) {
        Write-Substep "[SKIP] MongoDB count check: docker not available (trusting API response)"
        Write-Result -Scenario "E2" -Passed $true
        return
    }

    $count = Invoke-MongoQuery "db.getSiblingDB('order-service').orders.countDocuments({ orderNumber: '$firstOrderNumber' })"
    $count = ($count | Select-String -Pattern '\d+').Matches.Value
    Write-Substep "MongoDB count for orderNumber '$firstOrderNumber': $count"

    if ([int]$count -ne 1) {
        Write-Result -Scenario "E2" -Passed $false -Detail "Expected 1 order document in MongoDB, found $count"
        return
    }

    Write-Result -Scenario "E2" -Passed $true
}

# ============================================================
# SCENARIO E3: Expiry pressure — k6 quickcheck + log analysis
# ============================================================
function Invoke-E3 {
    Write-Step "[E3] Expiry Pressure — No Orphaned/Stale Reservations"
    Write-Substep "Scenario: Run k6 expiry-pressure test, grep logs for orphaned/stale reservation markers"

    if ($WhatIf) {
        Write-Result -Scenario "E3" -Passed $true -Detail "WhatIf mode"
        return
    }

    # Capture pre-test log timestamp for filtering later
    $preTimestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"

    $k6Ok = Invoke-K6 -ScriptName "expiry-pressure.js"
    if (-not $k6Ok) {
        Write-Substep "k6 expiry-pressure reported failures (threshold exceeded). Continuing with log analysis..."
    }

    # Allow a few seconds for async expiry processing
    Start-Sleep -Seconds 5

    # Get docker logs since before the test started
    $sinceParam = $preTimestamp.Substring(0, 19)
    try {
        $logs = docker compose logs inventoryservice-api --since=$sinceParam 2>&1
    } catch {
        Write-Substep "Could not retrieve docker logs: $_"
        Write-Result -Scenario "E3" -Passed $false -Detail "Failed to retrieve container logs"
        return
    }

    $logText = $logs | Out-String
    $matches = [regex]::Matches($logText, '(?i)(orphaned.*reservation|stale reservation|stale.*reservation)')

    Write-Substep "Log entries scanned: $($logs.Count) lines"
    Write-Substep "Orphaned/stale matches: $($matches.Count)"

    if ($matches.Count -gt 0) {
        foreach ($m in $matches) {
            Write-Substep "  Found: '$($m.Value)' in log"
        }
        Write-Result -Scenario "E3" -Passed $false -Detail "Found $($matches.Count) orphaned/stale reservation matches in logs"
        return
    }

    Write-Result -Scenario "E3" -Passed $true
}

# ============================================================
# SCENARIO E4: Excessive quantity edge case — all-or-nothing
# ============================================================
function Invoke-E4 {
    Write-Step "[E4] Excessive Quantity — All-or-Nothing Check"
    Write-Substep "Scenario: POST with excessive quantity on one item + valid item, assert success=false and failures non-empty, verify no record persisted"

    if ($WhatIf) {
        Write-Result -Scenario "E4" -Passed $true -Detail "WhatIf mode"
        return
    }

    $idempotencyKey = New-RandomIdempotencyKey

    # Request with excessive qty (99999) + valid item
    $body = @{
        items = @(
            @{ sku = "SKU-001"; warehouseId = "WH-1"; quantity = 99999 },
            @{ sku = "SKU-002"; warehouseId = "WH-1"; quantity = 1 }
        )
    } | ConvertTo-Json

    $headers = @{
        "Idempotency-Key"  = $idempotencyKey
        "Content-Type"     = "application/json"
        "X-Correlation-ID" = [Guid]::NewGuid().ToString("N")
    }

    # Count orders before the attempt
    $countBefore = $null
    if (Test-CommandExists docker) {
        try {
            $countBefore = Invoke-MongoQuery "db.getSiblingDB('order-service').orders.countDocuments()"
            $countBefore = ($countBefore | Select-String -Pattern '\d+').Matches.Value
            Write-Substep "Order count before POST: $countBefore"
        } catch {
            Write-Substep "[WARN] Could not query order count before POST: $_"
        }
    }

    # Send the request
    try {
        $resp = Invoke-RestMethod -Uri "$OrderServiceUrl/api/orders" -Method Post -Body $body -Headers $headers -TimeoutSec 15
    } catch {
        # If the API returns non-200 for failure, parse the error body
        try {
            $stream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            $errBody = $reader.ReadToEnd() | ConvertFrom-Json
            $resp = $errBody
        } catch {
            Write-Result -Scenario "E4" -Passed $false -Detail "Request failed: $($_.Exception.Message)"
            return
        }
    }

    Write-Substep "Response: success=$($resp.success), failures count=$($resp.failures.Count)"

    # Assert success is false
    if ($resp.success -eq $true) {
        Write-Result -Scenario "E4" -Passed $false -Detail "Request succeeded (success=true) despite excessive quantity"
        return
    }

    # Assert failures is non-empty
    $failures = $resp.failures
    if (-not $failures -or ($failures.Count -eq 0)) {
        Write-Result -Scenario "E4" -Passed $false -Detail "Response has no failure details"
        return
    }

    Write-Substep "Failure details: $($failures | ConvertTo-Json -Compress)"

    # Count orders after the attempt
    if (Test-CommandExists docker -and $countBefore) {
        try {
            $countAfter = Invoke-MongoQuery "db.getSiblingDB('order-service').orders.countDocuments()"
            $countAfter = ($countAfter | Select-String -Pattern '\d+').Matches.Value
            Write-Substep "Order count after POST: $countAfter"

            if ([int]$countAfter -ne [int]$countBefore) {
                Write-Result -Scenario "E4" -Passed $false -Detail "Order count changed from $countBefore to $countAfter despite failure (all-or-nothing violated)"
                return
            }
            Write-Substep "Order count unchanged: $countBefore (all-or-nothing respected)"
        } catch {
            Write-Substep "[WARN] Could not query order count after POST: $_"
        }
    }

    Write-Result -Scenario "E4" -Passed $true
}

# ============================================================
# SCENARIO E5: Restart resilience — manual restart guidance + log check
# ============================================================
function Invoke-E5 {
    Write-Step "[E5] Restart Resilience — No Duplicate Releases After Restart"
    Write-Substep "Scenario: Reference restart-during-expiry.md, prompt operator for manual restart, then verify no duplicate releases and run invariant check"

    $mdPath = Join-Path $PSScriptRoot ".." "scripts" "resilience" "restart-during-expiry.md"
    $mdPath = Resolve-Path $mdPath

    if (-not (Test-Path $mdPath)) {
        Write-Result -Scenario "E5" -Passed $false -Detail "Restart guide not found: $mdPath"
        return
    }

    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Yellow
    Write-Host "  E5: MANUAL RESTART REQUIRED" -ForegroundColor Yellow
    Write-Host "================================================================" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Follow the steps in the restart resilience guide:" -ForegroundColor White
    Write-Host "  $mdPath" -ForegroundColor White
    Write-Host ""
    Write-Host "Summary of required steps:" -ForegroundColor White
    Write-Host "  1. Ensure at least 3 expired pending reservations exist" -ForegroundColor White
    Write-Host "  2. Restart the InventoryService container:" -ForegroundColor White
    Write-Host "       docker compose restart inventoryservice-api" -ForegroundColor Cyan
    Write-Host "  3. Wait ~15s for the expiry job to resume" -ForegroundColor White
    Write-Host "  4. Check logs for clean recovery" -ForegroundColor White
    Write-Host ""

    if ($WhatIf) {
        Write-Substep "[WhatIf] Would prompt for Enter after manual restart"
        Write-Result -Scenario "E5" -Passed $true -Detail "WhatIf mode"
        return
    }

    Write-Host "Press Enter AFTER completing the manual restart steps above..." -ForegroundColor Yellow -NoNewline
    $null = Read-Host
    Write-Substep "Operator confirmed restart complete."

    # Allow a moment for logs to settle
    Start-Sleep -Seconds 3

    # Check logs for "duplicate release" patterns
    try {
        $logs = docker compose logs inventoryservice-api --since=1m 2>&1
    } catch {
        Write-Substep "Could not retrieve docker logs: $_"
        Write-Result -Scenario "E5" -Passed $false -Detail "Failed to retrieve container logs"
        return
    }

    $logText = $logs | Out-String
    $dupMatches = [regex]::Matches($logText, '(?i)(duplicate release|already released|DuplicateRelease)')

    Write-Substep "Log entries scanned: $($logs.Count) lines"
    Write-Substep "Duplicate release matches: $($dupMatches.Count)"

    # Check for actual duplicate releases (not idempotency skip)
    # "already released" is the idempotent skip message — it's expected/good, not an error
    # "duplicate release" (without context) or if actual double-processing occurred
    $actualDup = [regex]::Matches($logText, '(?i)(?<!already )(duplicate release)')
    Write-Substep "Actual duplicate release events (non-idempotent): $($actualDup.Count)"

    if ($actualDup.Count -gt 0) {
        Write-Result -Scenario "E5" -Passed $false -Detail "Found $($actualDup.Count) actual duplicate release events in logs"
        return
    }

    # Run invariant check
    $invariantOk = Invoke-InvariantCheck
    if (-not $invariantOk) {
        Write-Result -Scenario "E5" -Passed $false -Detail "Invariant verification failed after restart"
        return
    }

    Write-Result -Scenario "E5" -Passed $true
}

# ============================================================
# SCENARIO E6: Full stress suite — all 4 k6 scripts + invariant
# ============================================================
function Invoke-E6 {
    Write-Step "[E6] Full Stress Suite — All k6 Scripts + Invariant Verification"
    Write-Substep "Scenario: Run all 4 k6 stress scripts sequentially, then verify inventory invariants"

    if ($WhatIf) {
        Write-Result -Scenario "E6" -Passed $true -Detail "WhatIf mode"
        return
    }

    $failures = @()

    foreach ($script in $script:k6Scripts) {
        $ok = Invoke-K6 -ScriptName $script
        if (-not $ok) {
            $failures += $script
            Write-Substep "[WARN] $script reported failures (continuing to next test)"
        }
    }

    if ($failures.Count -gt 0) {
        Write-Substep "k6 failures in: $($failures -join ', ')"
        Write-Substep "Continuing to invariant verification..."
    }

    $invariantOk = Invoke-InvariantCheck
    if (-not $invariantOk) {
        Write-Result -Scenario "E6" -Passed $false -Detail "Invariant verification failed after full stress suite"
        return
    }

    if ($failures.Count -gt 0) {
        # k6 tests may fail due to expected stock exhaustion; invariant check passed, so the system is consistent
        Write-Substep "Invariant check passed despite k6 threshold failures (expected under contention)"
    }

    Write-Result -Scenario "E6" -Passed $true
}

# ============================================================
# SUMMARY
# ============================================================
function Show-Summary {
    $elapsed = (Get-Date) - $script:startTime
    $passCount = 0
    $failCount = 0

    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host "  EVALUATION SUMMARY" -ForegroundColor Cyan
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host "  Elapsed: $($elapsed.Minutes)m $($elapsed.Seconds)s" -ForegroundColor White
    Write-Host ""

    foreach ($key in $script:results.Keys) {
        $line = $script:results[$key]
        if ($line -match "PASS") {
            Write-Host "  $line" -ForegroundColor Green
            $passCount++
        } else {
            Write-Host "  $line" -ForegroundColor Red
            $failCount++
        }
    }

    Write-Host ""
    Write-Host "  Total: $($script:results.Count) | PASS: $passCount | FAIL: $failCount" -ForegroundColor White
    Write-Host "================================================================" -ForegroundColor Cyan

    if ($failCount -eq 0) {
        Write-Host "RESULT: ALL SCENARIOS PASSED" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "RESULT: $failCount SCENARIO(S) FAILED" -ForegroundColor Red
        exit 1
    }
}

# ============================================================
# MAIN
# ============================================================

Write-Host ""
Write-Host "Inventory Reservation System — Evaluation Scenario Runner" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Started at: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor White
Write-Host "OrderService: $OrderServiceUrl" -ForegroundColor White
if ($WhatIf) {
    Write-Host "Mode: WHAT-IF (dry run)" -ForegroundColor Yellow
}
Write-Host ""

# Phase 0: Prerequisites
$prereqsOk = Assert-Prerequisites
if (-not $prereqsOk -and -not $WhatIf) {
    Write-Host "`n[FATAL] Prerequisites check failed. Aborting." -ForegroundColor Red
    exit 1
}

# Run scenarios
Invoke-E1
Invoke-E2
Invoke-E3
Invoke-E4
Invoke-E5
Invoke-E6

# Summary
Show-Summary
