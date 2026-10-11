<#
.SYNOPSIS
    Builds the server and the client, starts the server in its own window (unless it's already running) and runs
    the client here.
.EXAMPLE
    ./etc/run.ps1
.EXAMPLE
    ./etc/run.ps1 -Clients 2          # a second client, logged in as the next dev player, in its own window
.EXAMPLE
    ./etc/run.ps1 --token=dev-player-2 # extra args go to the client
#>
param(
    # How many clients to run. The first runs here as dev-player-1; the others get their own windows.
    [int]$Clients = 1,
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$ClientArgs
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$godot = 'C:\Program Files\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
$serverProject = Join-Path $root 'src\server\Galaxies.Server'
$client = Join-Path $root 'src\client'

dotnet build $serverProject -v q --nologo
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet build $client -v q --nologo
if ($LASTEXITCODE) { exit $LASTEXITCODE }

if (-not (Get-Process Galaxies.Server -ErrorAction SilentlyContinue)) {
    Start-Process dotnet -ArgumentList 'run', '--no-build', '--project', "`"$serverProject`""
    Start-Sleep -Seconds 2
}

for ($i = 2; $i -le $Clients; $i++) {
    Start-Process $godot -ArgumentList '--path', "`"$client`"", '--', "--token=dev-player-$i"
}

& $godot --path $client -- @ClientArgs
