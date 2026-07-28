param(
    [Parameter(Mandatory = $true)]
    [string]$Command,
    [string]$Argument = '',
    [int]$TimeoutSeconds = 15
)

$standaloneClient = 'C:\Games\Steam\steamapps\common\RimWorld\Mods\RimWorldDevBridge\DevTools\Send-RimWorldBridge.ps1'
if (Test-Path -LiteralPath $standaloneClient) {
    $renamed = @{
        'RUN_TESTS' = 'RUN_WILDLIFE_TESTS'
        'SETTINGS' = 'WILDLIFE_SETTINGS'
        'SET_SETTING' = 'SET_WILDLIFE_SETTING'
        'DEFS' = 'WILDLIFE_DEFS'
        'OVERLAY' = 'WILDLIFE_OVERLAY'
    }
    $effectiveCommand = $Command.ToUpperInvariant()
    if ($renamed.ContainsKey($effectiveCommand)) { $effectiveCommand = $renamed[$effectiveCommand] }
    & $standaloneClient -Command $effectiveCommand -Argument $Argument -TimeoutMs ($TimeoutSeconds * 1000)
    exit $LASTEXITCODE
}

$data = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios'
$inputPath = Join-Path $data 'Wildlife-Bridge-In.txt'
$outputPath = Join-Path $data 'Wildlife-Bridge-Out.txt'
$statusPath = Join-Path $data 'Wildlife-Bridge-Status.txt'
$wakePath = Join-Path $data 'Wildlife-Bridge-Wake.request'
$clientMutex = [System.Threading.Mutex]::new($false, 'Local\WildlifeBridgeClient')
$mutexAcquired = $false
try {
    $mutexAcquired = $clientMutex.WaitOne([TimeSpan]::FromSeconds($TimeoutSeconds + 5))
}
catch [System.Threading.AbandonedMutexException] {
    $mutexAcquired = $true
}
if (-not $mutexAcquired) {
    $clientMutex.Dispose()
    Write-Output 'status=CLIENT_BUSY'
    exit 4
}

function Exit-Bridge {
    param([int]$Code)
    if ($script:mutexAcquired) {
        $script:clientMutex.ReleaseMutex()
        $script:mutexAcquired = $false
    }
    $script:clientMutex.Dispose()
    exit $Code
}

if (-not (Test-Path -LiteralPath $statusPath) -or
    (Get-Content -LiteralPath $statusPath -Raw) -notmatch '^bridge=(ON|DORMANT)') {
    Write-Output 'status=BRIDGE_UNAVAILABLE'
    Exit-Bridge 2
}

function Read-BridgeStatus {
    $values = @{}
    if (-not (Test-Path -LiteralPath $statusPath)) {
        return $values
    }
    Get-Content -LiteralPath $statusPath | ForEach-Object {
        $parts = $_ -split '=', 2
        if ($parts.Count -eq 2) { $values[$parts[0]] = $parts[1] }
    }
    return $values
}

$bridge = Read-BridgeStatus
if ($bridge.bridge -eq 'DORMANT') {
    Set-Content -LiteralPath ($wakePath + '.writing') -Value 'WAKE' -NoNewline
    Move-Item -LiteralPath ($wakePath + '.writing') -Destination $wakePath -Force
    $wakeDeadline = (Get-Date).AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 25
        $bridge = Read-BridgeStatus
        if ($bridge.bridge -eq 'ON') { break }
    } while ((Get-Date) -lt $wakeDeadline)
}

