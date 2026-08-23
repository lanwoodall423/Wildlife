Set-StrictMode -Version Latest

function Invoke-WildlifeCoordinatorJson {
    param(
        [Parameter(Mandatory = $true)][string]$CommandPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )
    $output = @(& $CommandPath @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $text = ($output -join "`n").Trim()
    $jsonStart = $text.IndexOf('{')
    if ($jsonStart -lt 0) {
        throw "DevBridge2 returned no JSON for '$($Arguments -join ' ')': $text"
    }
    try {
        return $text.Substring($jsonStart) | ConvertFrom-Json
    } catch {
        throw "DevBridge2 returned invalid JSON for '$($Arguments -join ' ')': $text"
    }
}

function Assert-WildlifeCoordinatorSuccess {
    param(
        [Parameter(Mandatory = $true)]$Response,
        [Parameter(Mandatory = $true)][string]$Operation
    )
    if ($null -eq $Response -or $Response.success -ne $true) {
        $code = if ($null -eq $Response) { 'NO_RESPONSE' } else { [string]$Response.errorCode }
        $message = if ($null -eq $Response) { 'no response' } else { [string]$Response.error }
        throw "DevBridge2 $Operation failed [$code]: $message"
    }
}

function ConvertTo-WildlifeCoordinatorJsonArgument {
    param(
        [Parameter(Mandatory = $true)][string]$Json
    )
    if ([string]::IsNullOrWhiteSpace($Json)) { throw 'JSON arguments cannot be empty.' }
    # DevBridge.cmd is a Windows batch boundary. Preserve the JSON quotes when
    # PowerShell forwards the argument through cmd.exe to the coordinator.
    return '"' + $Json.Replace('\', '\\').Replace('"', '\"') + '"'
}

function Get-WildlifeLaunchIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$CoordinatorRoot,
        [Parameter(Mandatory = $true)]$Status
    )
    $launchId = if ($Status.PSObject.Properties.Name -contains 'launchId') {
        [string]$Status.launchId
    } else { '' }
    $generation = 0
    $launchGeneration = if ($Status.PSObject.Properties.Name -contains 'launchGeneration') {
        [string]$Status.launchGeneration
    } else { '' }
    [int]::TryParse($launchGeneration, [Globalization.NumberStyles]::Integer,
        [Globalization.CultureInfo]::InvariantCulture, [ref]$generation) | Out-Null
    if ($generation -le 0) {
        $statusGeneration = if ($Status.PSObject.Properties.Name -contains 'generation') {
            [string]$Status.generation
        } else { '' }
        [int]::TryParse($statusGeneration, [Globalization.NumberStyles]::Integer,
            [Globalization.CultureInfo]::InvariantCulture, [ref]$generation) | Out-Null
    }

    $readinessPath = Join-Path $CoordinatorRoot 'Runtime\readiness.json'
    if (([string]::IsNullOrWhiteSpace($launchId) -or $generation -le 0) -and
        (Test-Path -LiteralPath $readinessPath -PathType Leaf)) {
        $readiness = Get-Content -LiteralPath $readinessPath -Raw | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($launchId)) { $launchId = [string]$readiness.launchId }
        if ($generation -le 0) { $generation = [int]$readiness.generation }
    }
    if ([string]::IsNullOrWhiteSpace($launchId) -or $generation -le 0) {
        throw 'DevBridge2 did not expose a complete ready launch identity.'
    }
    return [pscustomobject]@{ launchId = $launchId; generation = $generation }
}
