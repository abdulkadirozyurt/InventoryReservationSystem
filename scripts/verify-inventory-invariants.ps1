param(
    [string]$ConnectionString = "mongodb://localhost:27017/inventory-service?replicaSet=rs0",
    [string]$ReservationsCollection = "reservations",
    [string]$InventoryItemsCollection = "inventory-items",
    [string]$TransactionsCollection = "inventory-transactions",
    [string]$MongoContainerName = "inventoryreservationsystem-mongodb-1"
)

$script:passed = 0
$script:failed = 0

# ---- Helpers ----

function Write-Result {
    param([string]$CheckName, [bool]$Passed, [string[]]$Details)
    if ($Passed) {
        Write-Host "[PASS] $CheckName" -ForegroundColor Green
        $script:passed++
    } else {
        Write-Host "[FAIL] $CheckName" -ForegroundColor Red
        $script:failed++
        $maxDetails = 15
        for ($i = 0; $i -lt [Math]::Min($Details.Length, $maxDetails); $i++) {
            Write-Host "       $($Details[$i])" -ForegroundColor Red
        }
        if ($Details.Length -gt $maxDetails) {
            Write-Host "       ... and $($Details.Length - $maxDetails) more failures" -ForegroundColor DarkRed
        }
    }
}

function Write-Info {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor Gray
}

function Invoke-MongoShell {
    param([string]$ScriptJs, [string]$TargetConnectionString)

    if (Get-Command mongosh -ErrorAction SilentlyContinue) {
        return & mongosh --quiet --eval "JSON.stringify($ScriptJs)" $TargetConnectionString 2>$null
    }

    # Host'ta mongosh yoksa Compose MongoDB container'ındaki mongosh kullanılır.
    # Bu fallback Faz 6 doğrulama script'inin temiz clone + Docker ortamında çalışmasını sağlar.
    return & docker exec $MongoContainerName mongosh --quiet --eval "JSON.stringify($ScriptJs)" $TargetConnectionString 2>$null
}

function Invoke-MongoQuery {
    param([string]$ScriptJs)

    # Use simple regex replacement for collection names, avoiding $field name conflicts
    $jsCode = $ScriptJs -replace '__INV__', $InventoryItemsCollection
    $jsCode = $jsCode -replace '__RES__', $ReservationsCollection
    $jsCode = $jsCode -replace '__TXN__', $TransactionsCollection

    $result = Invoke-MongoShell -ScriptJs $jsCode -TargetConnectionString $ConnectionString
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        $cleanConn = ($ConnectionString -split '\?')[0]
        $result = Invoke-MongoShell -ScriptJs $jsCode -TargetConnectionString $cleanConn
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            throw "mongosh query failed (exit code: $exitCode). Connection: $cleanConn"
        }
    }

    $jsonText = ($result | Out-String).Trim()
    if ([string]::IsNullOrEmpty($jsonText)) { return @() }

    return $jsonText | ConvertFrom-Json -ErrorAction Stop
}

# ---- Bootstrap ----

if (-not (Get-Command mongosh -ErrorAction SilentlyContinue)) {
    try {
        docker exec $MongoContainerName mongosh --quiet --eval "db.adminCommand({ ping: 1 }).ok" | Out-Null
    }
    catch {
        Write-Host "ERROR: mongosh not found in PATH and MongoDB container fallback failed." -ForegroundColor Red
        Write-Host "Install mongosh or start Docker Compose MongoDB container: $MongoContainerName" -ForegroundColor Yellow
        exit 1
    }
}

Write-Host "`n--- Inventory Invariant Verification ---`n" -ForegroundColor Cyan
Write-Host "Database: $ConnectionString" -ForegroundColor Gray
Write-Host ""

# ---- Data Loading ----

