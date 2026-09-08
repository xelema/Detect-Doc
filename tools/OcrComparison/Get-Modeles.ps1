#Requires -Version 5.1
<#
.SYNOPSIS
    Installe les modèles OCR d'OcrComparison et vérifie leur empreinte.

.DESCRIPTION
    Les modèles ne sont pas versionnés : ils pèsent des dizaines de mégaoctets et se refabriquent
    à l'identique depuis leur source. Seule leur identité l'est, sous forme d'empreinte sha256
    dans la table ci-dessous.

    Un fichier dont l'empreinte ne correspond pas est supprimé plutôt qu'installé : un modèle de
    reconnaissance dépareillé de son dictionnaire ne lève aucune erreur, il rend du texte
    plausible et faux.

.PARAMETER Source
    Répertoire local ou partage réseau où chercher les fichiers, par leur nom, avant de les
    télécharger. Par défaut, la variable d'environnement OCRCOMPARISON_MODELES.

.PARAMETER Force
    Réinstalle aussi les fichiers déjà présents et conformes.

.EXAMPLE
    .\Get-Modeles.ps1

.EXAMPLE
    .\Get-Modeles.ps1 -Source \\serveur\partage\modeles-ocr
#>
[CmdletBinding()]
param(
    [string] $Source = $env:OCRCOMPARISON_MODELES,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$modeles = @(
    @{
        Destination = 'tessdata\fra.traineddata'
        Url         = 'https://github.com/tesseract-ocr/tessdata_fast/raw/87416418657359cb625c412a48b6e1d6d41c29bd/fra.traineddata'
        Sha256      = 'CED037562E8C80C13122DECE28DD477D399AF80911A28791A66A63AC1E3445CA'
    }
    @{
        Destination = 'tessdata\osd.traineddata'
        Url         = 'https://github.com/tesseract-ocr/tessdata_fast/raw/87416418657359cb625c412a48b6e1d6d41c29bd/osd.traineddata'
        Sha256      = '9CF5D576FCC47564F11265841E5CA839001E7E6F38FF7F7AACF46D15A96B00FF'
    }
    @{
        Destination = 'models\v6\PP-OCRv6_det_medium.onnx'
        Url         = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv6/det/PP-OCRv6_det_medium.onnx'
        Sha256      = '92078B7355007CCFFFCD4C8CD441A3AFD4538904D06881B29A155E1E679907C2'
    }
    @{
        Destination = 'models\v6\PP-OCRv6_rec_medium.onnx'
        Url         = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv6/rec/PP-OCRv6_rec_medium.onnx'
        Sha256      = 'EEF444829DBBE18D7FEA59A3F6EB75647518D2B3A9568D27C92E42940204894B'
    }
    @{
        Destination = 'models\v6\PP-OCRv6_det_small.onnx'
        Url         = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv6/det/PP-OCRv6_det_small.onnx'
        Sha256      = '090F04ABCD9D9A7498BC4EBF677E4CB9BDCE1FE4197DDB7E529F1EF44E1FF94F'
    }
    @{
        Destination = 'models\v6\PP-OCRv6_rec_small.onnx'
        Url         = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv6/rec/PP-OCRv6_rec_small.onnx'
        Sha256      = '6F327246B50388F3C176AE304BD95767EA6DC0C9AE92153EF8CBE210B3C14884'
    }
    @{
        # 18 708 caractères, les 50 langues. Va avec small et medium, jamais avec tiny.
        Destination = 'models\v6\ppocrv6_dict.txt'
        Url         = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/paddle/PP-OCRv6/rec/PP-OCRv6_rec_small/ppocrv6_dict.txt'
        Sha256      = 'B5F2BFE2BDD9448429E3E82B51C789775D9B42F2403D082B00662EB77E401C5D'
    }
    @{
        Destination = 'models\v6\PP-OCRv6_det_tiny.onnx'
        Url         = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv6/det/PP-OCRv6_det_tiny.onnx'
        Sha256      = 'F42C0FBD294D95EAC1A550E131B277DAC97462C8025FA4B6C3CEC1B7894BD3D5'
    }
    @{
        Destination = 'models\v6\PP-OCRv6_rec_tiny.onnx'
        Url         = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv6/rec/PP-OCRv6_rec_tiny.onnx'
        Sha256      = 'E16E242DE5937AD92609223F19BC2AFF3727EE40B095F996907C24749BAD251B'
    }
    @{
        # 6 904 caractères seulement : tiny a son propre alphabet, le confondre avec celui de
        # small ne lève aucune erreur et décale tout le texte reconnu.
        Destination = 'models\v6\ppocrv6_tiny_dict.txt'
        Url         = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/paddle/PP-OCRv6/rec/PP-OCRv6_rec_tiny/ppocrv6_tiny_dict.txt'
        Sha256      = 'C5CBE34EF40C29C4DF07ED012BF96569CB69A2D2A01A07027E9F13CB832BD9CD'
    }
)

$racine = Join-Path $PSScriptRoot 'OcrComparison'
$installes = 0

foreach ($modele in $modeles) {
    $cible = Join-Path $racine $modele.Destination
    $nom = Split-Path $modele.Destination -Leaf

    if (-not $Force -and (Test-Path $cible) -and
        (Get-FileHash $cible -Algorithm SHA256).Hash -eq $modele.Sha256) {
        Write-Host "$nom : deja conforme"
        continue
    }

    New-Item -ItemType Directory -Force -Path (Split-Path $cible) | Out-Null

    $copieLocale = if ($Source) { Join-Path $Source $nom } else { $null }
    if ($copieLocale -and (Test-Path $copieLocale)) {
        Write-Host "$nom : copie depuis $Source"
        Copy-Item $copieLocale $cible -Force
    }
    else {
        Write-Host "$nom : telechargement"
        Invoke-WebRequest -Uri $modele.Url -OutFile $cible -UseBasicParsing
    }

    $obtenu = (Get-FileHash $cible -Algorithm SHA256).Hash
    if ($obtenu -ne $modele.Sha256) {
        Remove-Item $cible -Force
        throw "$nom : empreinte $obtenu au lieu de $($modele.Sha256). Fichier supprime."
    }

    $installes++
}

Write-Host ""
Write-Host "$installes fichier(s) installe(s), $($modeles.Count - $installes) deja a jour."
