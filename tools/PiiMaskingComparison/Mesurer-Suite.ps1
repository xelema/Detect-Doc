<#
.SYNOPSIS
Enchaîne les sept mesures de référence du comparatif et consolide leurs résultats.

.DESCRIPTION
Chaque configuration tourne dans son propre conteneur, démarré puis détruit avant le suivant :
les mesures de mémoire ne se recouvrent pas. Une pause laisse la machine retomber au repos
entre deux runs. Compter environ une heure, les deux tiers étant pris par GLiNER.

Le tableau consolidé part dans `run/suite-<date>.md`.

.EXAMPLE
.\Mesurer-Suite.ps1
#>
param(
    [ValidateRange(0, 2147483647)][int]$PauseSecondes = 15,
    [string]$Image = 'detectdoc-pii-comparison'
)

$ErrorActionPreference = 'Stop'

$spacy = @{ PII_PRESIDIO_MODELE = 'fr_core_news_md'; PII_PRESIDIO_ENTITES = 'PERSON,EMAIL_ADDRESS,PHONE_NUMBER,IBAN_CODE' }
$huit = @{ PII_CONCURRENCE = 8; PII_TRAVAILLEURS = 8 }

$configurations = @(
    @{ Libelle = 'règles seules';                Moteur = 'regex';           Variables = @{} }
    @{ Libelle = 'Presidio spaCy, 1 document';    Moteur = 'presidio';        Variables = $spacy }
    @{ Libelle = 'hybride Presidio, 1 document';  Moteur = 'hybride';         Variables = $spacy }
    @{ Libelle = 'hybride Presidio, 8 documents'; Moteur = 'hybride';         Variables = $spacy + $huit }
    @{ Libelle = 'GLiNER2.5, 1 document';         Moteur = 'gliner2';         Variables = @{} }
    @{ Libelle = 'hybride GLiNER2.5, 1 document'; Moteur = 'hybride-gliner2'; Variables = @{} }
    @{ Libelle = 'hybride GLiNER2.5, 8 documents';Moteur = 'hybride-gliner2'; Variables = $huit }
)

function Get-Cellule {
    param([string[]]$Recap, [string]$Intitule, [string]$Default)

    $ligne = $Recap | Where-Object { $_ -like "| $Intitule |*" } | Select-Object -First 1
    if (-not $ligne) {
        if ($Default) { return $Default }
        throw "Métrique absente du récapitulatif : $Intitule."
    }
    return $ligne.Split('|')[2].Trim()
}

$depart = Get-Date
$resume = [System.Collections.Generic.List[object]]::new()

foreach ($configuration in $configurations) {
    Write-Host ''
    Write-Host "=== $($configuration.Libelle) ===" -ForegroundColor Cyan
    Write-Host (Get-Date -Format 'HH:mm:ss')

    $dossier = & "$PSScriptRoot\Mesurer-Run.ps1" -Moteur $configuration.Moteur -Variables $configuration.Variables -Image $Image
    if (-not $dossier) {
        throw "Aucun dossier rendu pour $($configuration.Libelle)."
    }

    # Encodage explicite : PowerShell 5.1 lit un fichier UTF-8 sans BOM comme de l'ANSI, et les
    # intitulés accentués du récapitulatif ne correspondraient plus à ceux cherchés ici.
    $recap = Get-Content -LiteralPath (Join-Path $dossier 'recap.md') -Encoding UTF8
    if ((Get-Cellule $recap 'Documents en erreur') -ne '0') {
        throw "Documents en erreur pour $($configuration.Libelle). Consulter $dossier\recap.md."
    }
    $coverage = Get-Cellule $recap 'Occurrences couvertes'

    # Un run trop court peut n'avoir aucun échantillon.
    $cheminStats = Join-Path $dossier 'stats.csv'
    $cpu = @(); $ram = @()
    if (Test-Path -LiteralPath $cheminStats) {
        $stats = @(Import-Csv -LiteralPath $cheminStats -Encoding UTF8)
        # Export-Csv emploie la culture du processus, y compris la virgule décimale française.
        $cpu = @($stats | ForEach-Object { [double]::Parse($_.cpu_pourcent) } | Sort-Object)
        $ram = @($stats | ForEach-Object { [double]::Parse($_.memoire_mo) } | Sort-Object)
    }

    $resume.Add([pscustomobject]@{
        Configuration = $configuration.Libelle
        Dossier       = Split-Path $dossier -Leaf
        Couverture    = $coverage
        HorsVocabu    = Get-Cellule $recap 'Occurrences couvertes hors règles de vocabulaire' -Default $coverage
        Residuels     = Get-Cellule $recap 'Documents avec au moins une PII résiduelle'
        Debit         = Get-Cellule $recap 'Débit'
        Mediane       = Get-Cellule $recap 'Temps médian par document'
        RamMax        = if ($ram.Count) { '{0:N0} Mo' -f $ram[-1] } else { '-' }
        CpuMedian     = if ($cpu.Count) { '{0:N0} %' -f $cpu[[math]::Floor(($cpu.Count - 1) / 2)] } else { '-' }
        CpuMax        = if ($cpu.Count) { '{0:N0} %' -f $cpu[-1] } else { '-' }
    })

    # La machine doit retomber au repos avant la mesure suivante.
    if ($configuration -ne $configurations[-1]) { Start-Sleep -Seconds $PauseSecondes }
}

$runs = Join-Path $PSScriptRoot 'PiiMaskingComparison\run'
$chemin = Join-Path $runs ("suite-{0:yyyy-MM-dd_HH-mm-ss}.md" -f $depart)

$lignes = @(
    "# Suite de mesures du $(Get-Date $depart -Format 'dd/MM/yyyy HH:mm')",
    '',
    "Sept configurations enchaînées, chacune dans son propre conteneur, $PauseSecondes s de pause entre deux.",
    "Durée totale : $([math]::Round(((Get-Date) - $depart).TotalMinutes, 1)) minutes.",
    '',
    '| Configuration | Couverture | Hors vocabulaire | Documents résiduels | Débit | Médiane | RAM max | CPU médian | CPU max | Dossier |',
    '|---|---|---|---|---|---|---|---|---|---|'
)
foreach ($r in $resume) {
    $lignes += "| $($r.Configuration) | $($r.Couverture) | $($r.HorsVocabu) | $($r.Residuels) | $($r.Debit) | $($r.Mediane) | $($r.RamMax) | $($r.CpuMedian) | $($r.CpuMax) | ``$($r.Dossier)`` |"
}

$lignes | Out-File -FilePath $chemin -Encoding utf8
Write-Host ''
Write-Host "Suite terminée. Tableau consolidé : $chemin" -ForegroundColor Green
$resume | Format-Table -AutoSize