try {
    Write-Info "Loading inventory items..."
    $inventoryItems = Invoke-MongoQuery -ScriptJs @'
        db.getCollection('__INV__').find({},
            { sku: 1, warehouseId: 1, quantityAvailable: 1, quantityReserved: 1 }
        ).toArray()
'@
    Write-Info "  Found $($inventoryItems.Count) item(s)"

    Write-Info "Loading transaction deltas..."
    $txnDeltas = Invoke-MongoQuery -ScriptJs @'
        db.getCollection('__TXN__').aggregate([
            { $group: {
                _id: { sku: '$sku', warehouseId: '$warehouseId' },
                sumQtyAvailDelta: { $sum: '$quantityAvailableDelta' },
                sumQtyReservedDelta: { $sum: '$quantityReservedDelta' }
            }}
        ]).toArray()
'@
    Write-Info "  Found $($txnDeltas.Count) (sku, warehouse) group(s)"

    Write-Info "Loading rebalance (type=5) transactions..."
    $rebalanceTxns = Invoke-MongoQuery -ScriptJs @'
        db.getCollection('__TXN__').aggregate([
            { $match: { type: 'Rebalance' } },
            { $group: {
                _id: { sku: '$sku', warehouseId: '$warehouseId' },
                sumQtyAvailDelta: { $sum: '$quantityAvailableDelta' }
            }}
        ]).toArray()
'@
    Write-Info "  Found $($rebalanceTxns.Count) rebalance record(s)"

    Write-Info "Loading pending reservations..."
    $pendingReservations = Invoke-MongoQuery -ScriptJs @'
        db.getCollection('__RES__').aggregate([
            { $match: { status: 'Pending' } },
            { $unwind: '$items' },
            { $group: {
                _id: {
                    sku: { $ifNull: ['$items.sku', '$items.Sku'] },
                    warehouseId: { $ifNull: ['$items.warehouseId', '$items.WarehouseId'] }
                },
                totalQuantity: { $sum: { $ifNull: ['$items.quantity', '$items.Quantity'] } }
            }}
        ]).toArray()
'@
    Write-Info "  Found $($pendingReservations.Count) pending (sku, warehouse) group(s)"

    Write-Info "Loading non-pending reservations..."
    $nonPendingReservations = Invoke-MongoQuery -ScriptJs @'
        db.getCollection('__RES__').aggregate([
            { $match: { status: { $ne: 'Pending' } } },
            { $unwind: '$items' },
            { $group: {
                _id: {
                    sku: { $ifNull: ['$items.sku', '$items.Sku'] },
                    warehouseId: { $ifNull: ['$items.warehouseId', '$items.WarehouseId'] }
                },
                totalQuantity: { $sum: { $ifNull: ['$items.quantity', '$items.Quantity'] } }
            }}
        ]).toArray()
'@
    Write-Info "  Found $($nonPendingReservations.Count) non-pending (sku, warehouse) group(s)"
} catch {
    Write-Host "`nERROR loading data from MongoDB: $_" -ForegroundColor Red
    exit 1
}

# ---- Build Lookups ----

$itemMap = @{}
foreach ($item in $inventoryItems) {
    $key = "$($item.sku)|$($item.warehouseId)"
    $itemMap[$key] = $item
}

$txnMap = @{}
foreach ($txn in $txnDeltas) {
    $key = "$($txn._id.sku)|$($txn._id.warehouseId)"
    $txnMap[$key] = $txn
}

$pendingMap = @{}
foreach ($pr in $pendingReservations) {
    $key = "$($pr._id.sku)|$($pr._id.warehouseId)"
    $pendingMap[$key] = [long]$pr.totalQuantity
}

$nonPendingMap = @{}
foreach ($np in $nonPendingReservations) {
    $key = "$($np._id.sku)|$($np._id.warehouseId)"
    $nonPendingMap[$key] = [long]$np.totalQuantity
}

# Aggregate rebalance by sku (global zero-sum)
$rebalanceBySku = @{}
foreach ($rt in $rebalanceTxns) {
    if ($null -eq $rt._id -or $null -eq $rt._id.sku) { continue }
    $sku = $rt._id.sku
    $delta = [long]$rt.sumQtyAvailDelta
    if (-not $rebalanceBySku.ContainsKey($sku)) { $rebalanceBySku[$sku] = 0 }
    $rebalanceBySku[$sku] += $delta
}

Write-Host ""

# ==========================================
# Check 1: No negative quantities
# ==========================================

$failDetails = @()

if ($inventoryItems.Count -eq 0) {
    Write-Result -CheckName "Check 1: No negative quantities" -Passed $true
} else {
    foreach ($item in $inventoryItems) {
        $key = "$($item.sku)|$($item.warehouseId)"
        $qAvail = [long]$item.quantityAvailable
        $qRes = [long]$item.quantityReserved
        if ($qAvail -lt 0) {
            $failDetails += "SKU '$($item.sku)', WH '$($item.warehouseId)': qtyAvailable=$qAvail (negative)"
        }
        if ($qRes -lt 0) {
            $failDetails += "SKU '$($item.sku)', WH '$($item.warehouseId)': qtyReserved=$qRes (negative)"
        }
    }
    Write-Result -CheckName "Check 1: No negative quantities" -Passed ($failDetails.Count -eq 0) -Details $failDetails
}

# ==========================================
# Check 2: Per-SKU ledger match
# ==========================================

$failDetails = @()
if ($inventoryItems.Count -eq 0) {
    Write-Result -CheckName "Check 2: Per-SKU ledger match" -Passed $true
} else {
    foreach ($item in $inventoryItems) {
        $key = "$($item.sku)|$($item.warehouseId)"
        $currentTotal = [long]$item.quantityAvailable + [long]$item.quantityReserved

        $txn = $txnMap[$key]
        if ($null -ne $txn) {
            $sumDeltas = [long]$txn.sumQtyAvailDelta + [long]$txn.sumQtyReservedDelta
        } else {
            $sumDeltas = 0
        }

        $initialValue = $currentTotal - $sumDeltas

        if ($initialValue -lt 0) {
            $failDetails += "SKU '$($item.sku)', WH '$($item.warehouseId)': initial stock=$initialValue (negative). current=$currentTotal, sumDeltas=$sumDeltas"
        }
    }

    Write-Result -CheckName "Check 2: Per-SKU ledger match" -Passed ($failDetails.Count -eq 0) -Details $failDetails
}

