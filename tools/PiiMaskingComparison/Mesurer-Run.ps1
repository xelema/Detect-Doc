<#
.SYNOPSIS
Lance un ou plusieurs runs du comparatif dans des conteneurs et releve leur consommation.

.DESCRIPTION
`docker stats` mesure le groupe de controle du conteneur, donc les deux processus, .NET et
Python. C'est la seule mesure qui n'ait pas a faire confiance a ce que les programmes declarent.
Chaque echantillon est pris par un appel ponctuel : le mode flux de `docker stats` redessine un
affichage vivant et seme des sequences ANSI dans le fichier.

Le releve horodate part dans `stats.csv`, aux cotes de `recap.md`, avec un resume dans
`stats.md`. `-Parallele` lance plusieurs conteneurs a la fois pour observer ce que coute la
concurrence entre replicas.

.EXAMPLE
.\Mesurer-Run.ps1 -Moteur hybride-gliner2

.EXAMPLE
.\Mesurer-Run.ps1 -Moteur presidio -Parallele 3
#>
param(
    [Parameter(Mandatory)][string]$Moteur,
    [hashtable]$Variables = @{},
    [ValidateRange(1, 2147483647)][int]$Parallele = 1,
    [ValidateRange(0.001, [double]::MaxValue)][double]$IntervalleSecondes = 1,
    [string]$Image = 'detectdoc-pii-comparison'
)

$ErrorActionPreference = 'Stop'

function Invoke-Docker {
    param([string[]]$Arguments)

    # Sous PowerShell 5.1, stderr peut contenir un diagnostic sans signaler un échec.
    $ErrorActionPreference = 'Continue'
    $output = docker @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Docker $($Arguments[0]) a échoué (code $LASTEXITCODE) : $($output -join [Environment]::NewLine)"
    }
    foreach ($line in $output) { $line.ToString() }
}

function ConvertTo-Megaoctets {
    param([string]$Mesure)

    # `docker stats` rend "393.5MiB / 15.4GiB" : seule la partie gauche nous interesse.
    $valeur = $Mesure.Split('/')[0].Trim()
    if ($valeur -notmatch '^([0-9.]+)\s*([KMG]i?B|B)$') { return 0 }

    $nombre = [double]$Matches[1]
    switch ($Matches[2]) {
        'B'   { $nombre / 1MB }
        'KiB' { $nombre / 1KB }
        'KB'  { $nombre / 1KB }
        'MiB' { $nombre }
        'MB'  { $nombre }
        'GiB' { $nombre * 1KB }
        'GB'  { $nombre * 1KB }
        default { 0 }
    }
}

function Get-Courbe {
    param([double[]]$Valeurs, [int]$Colonnes = 60)

    if (-not $Valeurs) { return '' }
    $paliers = ' .:-=+*#%@'.ToCharArray()
    $maximum = ($Valeurs | Measure-Object -Maximum).Maximum
    if ($maximum -le 0) { return ''.PadRight($Colonnes, $paliers[0]) }

    $par = [math]::Max(1, [math]::Ceiling($Valeurs.Count / $Colonnes))
    $courbe = ''
    for ($i = 0; $i -lt $Valeurs.Count; $i += $par) {
        $tranche = $Valeurs[$i..([math]::Min($i + $par - 1, $Valeurs.Count - 1))]
        $moyenne = ($tranche | Measure-Object -Average).Average
        $rang = [math]::Min($paliers.Count - 1, [math]::Floor($moyenne / $maximum * ($paliers.Count - 1)))
        $courbe += $paliers[$rang]
    }
    return $courbe
}

function Write-Resume {
    param(
        [object[]]$Echantillons,
        [string]$Chemin,
        [string]$Moteur,
        [int]$Parallele
    )

    $lignes = @("# Consommation du run ``$Moteur``", '')
    $duree = ($Echantillons | Measure-Object -Property seconde -Maximum).Maximum
    $lignes += "Releve par ``docker stats``, $($Echantillons.Count) echantillons sur $duree s, $Parallele conteneur(s)."
    $lignes += ''
    $lignes += '| Conteneur | CPU median | CPU max | RAM mediane | RAM max |'
    $lignes += '|---|---|---|---|---|'

    foreach ($groupe in $Echantillons | Group-Object conteneur) {
        $cpu = @($groupe.Group.cpu_pourcent | Sort-Object)
        $ram = @($groupe.Group.memoire_mo | Sort-Object)
        # Médiane basse, comme celle des durées dans le récapitulatif du benchmark.
        $lignes += ('| {0} | {1:N0} % | {2:N0} % | {3:N0} Mo | {4:N0} Mo |' -f $groupe.Name,
            $cpu[[math]::Floor(($cpu.Count - 1) / 2)], $cpu[-1], $ram[[math]::Floor(($ram.Count - 1) / 2)], $ram[-1])
    }

    if ($Parallele -gt 1) {
        # Le cumul instant par instant, seul chiffre qui dise ce que la machine porte reellement.
        $cumul = $Echantillons | Group-Object seconde | ForEach-Object {
            ($_.Group.memoire_mo | Measure-Object -Sum).Sum
        }
        $lignes += ('| **cumul** | | | | {0:N0} Mo |' -f ($cumul | Measure-Object -Maximum).Maximum)
    }

    $lignes += ''
    $lignes += '## Dans le temps'
    $lignes += ''
    $lignes += 'Une colonne par tranche de temps, du plus creux ` ` au plus plein `@`.'
    $lignes += ''

    foreach ($groupe in $Echantillons | Group-Object conteneur) {
        $serie = $groupe.Group | Sort-Object seconde
        $lignes += '```'
        $lignes += $groupe.Name
        $lignes += ('RAM  {0,6:N0} Mo max |{1}|' -f ($serie.memoire_mo | Measure-Object -Maximum).Maximum, (Get-Courbe -Valeurs $serie.memoire_mo))
        $lignes += ('CPU  {0,6:N0} %  max |{1}|' -f ($serie.cpu_pourcent | Measure-Object -Maximum).Maximum, (Get-Courbe -Valeurs $serie.cpu_pourcent))
        $fin = "$duree s"
        $lignes += ('     0 s'.PadRight(66 - $fin.Length) + $fin)
        $lignes += '```'
        $lignes += ''
    }

    $lignes | Out-File -FilePath $Chemin -Encoding utf8
}


