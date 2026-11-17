# StudyShellGuard - independent game watchdog.
# Runs as SYSTEM every minute (scheduled task StudyShellGuard).
# Kills blocked game processes when HardBlock or Study mode is active.
# Works even if StudyShell itself is closed.

$ErrorActionPreference = 'SilentlyContinue'

$settingsPath = 'C:\Users\1\AppData\Roaming\StudyShell\settings.json'
$logPath      = 'C:\Users\1\AppData\Roaming\StudyShell\logs\guard.log'

# Process names to kill (exact match, case-insensitive, without .exe)
$blockedNames = @(
    'radmir launcher','radmirlauncher','radmir','radmirgames','radmir games',
    'crmp','samp','gta_sa',
    'robloxplayerbeta','robloxplayerlauncher','robloxplayerinstaller','roblox',
    'robloxstudiobeta','robloxstudio','robloxstudioinstaller','robloxstudiolauncher',
    'robloxcrashhandler','studiomcp',
    'minecraft','minecraftlauncher','tlauncher','tl','flauncher','ll',
    'steam','steamwebhelper','epicgameslauncher','battle.net','battlenet',
    'origin','eadesktop','ubisoftconnect','upc','galaxyclient','gog galaxy',
    'riotclientservices','riotclient','leagueclient','leagueoflegends',
    'valorant','bethesdanet','rockstar','socialclubhelper','itch','playnite',
    'fortniteclient-win64-shipping','fortnitelauncher','fortnite',
    'csgo','cs2','dota2','rustclient','rust','escapefromtarkov','tarkov','osu!',
    'geometrydash','amongus','terraria','tmodloader','gta5','gtav',
    'overwatch','overwatch2','hearthstone','diablo','diablo iv',
    'genshinimpact','honkai','starrail','zenlesszonezero','pubg','tslgame',
    'warframe','warframe.x64','apex','r5apex','fifa','fc24','fc25',
    'nba2k','madden','call of duty','cod','modernwarfare','cyberpunk2077',
    'witcher3','reddead2','rdr2','eldenring','dark souls','darksouls',
    'fallout4','skyrimse','skyrim','factorio','rimworld','subnautica',
    'phasmophobia','garrysmod','satisfactory','valheim','7daystodie',
    'deadbydaylight','dontstarve','spelunky','hollowknight','celeste',
    'cuphead','undertale','brawlstars','clashroyale','clashofclans',
    'candycrush','subwaysurfers'
)

# Java is only killed when it belongs to a game launcher
$javaPathPattern = '(?i)(minecraft|tlauncher|flauncher|legacy)'

function Test-BlockActive {
    if (-not (Test-Path $settingsPath)) { return $false }
    try {
        $s = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        if ($s.HardBlock -eq $true) { return $true }
        if ($s.StudySessionActive -eq $true) {
            try {
                $end = [DateTime]$s.StudySessionEndUtc
                if ($end -gt (Get-Date)) { return $true }
            } catch { }
        }
        if ($s.StudyScheduleEnabled -eq $true) {
            $now = Get-Date
            $start = [DateTime]::Today.Add([TimeSpan]::Parse($s.StudyStart))
            $endT  = [DateTime]::Today.Add([TimeSpan]::Parse($s.StudyEnd))
            if ($start -eq $endT) { return $false }
            if ($start -lt $endT) { if ($now -ge $start -and $now -lt $endT) { return $true } }
            else { if ($now -ge $start -or $now -lt $endT) { return $true } }
        }
    } catch { }
    return $false
}

if (-not (Test-BlockActive)) { exit 0 }

$killed = @()
Get-CimInstance Win32_Process | ForEach-Object {
    $p = $_
    $name = $p.Name
    $exe  = $p.ExecutablePath
    $isJava = ($name -match '^java(w)?(ws)?(path(_target_1)?)?\.exe$')
    $hit = $false
    if ($blockedNames -contains $name.ToLowerInvariant()) { $hit = $true }
    if ($isJava -and $exe -match $javaPathPattern) { $hit = $true }
    if ($hit) {
        try {
            Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop
            $killed += "$($p.ProcessId):${name}:${exe}"
        } catch { }
    }
}

if ($killed.Count -gt 0) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') killed: " + ($killed -join ' | ')
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
}
