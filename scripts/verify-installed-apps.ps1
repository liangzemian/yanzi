param(
    [int]$Top = 80
)

$ErrorActionPreference = "Stop"

$roots = @(
    [pscustomobject]@{ Path = [Environment]::GetFolderPath("StartMenu"); Recurse = $true },
    [pscustomobject]@{ Path = [Environment]::GetFolderPath("CommonStartMenu"); Recurse = $true },
    [pscustomobject]@{ Path = [Environment]::GetFolderPath("DesktopDirectory"); Recurse = $false },
    [pscustomobject]@{ Path = [Environment]::GetFolderPath("CommonDesktopDirectory"); Recurse = $false }
) | Where-Object { $_.Path -and (Test-Path $_.Path) }

$shell = New-Object -ComObject WScript.Shell
$results = New-Object System.Collections.Generic.List[object]
$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

function Test-ExcludedTitle([string]$title) {
    $lower = $title.ToLowerInvariant()
    return $lower.Contains('卸载') -or
        $lower.Contains('uninstall') -or
        $lower.Contains('install') -or
        $lower.Contains('repair') -or
        $lower.Contains('update') -or
        $lower.Contains('帮助') -or
        $lower.Contains('help') -or
        $lower.Contains('readme')
}

foreach ($root in $roots) {
    $items = if ($root.Recurse) {
        Get-ChildItem $root.Path -Recurse -Include *.lnk,*.exe,*.appref-ms -File -ErrorAction SilentlyContinue
    } else {
        Get-ChildItem $root.Path -Include *.lnk,*.exe,*.appref-ms -File -ErrorAction SilentlyContinue
    }

    $items | ForEach-Object {
        $path = $_.FullName
        $title = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
        if ([string]::IsNullOrWhiteSpace($title) -or (Test-ExcludedTitle $title)) {
            return
        }

        $launchTarget = $path
        $displayPath = $path
        $arguments = ''

        if ($_.Extension -ieq '.lnk') {
            try {
                $shortcut = $shell.CreateShortcut($path)
                if ($null -ne $shortcut) {
                    if (-not [string]::IsNullOrWhiteSpace($shortcut.TargetPath)) {
                        $displayPath = $shortcut.TargetPath.Trim()
                        if (Test-Path $displayPath) {
                            $launchTarget = $displayPath
                        }
                    }
                    if (-not [string]::IsNullOrWhiteSpace($shortcut.Arguments) -and $launchTarget -ne $path) {
                        $arguments = $shortcut.Arguments.Trim()
                    }
                }
            } catch {
            }
        }

        $key = ($title.Trim() + '|' + $launchTarget.Trim()).ToLowerInvariant()
        if (-not $seen.Add($key)) {
            return
        }

        $results.Add([pscustomobject]@{
            Title = $title
            LaunchTarget = $launchTarget
            Arguments = $arguments
            SourcePath = $path
        }) | Out-Null
    }
}

$ordered = $results | Sort-Object Title
Write-Host ("Found applications: {0}" -f $ordered.Count)
$ordered | Select-Object -First $Top | Format-Table -AutoSize
