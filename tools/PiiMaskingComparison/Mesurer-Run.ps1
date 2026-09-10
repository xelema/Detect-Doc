<#
.SYNOPSIS
Lance un run du comparatif dans un conteneur et releve sa consommation pendant l'execution.

.DESCRIPTION
`docker stats` mesure le groupe de controle du conteneur, donc les deux processus, .NET et
Python. C'est la seule mesure qui n'ait pas a faire confiance a ce que les programmes declarent.
L'echantillon est horodate pour distinguer le chargement du modele du traitement des documents,
puis range dans le dossier du run aux cotes de `recap.md`.

.EXAMPLE
.\Mesurer-Run.ps1 -Moteur hybride-gliner2

.EXAMPLE
.\Mesurer-Run.ps1 -Moteur presidio -Variables @{ PII_PRESIDIO_MODELE = 'fr_core_news_md' }
#>
param(
    [Parameter(Mandatory)][string]$Moteur,
    [hashtable]$Variables = @{},
    [string]$Image = 'detectdoc-pii-comparison'
)

$ErrorActionPreference = 'Stop'

$corpus = (Resolve-Path "$PSScriptRoot\..\..\corpus").Path
$runs = Join-Path $PSScriptRoot 'PiiMaskingComparison\run'
New-Item -ItemType Directory -Force -Path $runs | Out-Null

$nom = "pii-$Moteur-$(Get-Date -Format 'HHmmss')"
$arguments = @('run', '--detach', '--name', $nom,
    '--volume', "${corpus}:/corpus:ro", '--volume', "${runs}:/work/run")
foreach ($cle in $Variables.Keys) {
    $arguments += @('--env', "$cle=$($Variables[$cle])")
}
$arguments += @($Image, '/corpus/manifest.jsonl', $Moteur)

$avant = Get-ChildItem $runs -Directory | Select-Object -ExpandProperty Name
docker @arguments | Out-Null

$releve = Join-Path $env:TEMP "$nom.csv"
'horodatage;cpu_pourcent;memoire;processus' | Out-File -FilePath $releve -Encoding utf8
$echantillonnage = Start-Job -ScriptBlock {
    param($nom, $chemin)
    docker stats $nom --format '{{.CPUPerc}};{{.MemUsage}};{{.PIDs}}' |
        ForEach-Object { "$(Get-Date -Format o);$_" | Out-File -FilePath $chemin -Append -Encoding utf8 }
} -ArgumentList $nom, $releve

try {
    docker wait $nom | Out-Null
}
finally {
    Stop-Job $echantillonnage -ErrorAction SilentlyContinue
    Remove-Job $echantillonnage -Force -ErrorAction SilentlyContinue
}

docker logs $nom
docker rm $nom | Out-Null

# Le nom du dossier de run est decide dans le conteneur : on le retrouve par difference.
$dossier = Get-ChildItem $runs -Directory | Where-Object { $_.Name -notin $avant } |
    Sort-Object CreationTime | Select-Object -Last 1
if ($dossier) {
    Move-Item $releve (Join-Path $dossier.FullName 'stats.csv') -Force
    Write-Host "Releve ecrit dans $($dossier.FullName)\stats.csv"
}
else {
    Write-Host "Aucun dossier de run cree, releve conserve dans $releve"
}
