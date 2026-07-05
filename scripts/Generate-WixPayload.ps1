param(
    [Parameter(Mandatory = $true)]
    [string]$AppSource,
    [Parameter(Mandatory = $true)]
    [string]$TraySource,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

function Get-StableId {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prefix,
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $sha1 = [System.Security.Cryptography.SHA1]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())
        $hash = $sha1.ComputeHash($bytes)
        $short = -join ($hash[0..7] | ForEach-Object { $_.ToString('x2') })
        return "${Prefix}_$short"
    }
    finally {
        $sha1.Dispose()
    }
}

function Escape-Xml {
    param([string]$Value)
    return [System.Security.SecurityElement]::Escape($Value)
}

function Get-RelativePath {
    param(
        [string]$BasePath,
        [string]$Path
    )

    $baseUri = [Uri]((Resolve-Path -LiteralPath $BasePath).Path.TrimEnd('\\') + '\\')
    $pathUri = [Uri](Resolve-Path -LiteralPath $Path).Path
    $relativeUri = $baseUri.MakeRelativeUri($pathUri)
    return [Uri]::UnescapeDataString($relativeUri.ToString()).Replace('/', '\\')
}

function New-PayloadMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceRoot,
        [Parameter(Mandatory = $true)]
        [string]$Prefix,
        [Parameter(Mandatory = $true)]
        [string]$RootDirectoryId,
        [Parameter(Mandatory = $true)]
        [string]$ComponentGroupId
    )

    $sourceRootResolved = (Resolve-Path -LiteralPath $SourceRoot).Path
    $directoryIdByRelativePath = @{}
    $directoryIdByRelativePath[''] = $RootDirectoryId

    $directories = Get-ChildItem -LiteralPath $sourceRootResolved -Directory -Recurse | Sort-Object FullName
    foreach ($directory in $directories) {
        $relative = $directory.FullName.Substring($sourceRootResolved.Length).TrimStart('\\').Replace('/', '\\').Trim('\\')
        $directoryIdByRelativePath[$relative] = Get-StableId -Prefix "DIR_$Prefix" -Value $relative
    }

    $components = @()
    $files = Get-ChildItem -LiteralPath $sourceRootResolved -File -Recurse | Where-Object { $_.Extension -ne '.pdb' } | Sort-Object FullName
    foreach ($file in $files) {
        $relativeFilePath = $file.FullName.Substring($sourceRootResolved.Length).TrimStart('\\').Replace('/', '\\').Trim('\\')
        $relativeDirectory = $file.DirectoryName.Substring($sourceRootResolved.Length).TrimStart('\\').Replace('/', '\\').Trim('\\')
        if ([string]::IsNullOrWhiteSpace($relativeDirectory) -or $relativeDirectory -eq '.') {
            $relativeDirectory = ''
        }

        $directoryId = $RootDirectoryId
        if ($relativeDirectory -ne '') {
            $directoryId = Get-StableId -Prefix "DIR_$Prefix" -Value $relativeDirectory
        }
        $componentId = Get-StableId -Prefix "CMP_$Prefix" -Value $relativeFilePath
        $fileId = Get-StableId -Prefix "FIL_$Prefix" -Value $relativeFilePath

        $components += [PSCustomObject]@{
            DirectoryId = $directoryId
            ComponentId = $componentId
            FileId = $fileId
            Source = $file.FullName
        }
    }

    return [PSCustomObject]@{
        SourceRoot = $sourceRootResolved
        Prefix = $Prefix
        RootDirectoryId = $RootDirectoryId
        ComponentGroupId = $ComponentGroupId
        DirectoryIdByRelativePath = $directoryIdByRelativePath
        Components = $components
    }
}

function Write-Directories {
    param(
        [System.Text.StringBuilder]$Builder,
        $Payload
    )

    foreach ($entry in $Payload.DirectoryIdByRelativePath.GetEnumerator() | Where-Object { $_.Key -ne '' } | Sort-Object Key) {
        $relativePath = $entry.Key.Replace('/', '\\').Trim('\\')
        $directoryId = $entry.Value
        $name = Split-Path -Path $relativePath -Leaf
        $parentRelativePath = Split-Path -Path $relativePath -Parent
        if ([string]::IsNullOrWhiteSpace($parentRelativePath) -or $parentRelativePath -eq '.') {
            $parentRelativePath = ''
        }
        else {
            $parentRelativePath = $parentRelativePath.Replace('/', '\\').Trim('\\')
        }

        $parentId = $Payload.DirectoryIdByRelativePath[$parentRelativePath]
        if ([string]::IsNullOrWhiteSpace($parentId)) {
            $parentId = $Payload.RootDirectoryId
        }
        [void]$Builder.AppendLine('  <Fragment>')
        [void]$Builder.AppendLine("    <DirectoryRef Id=`"$(Escape-Xml $parentId)`">")
        [void]$Builder.AppendLine("      <Directory Id=`"$(Escape-Xml $directoryId)`" Name=`"$(Escape-Xml $name)`" />")
        [void]$Builder.AppendLine('    </DirectoryRef>')
        [void]$Builder.AppendLine('  </Fragment>')
    }
}

function Write-Components {
    param(
        [System.Text.StringBuilder]$Builder,
        $Payload
    )

    [void]$Builder.AppendLine('  <Fragment>')
    [void]$Builder.AppendLine("    <ComponentGroup Id=`"$(Escape-Xml $Payload.ComponentGroupId)`">")

    foreach ($component in $Payload.Components) {
        [void]$Builder.AppendLine("      <Component Id=`"$(Escape-Xml $component.ComponentId)`" Directory=`"$(Escape-Xml $component.DirectoryId)`" Guid=`"*`">")
        [void]$Builder.AppendLine("        <File Id=`"$(Escape-Xml $component.FileId)`" Source=`"$(Escape-Xml $component.Source)`" />")
        [void]$Builder.AppendLine('      </Component>')
    }

    [void]$Builder.AppendLine('    </ComponentGroup>')
    [void]$Builder.AppendLine('  </Fragment>')
}

if (-not (Test-Path -LiteralPath $AppSource)) {
    throw "App payload directory was not found: $AppSource"
}

if (-not (Test-Path -LiteralPath $TraySource)) {
    throw "Tray payload directory was not found: $TraySource"
}

$outputDirectory = Split-Path -Path $OutputPath -Parent
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$appPayload = New-PayloadMetadata -SourceRoot $AppSource -Prefix 'APP' -RootDirectoryId 'INSTALLDIR_APP' -ComponentGroupId 'AppPayloadComponents'
$trayPayload = New-PayloadMetadata -SourceRoot $TraySource -Prefix 'TRAY' -RootDirectoryId 'INSTALLDIR_TRAY' -ComponentGroupId 'TrayPayloadComponents'

$builder = [System.Text.StringBuilder]::new()
[void]$builder.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
[void]$builder.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
Write-Directories -Builder $builder -Payload $appPayload
Write-Directories -Builder $builder -Payload $trayPayload
Write-Components -Builder $builder -Payload $appPayload
Write-Components -Builder $builder -Payload $trayPayload
[void]$builder.AppendLine('</Wix>')

Set-Content -LiteralPath $OutputPath -Value $builder.ToString() -Encoding UTF8
