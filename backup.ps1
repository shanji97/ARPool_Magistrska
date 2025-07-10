# Set paths
$source = "C:\Users\Aleksander\Desktop\ARPool_Magistrska\PTS_2"
$destination = "C:\Users\Aleksander\OneDrive - Univerza v Ljubljani\Magistrska\Backup"

# Exclude Library, Temp, Logs, Obj
$exclude = @("Library", "Temp", "Logs", "Obj")

# Create destination if not exists
if (!(Test-Path -Path $destination)) {
    New-Item -ItemType Directory -Path $destination
}

# Copy with exclusions
Get-ChildItem -Path $source -Recurse | Where-Object {
    $relativePath = $_.FullName.Substring($source.Length + 1)
    $exclude -notcontains $relativePath.Split('\')[0]
} | ForEach-Object {
    $destPath = Join-Path $destination ($_.FullName.Substring($source.Length + 1))
    if ($_.PSIsContainer) {
        New-Item -ItemType Directory -Path $destPath -Force | Out-Null
    } else {
        Copy-Item $_.FullName -Destination $destPath -Force
    }
}

Write-Host "Backup completed to $destination"