[CmdletBinding()]
param(
    [string]$GamePath,
    [switch]$Uninstall,
    [string]$LoaderArchive
)

$ErrorActionPreference = 'Stop'
$loaderUrl = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip'
$loaderHash = '82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4'
$gameExe = 'Chill With You.exe'

function Test-GameFolder([string]$Path) {
    return $Path -and
        (Test-Path -LiteralPath (Join-Path $Path $gameExe) -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $Path 'Chill With You_Data\Managed\Assembly-CSharp.dll') -PathType Leaf)
}

function Find-GameFolder {
    $steamRoots = @(
        (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction SilentlyContinue).SteamPath
        (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -Name InstallPath -ErrorAction SilentlyContinue).InstallPath
        (Get-ItemProperty 'HKLM:\SOFTWARE\Valve\Steam' -Name InstallPath -ErrorAction SilentlyContinue).InstallPath
    )
    $libraries = foreach ($steam in ($steamRoots | Where-Object { $_ } | Select-Object -Unique)) {
        $steam
        $list = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $list -PathType Leaf) {
            foreach ($entry in [regex]::Matches([IO.File]::ReadAllText($list), '(?m)^\s*"path"\s*"([^"]+)"')) {
                $entry.Groups[1].Value.Replace('\\', '\')
            }
        }
    }
    foreach ($library in ($libraries | Select-Object -Unique)) {
        $steamApps = Join-Path $library 'steamapps'
        $manifest = Join-Path $steamApps 'appmanifest_3548580.acf'
        if (!(Test-Path -LiteralPath $manifest -PathType Leaf)) { continue }
        $entry = [regex]::Match([IO.File]::ReadAllText($manifest), '(?m)^\s*"installdir"\s*"([^"\\/:]+)"')
        if (!$entry.Success -or $entry.Groups[1].Value -in '.', '..') { continue }
        $candidate = Join-Path (Join-Path $steamApps 'common') $entry.Groups[1].Value
        if (Test-GameFolder $candidate) { return $candidate }
    }
}

function Get-GameFolder {
    if ($GamePath) { return $GamePath }
    $found = Find-GameFolder
    if ($found) { return $found }

    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    try {
        $dialog.Description = 'Select the folder containing Chill With You.exe.'
        $dialog.ShowNewFolderButton = $false
        if ($dialog.ShowDialog() -eq [Windows.Forms.DialogResult]::OK) {
            return $dialog.SelectedPath
        }
    } finally { $dialog.Dispose() }
}

function Enable-HiddenManager([string]$Config) {
    $section = [regex]::new('(?ms)^\[Chainloader\][^\S\r\n]*\r?\n(?<body>.*?)(?=^\[|\z)')
    $setting = [regex]::new('(?m)^HideManagerGameObject[^\S\r\n]*=[^\r\n]*')
    $nl = [Environment]::NewLine
    if (!(Test-Path -LiteralPath $Config -PathType Leaf)) {
        [IO.File]::WriteAllText($Config, "[Chainloader]$($nl)HideManagerGameObject = true$nl")
        return
    }
    $original = [IO.File]::ReadAllText($Config)
    $match = $section.Match($original)
    if ($match.Success) {
        $body = $match.Groups['body']
        $updatedBody = if ($setting.IsMatch($body.Value)) {
            $setting.Replace($body.Value, 'HideManagerGameObject = true')
        } else {
            "HideManagerGameObject = true$nl" + $body.Value
        }
        $updated = $original.Substring(0, $body.Index) + $updatedBody + $original.Substring($body.Index + $body.Length)
    } else {
        $updated = $original.TrimEnd() + "$nl$nl[Chainloader]$($nl)HideManagerGameObject = true$nl"
    }
    if ($updated -ceq $original) { return }
    $backup = "$Config.before-ChillPortrait"
    if (!(Test-Path -LiteralPath $backup)) {
        Copy-Item -LiteralPath $Config -Destination $backup
    }
    [IO.File]::WriteAllText($Config, $updated)
}