function Compact-Fallback {
    param([string[]]$Lines)
    function Pick([string]$Pattern) {
        return @($Lines | Where-Object { $_ -match $Pattern } | Select-Object -First 1)[0]
    }
    function Val([string]$Line, [string]$Pattern, [string]$Default = '?') {
        if ($Line -and $Line -match $Pattern) { return $Matches[1] }
        return $Default
    }
    $tick = Pick '^tick='
    $map = Pick '^map='
    $colony = Pick '^colony='
    $wild = Pick '^wildlife='
    $regional = Pick '^regional=species:'
    $stories = Pick '^stories='
    $groups = Pick '^herds=PREY '
    $homes = Pick '^herds=HOMES '
    $packs = Pick '^packs=PREDATORS '
    $signals = Pick '^signals='
    $journal = Pick '^journal=JOURNAL '
    $moment = Pick '^journal=(MOMENT|OPPORTUNITY) '
    $memory = Pick '^memory=MEMORY '
    $mapPerf = Pick '^map=pawns:'
    @(
        ('cx=f4 t=' + (Val $tick 'tick=(\d+)') + ' m=' + (Val $map 'map=(\d+)') +
            ' b=' + (Val $map 'biome=([^ ]+)') + ' s=' + (Val $map 'season=([^ ]+)')),
        ('pop=w' + (Val $wild 'wild:(\d+)') + '/p' + (Val $wild 'predators:(\d+)') +
            '/sp' + (Val $wild 'species:(\d+)') + ' c' + (Val $colony 'colonists:(\d+)') +
            '/ta' + (Val $colony 'tameAnimals:(\d+)') + ' rg' +
            (Val $regional 'species:(\d+)') + '/ro' + (Val $regional 'roaming:(\d+)')),
        ('sim=g' + (Val $groups 'groups=(\d+)') + '/th' +
            (Val $groups 'threatenedGroups=(\d+)') + '/hm' +
            (Val $homes 'claimed=(\d+)') + ' pk' + (Val $packs 'packs=(\d+)') +
            '/hunt' + (Val $packs 'activeHunts=(\d+)') + ' sig' +
            (Val $signals 'active:(\d+)') + '/d' + (Val $signals 'dialects:(\d+)')),
        ('story=mem' + (Val $memory 'records=(\d+)') + '/soc' +
            (Val $memory 'social=(\d+)') + '/not' + (Val $stories 'notable:(\d+)') +
            '/mys' + (Val $stories 'mysteries:(\d+)') + ' jn' +
            (Val $journal 'entries=(\d+)') + ' mom=' +
            (Val $moment '^(?:journal=)?(?:MOMENT|OPPORTUNITY) ([^ ]+)')),
        ('load=pawns' + (Val $mapPerf 'pawns:(\d+)') + '/things' +
            (Val $mapPerf 'things:(\d+)'))
    )
}

$compactFallback = $Command.Equals('CODEX', [StringComparison]::OrdinalIgnoreCase) -and
    $bridge.protocol -ne 'v5'
$wireCommand = if ($compactFallback) { 'BATCH_INSPECT' } else { $Command }
$wireArgument = if ($compactFallback) {
    'SNAPSHOT,SYSTEMS,SIGNALS,RECENT,PERFORMANCE'
} else { $Argument }
$id = [Guid]::NewGuid().ToString('N')
$payload = $id + '|' + $wireCommand + '|' + $wireArgument

if ($bridge.transport -like 'tcp*' -and [int]$bridge.port -gt 0 -and $bridge.token) {
    try {
        $client = [System.Net.Sockets.TcpClient]::new()
        $client.NoDelay = $true
        $client.Connect('127.0.0.1', [int]$bridge.port)
        $stream = $client.GetStream()
        $writer = [System.IO.StreamWriter]::new($stream, [System.Text.UTF8Encoding]::new($false), 4096, $true)
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $false, 4096, $true)
        $writer.NewLine = "`n"
        $writer.WriteLine($bridge.token + '|' + $payload)
        $writer.Flush()
        $response = $reader.ReadToEnd()
        $reader.Dispose()
        $writer.Dispose()
        $client.Dispose()
        $lines = $response -split "`r?`n" | Where-Object { $_ -ne '' }
        if ($lines.Count -ge 2 -and $lines[1] -eq 'status=OK') {
            $result = @($lines | Select-Object -Skip 2)
            if ($compactFallback) { Compact-Fallback $result } else { $result }
            Exit-Bridge 0
        }
        $lines
        Exit-Bridge 1
    }
    catch {
        if ($client) { $client.Dispose() }
        # Fall through to the file protocol for crash recovery and compatibility.
    }
}

$temporary = $inputPath + '.writing'
Set-Content -LiteralPath $temporary -Value $payload -NoNewline
Move-Item -LiteralPath $temporary -Destination $inputPath -Force

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
do {
    Start-Sleep -Milliseconds 50
    if (Test-Path -LiteralPath $outputPath) {
        $lines = Get-Content -LiteralPath $outputPath
        if ($lines.Count -ge 2 -and $lines[0] -eq ('id=' + $id)) {
            if ($lines[1] -eq 'status=OK') {
                $result = @($lines | Select-Object -Skip 2)
                if ($compactFallback) { Compact-Fallback $result } else { $result }
                Exit-Bridge 0
            }
            $lines
            Exit-Bridge 1
        }
    }
} while ((Get-Date) -lt $deadline)

Write-Output 'status=TIMEOUT'
Exit-Bridge 3
