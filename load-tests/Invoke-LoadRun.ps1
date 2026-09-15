# Orchestre un palier de charge complet en une seule commande :
# capture des logs du conteneur pendant le run, echantillonnage docker stats,
# run K6, puis recapitulatif (statuts, etapes, RAM/CPU max) et meta JSON.
# Un run produit dans results/ : k6-<RunId>.summary.txt, k6-<RunId>.requests.csv,
# stats-<RunId>.csv, logs-<RunId>.txt, run-<RunId>.meta.json.
param(
    [int]$Vus = 20,
    [int]$IterationsPerVu = 5,
    [string]$RunId = '',
    [string]$ContainerName = '',
    [string]$BaseUrl = '',
    [int]$StatsIntervalSeconds = 5,
    [switch]$LocalK6
)

$ErrorActionPreference = 'Stop'
$loadTestsDir = $PSScriptRoot
$repoRoot = Split-Path $loadTestsDir -Parent
$resultsDir = Join-Path $loadTestsDir 'results'
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

if ($RunId -eq '') { $RunId = "vus$Vus" }
$iterations = $Vus * $IterationsPerVu

# Resolution du conteneur par l'image, quel que soit son nom (compose ou aleatoire).
if ($ContainerName -eq '') {
    $ContainerName = docker ps --filter 'ancestor=detect-doc:local' --format '{{.Names}}' |
        Select-Object -First 1
    if (-not $ContainerName) {
        Write-Error "Aucun conteneur detect-doc:local en cours. Demarre l'API ou passe -ContainerName."
        exit 1
    }
}

$commit = git -C $repoRoot rev-parse --short HEAD 2>$null
$branch = git -C $repoRoot branch --show-current 2>$null
$startedAt = (Get-Date).ToUniversalTime()

$statsFile = Join-Path $resultsDir "stats-$RunId.csv"
$logsFile = Join-Path $resultsDir "logs-$RunId.txt"
$metaFile = Join-Path $resultsDir "run-$RunId.meta.json"
$consoleFile = Join-Path $resultsDir "console-$RunId.txt"

Write-Host "Run $RunId : $Vus VUs x $IterationsPerVu iterations ($iterations requetes), conteneur '$ContainerName'"

# 1. Capture des logs du conteneur a partir de maintenant, jusqu'a la fin du run.
$since = $startedAt.ToString('yyyy-MM-ddTHH:mm:ss')
$logArgs = "logs --since $since --follow $ContainerName"
$logProcess = Start-Process -FilePath 'docker' -ArgumentList $logArgs `
    -RedirectStandardOutput $logsFile -RedirectStandardError "$logsFile.err" `
    -NoNewWindow -PassThru

# 2. Echantillonnage docker stats dans un job en arriere-plan.
'timestamp;container;cpu_percent;mem_usage;mem_percent;net_io;block_io;pids' |
    Out-File -FilePath $statsFile -Encoding utf8
$statsJob = Start-Job -ScriptBlock {
    param($container, $file, $interval)
    while ($true) {
        $line = docker stats --no-stream --format '{{.Name}};{{.CPUPerc}};{{.MemUsage}};{{.MemPerc}};{{.NetIO}};{{.BlockIO}};{{.PIDs}}' $container 2>$null
        if ($line) {
            '{0};{1}' -f (Get-Date -Format 'yyyy-MM-ddTHH:mm:ss'), $line |
                Out-File -FilePath $file -Append -Encoding utf8
        }
        Start-Sleep -Seconds $interval
    }
} -ArgumentList $ContainerName, $statsFile, $StatsIntervalSeconds

try {
    # 3. Run K6.
    $corpusDir = Join-Path $repoRoot 'corpus'
    $typesFile = Join-Path $repoRoot 'tools\ClassificationEvaluation\descriptions-types.json'
    if ($LocalK6) {
        $env:DETECTDOC_BASE_URL = if ($BaseUrl) { $BaseUrl } else { 'http://localhost:8080' }
        $env:K6_CORPUS_DIR = $corpusDir
        $env:K6_TYPES_PATH = $typesFile
        $env:LOAD_VUS = "$Vus"
        $env:K6_RUN_ID = $RunId
        $env:K6_RESULTS_DIR = $resultsDir
        k6 run (Join-Path $loadTestsDir 'classify.js') 2>&1 |
            Tee-Object -FilePath $consoleFile
        $k6Exit = $LASTEXITCODE
    }
    else {
        $base = if ($BaseUrl) { $BaseUrl } else { 'http://host.docker.internal:8080' }
        docker run --rm -i `
            -v "${loadTestsDir}:/scripts" `
            -v "${corpusDir}:/corpus:ro" `
            -v "${typesFile}:/scripts/descriptions-types.json:ro" `
            -e "DETECTDOC_BASE_URL=$base" `
            -e K6_CORPUS_DIR=/corpus `
            -e K6_TYPES_PATH=/scripts/descriptions-types.json `
            -e "LOAD_VUS=$Vus" `
            -e "K6_RUN_ID=$RunId" `
            -e K6_RESULTS_DIR=/scripts/results `
            grafana/k6 run /scripts/classify.js 2>&1 |
            Tee-Object -FilePath $consoleFile
        $k6Exit = $LASTEXITCODE
    }
}
finally {
    Stop-Job $statsJob -ErrorAction SilentlyContinue
    Remove-Job $statsJob -Force -ErrorAction SilentlyContinue
    Stop-Process -Id $logProcess.Id -Force -ErrorAction SilentlyContinue
}

