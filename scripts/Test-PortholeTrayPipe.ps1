param(
    [switch]$StartTray,
    [int]$StartupTimeoutSeconds = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$pipeName = 'Porthole.Images.v2'

function Start-PortholeTray {
    $existing = Get-Process Porthole.Tray -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($existing) {
        return $false
    }

    Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', 'src/Porthole.Tray', '-c', 'Debug') -WorkingDirectory $repoRoot | Out-Null
    return $true
}

function Wait-ForPipe {
    param(
        [string]$Name,
        [int]$TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $client = [System.IO.Pipes.NamedPipeClientStream]::new('.', $Name, [System.IO.Pipes.PipeDirection]::InOut)
        try {
            $client.Connect(250)
            return $client
        }
        catch [TimeoutException] {
            $client.Dispose()
        }
        catch [System.IO.IOException] {
            $client.Dispose()
        }
    }

    throw "Timed out waiting for pipe '$Name'."
}

function Invoke-PipeRequest {
    param(
        [int]$Operation,
        [string]$ImageReference,
        [string]$NewTag
    )

    $client = Wait-ForPipe -Name $pipeName -TimeoutSeconds $StartupTimeoutSeconds
    $writer = $null
    $reader = $null
    try {
        $writer = [System.IO.StreamWriter]::new($client, [System.Text.Encoding]::UTF8, 1024, $true)
        $reader = [System.IO.StreamReader]::new($client, [System.Text.Encoding]::UTF8, $false, 1024, $true)

        $request = @{
            Operation = $Operation
            ImageReference = $ImageReference
            NewTag = $NewTag
        }

        $envelope = @{
            Kind = 0
            Message = ($request | ConvertTo-Json -Compress)
            Snapshot = $null
            Images = $null
            Progress = $null
        }

        $writer.WriteLine(($envelope | ConvertTo-Json -Compress))
    $writer.Flush()

        while ($true) {
            $line = $reader.ReadLine()
            if ($null -eq $line) {
                throw 'The tray pipe closed before returning a response.'
            }

            $response = $line | ConvertFrom-Json
            if ($response.Kind -eq 2) {
                return $response
            }

            if ($response.Kind -eq 3) {
                $message = if ($null -ne $response.Message -and $response.Message -ne '') {
                    [string]$response.Message
                }
                else {
                    'The tray pipe returned an unspecified error.'
                }

                throw $message
            }
        }
    }
    finally {
        if ($writer) {
            try { $writer.Dispose() } catch [System.IO.IOException] { }
        }

        if ($reader) {
            try { $reader.Dispose() } catch [System.IO.IOException] { }
        }

        try { $client.Dispose() } catch [System.IO.IOException] { }
    }
}

$startedTray = $false
if ($StartTray) {
    $startedTray = Start-PortholeTray
}

$snapshot = Invoke-PipeRequest -Operation 10 -ImageReference $null -NewTag $null
$images = Invoke-PipeRequest -Operation 0 -ImageReference $null -NewTag $null

[pscustomobject]@{
    StartedTray = $startedTray
    Snapshot = $snapshot.Snapshot
    ImageCount = @($images.Images).Count
    Images = @($images.Images | Select-Object Repository, Tag, Reference)
} | ConvertTo-Json -Depth 6
