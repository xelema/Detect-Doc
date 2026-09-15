# Echantillonne `docker stats` du conteneur API pendant un run K6.
# Un CSV par run, nomme avec le meme identifiant que K6_RUN_ID pour la correlation.
param(
    # Vide : resout le conteneur a partir de l'image detect-doc:local (nom compose ou aleatoire).
    [string]$ContainerName = '',
    [int]$IntervalSeconds = 5,
    [string]$RunId = '',
    [string]$OutputDir = (Join-Path $PSScriptRoot 'results')
)

if ($RunId -eq '') { $RunId = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss' }

if ($ContainerName -eq '') {
    $ContainerName = docker ps --filter 'ancestor=detect-doc:local' --format '{{.Names}}' |
        Select-Object -First 1
    if (-not $ContainerName) {
        Write-Error "Aucun conteneur detect-doc:local en cours. Passe -ContainerName ou demarre l'API."
        exit 1
    }
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$outputFile = Join-Path $OutputDir "stats-$RunId.csv"

'timestamp;container;cpu_percent;mem_usage;mem_percent;net_io;block_io;pids' |
    Out-File -FilePath $outputFile -Encoding utf8

Write-Host "Echantillonnage de '$ContainerName' toutes les ${IntervalSeconds}s vers $outputFile (Ctrl+C pour arreter)"

while ($true) {
    $line = docker stats --no-stream --format '{{.Name}};{{.CPUPerc}};{{.MemUsage}};{{.MemPerc}};{{.NetIO}};{{.BlockIO}};{{.PIDs}}' $ContainerName 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "docker stats a echoue pour '$ContainerName'"
    }
    elseif ($line) {
        '{0};{1}' -f (Get-Date -Format 'yyyy-MM-ddTHH:mm:ss'), $line |
            Out-File -FilePath $outputFile -Append -Encoding utf8
    }
    Start-Sleep -Seconds $IntervalSeconds
}
