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

function Get-WildlifeSaveDataPath {
    param([string]$Path = '')
    $value = if ([string]::IsNullOrWhiteSpace($Path)) {
        [Environment]::GetEnvironmentVariable('RIMWORLD_DATA_PATH')
    } else { $Path }
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw 'Set RIMWORLD_DATA_PATH to RimWorld''s user data directory.'
    }
    $value = [IO.Path]::GetFullPath($value)
    if (-not (Test-Path -LiteralPath $value -PathType Container)) {
        throw "RimWorld user data directory is missing: $value"
    }
    return $value
}

function Get-WildlifeCoordinatorRoot {
    param([string]$Root = '')
    $value = if ([string]::IsNullOrWhiteSpace($Root)) {
        $configured = [Environment]::GetEnvironmentVariable('WILDLIFE_DEVBRIDGE2_ROOT')
        if ([string]::IsNullOrWhiteSpace($configured)) {
            [Environment]::GetEnvironmentVariable('DEVBRIDGE2_ROOT')
        } else { $configured }
    } else { $Root }
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw 'Set WILDLIFE_DEVBRIDGE2_ROOT or DEVBRIDGE2_ROOT to the DevBridge2 checkout.'
    }
    $value = [IO.Path]::GetFullPath($value)
    $command = Join-Path $value 'DevBridge.cmd'
    if (-not (Test-Path -LiteralPath $command -PathType Leaf)) {
        throw "DevBridge2 command is missing: $command"
    }
    return $value
}

function Get-WildlifeCoordinatorCommand {
    param([string]$Root = '')
    return Join-Path (Get-WildlifeCoordinatorRoot $Root) 'DevBridge.cmd'
}
