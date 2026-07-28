param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,
    [switch]$RestartTransport
)

$resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path
$null = [xml](Get-Content -LiteralPath $resolvedSource -Raw)
$data = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios'
$destination = Join-Path $data 'Wildlife-Bridge-HotCommands.xml'
$temporary = $destination + '.writing'

Copy-Item -LiteralPath $resolvedSource -Destination $temporary -Force
Move-Item -LiteralPath $temporary -Destination $destination -Force

$sender = Join-Path $PSScriptRoot 'Send-WildlifeBridge.ps1'
$command = if ($RestartTransport) { 'RESTART_BRIDGE' } else { 'RELOAD_BRIDGE' }
& $sender -Command $command

