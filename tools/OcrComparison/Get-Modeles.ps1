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
        # Va avec les modèles small et medium. Le tier tiny a le sien, ppocrv6_tiny_dict.txt.
        Destination = 'models\v6\ppocrv6_dict.txt'
        Url         = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/paddle/PP-OCRv6/rec/PP-OCRv6_rec_small/ppocrv6_dict.txt'
        Sha256      = 'B5F2BFE2BDD9448429E3E82B51C789775D9B42F2403D082B00662EB77E401C5D'
    }
    @{
        Destination = 'models\v6\PP-LCNet_x0_25_textline_ori.onnx'
        Url         = 'https://huggingface.co/PaddlePaddle/PP-LCNet_x0_25_textline_ori_onnx/resolve/main/inference.onnx'
        Sha256      = '94A6A0A0425F2B5F08B5DF72086F2D72ABE40F1D22F6D12D2CD83674F11F2FF3'
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