if (![Environment]::Is64BitOperatingSystem) { throw '64-bit Windows is required.' }
$selected = Get-GameFolder
if (!(Test-GameFolder $selected)) {
    throw 'Game folder not found. Select the folder containing Chill With You.exe.'
}
$game = (Resolve-Path -LiteralPath $selected).ProviderPath
Write-Host "Game folder: $game"
if (Get-Process -Name 'Chill With You' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq (Join-Path $game $gameExe) }) {
    throw 'Close the game via End Call, then run this installer again.'
}

$pluginDir = Join-Path $game 'BepInEx\plugins'
$target = Join-Path $pluginDir 'ChillPortrait.dll'
if ($Uninstall) {
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        Remove-Item -LiteralPath $target
        Write-Host 'Chill Portrait removed. BepInEx and settings were kept.'
    } else {
        Write-Host 'Chill Portrait is already absent.'
    }
    return
}

$plugin = Join-Path $PSScriptRoot 'ChillPortrait.dll'
if (!(Test-Path -LiteralPath $plugin -PathType Leaf)) {
    throw 'ChillPortrait.dll is missing. Extract the full release ZIP before installing.'
}

$beDir = Join-Path $game 'BepInEx'
$beDll = Join-Path $beDir 'core\BepInEx.dll'
$doorstop = Join-Path $game 'doorstop_config.ini'
$injector = Join-Path $game 'winhttp.dll'
$hasLoader = Test-Path -LiteralPath $beDll -PathType Leaf
if ($hasLoader) {
    $version = [Reflection.AssemblyName]::GetAssemblyName($beDll).Version
    if ($version.Major -ne 5) { throw "BepInEx $version is installed. This mod uses BepInEx 5." }
    if (!(Test-Path -LiteralPath $doorstop -PathType Leaf) -or !(Test-Path -LiteralPath $injector -PathType Leaf)) {
        throw 'The existing BepInEx 5 installation is missing Windows loader files. Repair it first.'
    }
    if ([IO.File]::ReadAllText($doorstop) -match '(?im)^\s*enabled\s*=\s*false\s*$') {
        throw 'BepInEx is disabled in doorstop_config.ini. Enable it before installing the mod.'
    }
    Write-Host "Reusing BepInEx $version."
} else {
    foreach ($existing in @($beDir, $doorstop, $injector, (Join-Path $game '.doorstop_version'))) {
        if (Test-Path -LiteralPath $existing) {
            throw "Existing loader files detected at $existing. Repair or remove that loader before installing."
        }
    }

    Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1')
    Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Archive\Microsoft.PowerShell.Archive.psd1')
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $work = [IO.Path]::GetFullPath((Join-Path $tempRoot ('ChillPortrait-' + [guid]::NewGuid().ToString('N'))))
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        $archive = $LoaderArchive
        if (!$archive) {
            $archive = Join-Path $work 'BepInEx.zip'
            Write-Host 'Downloading BepInEx 5.4.23.5 from GitHub...'
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri $loaderUrl -UseBasicParsing -OutFile $archive
        }
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $loaderHash) {
            throw 'BepInEx archive failed SHA-256 verification. No game files were copied.'
        }
        $expanded = Join-Path $work 'loader'
        Expand-Archive -LiteralPath $archive -DestinationPath $expanded
        foreach ($entry in @('BepInEx', 'doorstop_config.ini', '.doorstop_version', 'winhttp.dll')) {
            Copy-Item -LiteralPath (Join-Path $expanded $entry) -Destination (Join-Path $game $entry) -Recurse
        }
    } finally {
        if ([IO.Path]::GetDirectoryName($work) -eq $tempRoot.TrimEnd('\', '/')) {
            Remove-Item -LiteralPath $work -Recurse -Force
        }
    }
}

$configDir = Join-Path $beDir 'config'
New-Item -ItemType Directory -Force -Path $pluginDir, $configDir | Out-Null
Enable-HiddenManager (Join-Path $configDir 'BepInEx.cfg')
Copy-Item -LiteralPath $plugin -Destination $target -Force
Write-Host 'Chill Portrait installed. Start the game from Steam.'
