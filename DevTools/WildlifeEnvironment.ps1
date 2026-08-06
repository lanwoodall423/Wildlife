Set-StrictMode -Version Latest

function Get-WildlifeRequiredPath {
    param(
        [Parameter(Mandatory = $true)][string]$VariableName,
        [Parameter(Mandatory = $true)][string]$Description
    )
    $value = [Environment]::GetEnvironmentVariable($VariableName)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Set $VariableName to the $Description."
    }
    return [IO.Path]::GetFullPath($value)
}

function Get-WildlifeGamePath {
    $path = Get-WildlifeRequiredPath 'RIMWORLD_EXE' 'RimWorldWin64.exe path'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "RimWorld executable is missing: $path" }
    return $path
}

function Get-WildlifeDataPath {
    $path = Get-WildlifeRequiredPath 'RIMWORLD_DATA_PATH' 'RimWorld user data directory'
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw "RimWorld data directory is missing: $path" }
    return $path
}

function Get-WildlifeBridgeClientPath {
    $path = [Environment]::GetEnvironmentVariable('RIMWORLD_DEVBRIDGE_CLIENT')
    if ([string]::IsNullOrWhiteSpace($path)) {
        $root = [Environment]::GetEnvironmentVariable('RIMWORLD_DEVBRIDGE_ROOT')
        if (-not [string]::IsNullOrWhiteSpace($root)) {
            $path = Join-Path $root 'DevTools\devbridge.ps1'
        }
    }
    if ([string]::IsNullOrWhiteSpace($path)) {
        throw 'Set RIMWORLD_DEVBRIDGE_CLIENT to devbridge.ps1 or RIMWORLD_DEVBRIDGE_ROOT to the RimWorld DevBridge checkout.'
    }
    $path = [IO.Path]::GetFullPath($path)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "DevBridge client is missing: $path" }
    if ([IO.Path]::GetFileName($path) -ne 'devbridge.ps1' -and
        [Environment]::GetEnvironmentVariable('WILDLIFE_ENABLE_LEGACY_BRIDGE') -ne '1') {
        throw "Use the current DevBridge client devbridge.ps1, or explicitly set WILDLIFE_ENABLE_LEGACY_BRIDGE=1 for compatibility testing: $path"
    }
    return $path
}