Get-Command docker -ErrorAction Stop | Out-Null
$corpus = (Resolve-Path -LiteralPath "$PSScriptRoot\..\..\corpus").Path
if (-not (Test-Path -LiteralPath (Join-Path $corpus 'manifest.jsonl') -PathType Leaf)) {
    throw "Manifest absent de $corpus."
}
$runs = Join-Path $PSScriptRoot 'PiiMaskingComparison\run'
New-Item -ItemType Directory -Force -Path $runs | Out-Null
$avant = Get-ChildItem $runs -Directory | Select-Object -ExpandProperty Name

$horodatage = '{0:HHmmss}-{1}' -f (Get-Date), [guid]::NewGuid().ToString('N').Substring(0, 8)
$noms = @(1..$Parallele | ForEach-Object { "pii-$Moteur-$horodatage-$_" })
$started = [System.Collections.Generic.List[string]]::new()
$echantillons = [System.Collections.Generic.List[object]]::new()
$depart = Get-Date

try {
    foreach ($nom in $noms) {
        $arguments = @('run', '--detach', '--name', $nom,
            '--volume', "${corpus}:/corpus:ro", '--volume', "${runs}:/work/run")
        foreach ($cle in $Variables.Keys) {
            $arguments += @('--env', "$cle=$($Variables[$cle])")
        }
        $arguments += @($Image, '/corpus/manifest.jsonl', $Moteur)
        Invoke-Docker -Arguments $arguments | Out-Null
        $started.Add($nom)
    }

    while ($true) {
        $vivants = @(Invoke-Docker -Arguments @('ps', '--format', '{{.Names}}', '--filter', "name=pii-$Moteur-$horodatage-"))
        if (-not $vivants) { break }

        # Un appel par tour plutôt qu'un flux : la sortie ponctuelle est propre, sans ANSI.
        try {
            $mesure = Invoke-Docker -Arguments (@('stats', '--no-stream', '--format', '{{.Name}};{{.CPUPerc}};{{.MemUsage}};{{.PIDs}}') + $vivants)
        }
        catch {
            # Le dernier conteneur peut s'arrêter entre docker ps et docker stats.
            if (Invoke-Docker -Arguments @('ps', '--quiet', '--filter', "name=pii-$Moteur-$horodatage-")) { throw }
            break
        }
        $instant = [math]::Round(((Get-Date) - $depart).TotalSeconds, 1)
        foreach ($ligne in $mesure) {
            if ($ligne -match '^\s*$') { continue }
            $champs = ($ligne -replace "$([char]27)\[[0-9;]*[A-Za-z]", '').Split(';')
            if ($champs.Count -lt 4) { continue }
            $echantillons.Add([pscustomobject]@{
                seconde      = $instant
                conteneur    = $champs[0]
                cpu_pourcent = [double]($champs[1] -replace '[%\s]', '')
                memoire_mo   = ConvertTo-Megaoctets $champs[2]
                processus    = [int]$champs[3]
            })
        }
        Start-Sleep -Seconds $IntervalleSecondes
    }

    foreach ($nom in $noms) {
        # Le seul objet rendu par ce script est le dossier du run.
        Invoke-Docker -Arguments @('logs', $nom) | ForEach-Object { Write-Host $_ }
        $exitCode = Invoke-Docker -Arguments @('inspect', '--format', '{{.State.ExitCode}}', $nom)
        if ($exitCode -ne '0') { throw "Le conteneur $nom a échoué (code $exitCode)." }
    }
}
finally {
    if ($started.Count) { Invoke-Docker -Arguments (@('rm', '--force') + $started.ToArray()) | Out-Null }
}

$dossiers = Get-ChildItem $runs -Directory | Where-Object { $_.Name -notin $avant } | Sort-Object CreationTime
if (-not $dossiers) {
    throw 'Aucun dossier de run créé.'
}

$cible = $dossiers[-1].FullName
$echantillons | Export-Csv -Path (Join-Path $cible 'stats.csv') -NoTypeInformation -Encoding utf8
Write-Resume -Echantillons $echantillons -Chemin (Join-Path $cible 'stats.md') -Moteur $Moteur -Parallele $Parallele
Write-Host "Releve ecrit dans $cible"
Write-Output $cible