# 4. Meta du run : contexte necessaire a la reproductibilite.
$endedAt = (Get-Date).ToUniversalTime()
@{
    runId           = $RunId
    branch          = $branch
    commit          = $commit
    container       = $ContainerName
    vus             = $Vus
    iterations      = $iterations
    requestTimeout  = '120s'
    engine          = 'auto'
    split           = 'dev'
    startedAtUtc    = $startedAt.ToString('o')
    endedAtUtc      = $endedAt.ToString('o')
    k6ExitCode      = $k6Exit
} | ConvertTo-Json | Out-File $metaFile -Encoding utf8

# 5. Reconstitution du CSV de requetes depuis les lignes REQ| de la console K6
# (chaque VU a son contexte JS : seule la sortie console transporte les mesures).
$requestsFile = Join-Path $resultsDir "k6-$RunId.requests.csv"
$header = 'vu,iter,doc,regime,status,duration_ms,moteur,type_predit,confiance,pages,extraction_ms,pii_ms,classification_ms,total_ms,execution_id,error_title'
$reqLines = Select-String -Path $consoleFile -Pattern 'REQ\|req\|(.*)"?\s*$' -ErrorAction SilentlyContinue
if ($reqLines) {
    $header | Out-File $requestsFile -Encoding utf8
    foreach ($m in $reqLines) {
        $fields = $m.Matches[0].Groups[1].Value -replace '"$', '' -split '\|'
        $row = ($fields | ForEach-Object {
                if ($_ -match '[",]') { '"' + ($_ -replace '"', '""') + '"' } else { $_ }
            }) -join ','
        $row | Out-File $requestsFile -Append -Encoding utf8
    }
}

# 6. Recapitulatif : statuts HTTP, durees par etape, RAM/CPU max.
if (Test-Path $requestsFile) {
    $reqs = Import-Csv $requestsFile
    Write-Host "`n=== Statuts HTTP ==="
    $reqs | Group-Object status | Sort-Object Name | ForEach-Object {
        Write-Host ("  {0} : {1}" -f $_.Name, $_.Count)
    }
    $errors = $reqs | Where-Object { $_.status -ne '200' }
    if ($errors) {
        Write-Host "`n=== Titres d'erreur (ProblemDetails) ==="
        $errors | Group-Object error_title | Sort-Object Count -Descending | ForEach-Object {
            Write-Host ("  {0}x {1}" -f $_.Count, $_.Name)
        }
    }
    $ok = $reqs | Where-Object { $_.status -eq '200' }
    if ($ok) {
        Write-Host "`n=== Durees moyennes par etape (reponses 200) ==="
        foreach ($col in 'extraction_ms', 'pii_ms', 'classification_ms', 'total_ms') {
            $avg = ($ok | Measure-Object -Property $col -Average).Average
            Write-Host ("  {0} : {1:N0} ms" -f $col, $avg)
        }
        Write-Host "`n=== Repartition par moteur ==="
        $ok | Group-Object moteur | ForEach-Object {
            $avg = ($_.Group | Measure-Object -Property duration_ms -Average).Average
            Write-Host ("  {0} : {1} docs, duree moyenne {2:N0} ms" -f $_.Name, $_.Count, $avg)
        }
    }
}
if (Test-Path $statsFile) {
    $stats = Import-Csv $statsFile -Delimiter ';'
    $maxCpu = ($stats | ForEach-Object { [double]($_.cpu_percent -replace '%', '') } | Measure-Object -Maximum).Maximum
    $maxMem = ($stats | ForEach-Object {
            $v = ($_.mem_usage -split ' / ')[0]
            if ($v -match 'GiB') { [double]($v -replace 'GiB', '') * 1024 } else { [double]($v -replace 'MiB', '') }
        } | Measure-Object -Maximum).Maximum
    Write-Host "`n=== Conteneur ==="
    Write-Host ("  CPU max : {0}%   RAM max : {1:N0} Mio" -f $maxCpu, $maxMem)
}

Write-Host "`nFichiers du run dans results/ : k6-$RunId.summary.txt, k6-$RunId.requests.csv, stats-$RunId.csv, logs-$RunId.txt, run-$RunId.meta.json"
exit $k6Exit