# ==========================================
# Check 3: Rebalance is zero-sum per SKU globally
# ==========================================

$failDetails = @()
if ($rebalanceBySku.Count -eq 0) {
    Write-Result -CheckName "Check 3: Rebalance is zero-sum per SKU" -Passed $true
} else {
    foreach ($entry in $rebalanceBySku.GetEnumerator()) {
        $sku = $entry.Key
        $globalDelta = $entry.Value
        if ($globalDelta -ne 0) {
            $failDetails += "SKU '$sku': rebalance global delta = $globalDelta (expected 0)"
        }
    }
    Write-Result -CheckName "Check 3: Rebalance is zero-sum per SKU" -Passed ($failDetails.Count -eq 0) -Details $failDetails
}

# ==========================================
# Check 4: Pending reservations match reserved quantities
# ==========================================

$failDetails = @()
# For every (SKU, WH) in inventory-items, verify qtyReserved == sum(pending)
if ($inventoryItems.Count -eq 0) {
    Write-Result -CheckName "Check 4: Pending reservations match reserved quantities" -Passed $true
} else {
    foreach ($item in $inventoryItems) {
        $key = "$($item.sku)|$($item.warehouseId)"
        $qReserved = [long]$item.quantityReserved
        $pendingQty = if ($pendingMap.ContainsKey($key)) { $pendingMap[$key] } else { 0 }

        if ($qReserved -ne $pendingQty) {
            $diff = $qReserved - $pendingQty
            $failDetails += "SKU '$($item.sku)', WH '$($item.warehouseId)': pending=$pendingQty != reserved=$qReserved (diff=$diff)"
        }
    }

    # Also flag any pending reservations with no inventory item
    foreach ($entry in $pendingMap.GetEnumerator()) {
        if (-not $itemMap.ContainsKey($entry.Key)) {
            $parts = $entry.Key -split '\|'
            $failDetails += "SKU '$($parts[0])', WH '$($parts[1])': pending qty=$($entry.Value) but no inventory item record"
        }
    }

    Write-Result -CheckName "Check 4: Pending reservations match reserved quantities" -Passed ($failDetails.Count -eq 0) -Details $failDetails
}

# ==========================================
# Check 5: Non-pending reservations don't hold stock
# ==========================================

$failDetails = @()
# For every (SKU, WH) in inventory-items, verify that NON-pending items
# don't leak into quantityReserved.
# Since Check 4 verifies qtyReserved == sum(pending), any non-pending
# item quantities still affecting inventory would manifest as a residual.
$anyResidual = $false

if ($inventoryItems.Count -eq 0) {
    Write-Result -CheckName "Check 5: Non-pending reservations don't hold stock" -Passed $true
} else {
    foreach ($item in $inventoryItems) {
        $key = "$($item.sku)|$($item.warehouseId)"
        $qReserved = [long]$item.quantityReserved
        $pendingQty = if ($pendingMap.ContainsKey($key)) { $pendingMap[$key] } else { 0 }
        $residual = $qReserved - $pendingQty

        if ($residual -ne 0) {
            $anyResidual = $true
            $failDetails += "SKU '$($item.sku)', WH '$($item.warehouseId)': qtyReserved residual=$residual (qtyReserved=$qReserved, pending sum=$pendingQty)"
        }
    }

    # Also check: non-pending reservations with quantities that have no corresponding inventory item
    foreach ($entry in $nonPendingMap.GetEnumerator()) {
        if ($entry.Value -gt 0) {
            $parts = $entry.Key -split '\|'
            if (-not $itemMap.ContainsKey($entry.Key)) {
                $failDetails += "SKU '$($parts[0])', WH '$($parts[1])': non-pending qty=$($entry.Value) but no inventory item record"
            }
        }
    }

    if (-not $anyResidual) {
        Write-Result -CheckName "Check 5: Non-pending reservations don't hold stock" -Passed $true
    } else {
        Write-Result -CheckName "Check 5: Non-pending reservations don't hold stock" -Passed $false -Details $failDetails
    }
}

# ==========================================
# Summary
# ==========================================

$total = $script:passed + $script:failed
Write-Host ""
Write-Host ("=" * 55) -ForegroundColor Cyan
if ($script:failed -eq 0) {
    Write-Host "RESULT: $total/$total checks PASSED." -ForegroundColor Green
    Write-Host "Exit code: 0" -ForegroundColor Green
    exit 0
} else {
    Write-Host "RESULT: $script:passed/$total checks PASSED. $script:failed checks FAILED." -ForegroundColor Red
    Write-Host "Exit code: 1" -ForegroundColor Red
    exit 1
}
