# OcrComparison

Compare des moteurs d'extraction de texte sur le corpus de `Detect-Doc`, work item 115215. Un run porte un seul moteur et produit ses mesures de qualité et de coût.

## Installer les modèles

Les modèles ne sont pas dans le dépôt. Avant le premier run :

```powershell
.\Get-Modeles.ps1
```

Le script télécharge chaque fichier et rejette celui dont l'empreinte sha256 ne correspond pas à celle qu'il épingle. Sans réseau vers GitHub ou ModelScope, pointer une copie locale ou un partage :

```powershell
.\Get-Modeles.ps1 -Source \\serveur\partage\modeles-ocr
```

Le script installe Tesseract (`fra`, `osd`) et PP-OCRv6 en tailles `tiny` et `small`. La taille `medium` pèse 138 Mo à elle seule et n'est pas installée par défaut : ajouter ses trois entrées à la table du script pour la mesurer.

Le classifieur d'orientation n'y figure pas, RapidOcrNet le livre dans son paquet sous `models\v5`. PP-OCRv6 le réutilise directement.

**Pourquoi pas dans le dépôt.** Les huit fichiers pèsent 48 Mo, et le triple avec `medium`. Un binaire commité reste dans l'historique pour toujours, y compris quand le modèle sera remplacé. C'est la règle déjà retenue pour le corpus : versionner l'identité, pas les octets. `Get-Modeles.ps1` tient lieu de manifest.

**Chaque taille de PP-OCRv6 a son propre dictionnaire** : 18 708 caractères pour `small` et `medium`, 6 904 pour `tiny`. Les intervertir ne lève aucune erreur, ça décale l'alphabet et rend du texte plausible et faux. C'est la raison d'être de la vérification d'empreinte.

## Lancer un run

```powershell
dotnet run -c Release -- <chemin-du-corpus> <chemin-du-manifest> [moteur] [reconnaissances-paralleles]
```

Moteurs disponibles : `tesseract` (défaut), `pdfpig`, `ppocr-tiny`, `ppocr-small`, `ppocr-medium`. Le corpus est le dossier qui contient `raw/`, le manifest est `corpus/manifest.jsonl`.

Le dernier argument règle le nombre de lignes reconnues en parallèle par RapidOcrNet 4.1.0. Sa valeur par défaut est `1`, pour préserver la ligne de base. Il ne s'applique qu'aux moteurs PP-OCR.

```powershell
dotnet run -c Release -- ..\..\corpus ..\..\corpus\manifest.jsonl tesseract
dotnet run -c Release -- ..\..\corpus ..\..\corpus\manifest.jsonl ppocr-small 8
```

Le split mesuré est `dev`. Le jeu `test` ne s'ouvre qu'au moment de produire une mesure destinée au rapport.

## Ce qu'un run produit

Un dossier `run/<date>_<moteur>_<réglages>/` contenant quatre fichiers :

| Fichier | Contenu |
|---|---|
| `run.json` | conditions du run : moteur, version, paramètres, empreinte du manifest, machine |
| `resultats.jsonl` | une ligne par document, au format de `docs/protocole-evaluation.md` |
| `score.md` | scores par type, moyennes macro, coût |
| `textes.json` | texte attendu et texte extrait de chaque document, découpés en lignes pour la relecture |

## Mesurer le coût

`score.md` publie le temps écoulé, le temps processeur et le pic mémoire.

Le pic mémoire est **échantillonné** pendant chaque extraction, toutes les 25 ms, et non lu dans `Process.PeakWorkingSet64` : ce compteur est le pic du processus depuis son démarrage et ne redescend jamais, il ferait hériter chaque document du pic de tous les précédents. Chaque ligne porte aussi la mémoire relevée avant l'extraction, dont l'écart au pic donne le coût propre du document, seule part comparable d'un moteur à l'autre.

Deux conséquences pour qui modifie le code : rien d'autre que l'extraction ne doit se produire dans la fenêtre chronométrée, et le processus ne doit rien accumuler d'un document au suivant.

Les temps ne sont pas comparables d'un run à l'autre, ±40 % de bruit selon la charge machine ont déjà été observés sur un travail identique. Moyenner sur trois runs avant de publier un chiffre. Les scores, eux, sont déterministes, et le pic mémoire se reproduit à quelques mégaoctets près.

## Structure

| Dossier | Rôle |
|---|---|
| `Engines/` | un moteur par famille, derrière `ITextExtractionEngine`. `RenduPdf` rend les pages en PNG à 300 DPI pour tous les moteurs qui travaillent sur des pixels |
| `Preprocessing/` | détourage, redressement par quart de tour, deskew. Propre à Tesseract |
| `Manifest/` | lecture de `corpus/manifest.jsonl` |
| `Scoring/` | normalisation du texte, Levenshtein, Jaccard |
| `Benchmark/` | exécution d'un run, mesure des ressources, écriture des résultats |

Un moteur expose son nom, sa version, ses paramètres et une méthode d'extraction qui rend le texte, une confiance quand il en a une, et le fait qu'il ait dû passer par un chemin de rattrapage. Le benchmark ne connaît rien de l'OCR : toute stratégie de repli appartient au moteur.
