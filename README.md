# Detect-Doc

POC de détection de type documentaire : le service reçoit un document PDF et une liste de types propre à l'appelant, puis renvoie le type le plus probable et un niveau de confiance, y compris quand ce score est faible. La chaîne est : extraction du texte, masquage des PII, classification par Mistral via Flowise. Objectif : réduire l'effort de qualification documentaire au dépôt en GED, puis réintégrer le service dans ValIAPI.

## Démarrer l'API

Prérequis : SDK .NET 10 épinglé dans `global.json`, modèles OCR installés par `Get-Modeles.ps1`. Copier la configuration puis renseigner `Flowise__FlowId`. Depuis la racine du dépôt :

```powershell
.\Get-Modeles.ps1
cp .env.exemple .env
dotnet restore DetectDoc.slnx
dotnet build DetectDoc.slnx --no-restore
dotnet test DetectDoc.slnx --no-build --filter "Category!=Corpus"
$env:Flowise__BaseUrl = "https://valia-flowise.val.local"
$env:Flowise__FlowId = "<flow-id>"
dotnet run --project src/DetectDoc.Api -- --environment Development --urls http://localhost:5080
```

`dotnet run` ne lit pas le `.env` : les deux variables se posent dans la session avant de lancer.

En conteneur, l'image embarque modèles OCR et environnement Python Presidio, et le `.env` se passe tel quel :

```powershell
docker build --tag detect-doc:local .
docker run --rm --publish 8080:8080 --env-file .env detect-doc:local
# ou
docker compose --env-file .env up -d --build

Invoke-RestMethod http://localhost:8080/health
```

La dernière commande retourne `statut : ok`. Arrêter avec `Ctrl+C` ou `docker compose down`.

## Routes

| Route | Réponse | Disponibilité |
|---|---|---|
| [/health](http://localhost:5080/health) | HTTP 200, `{"statut":"ok"}` | Tous les environnements |
| `POST /api/documents/classify` | Extraction puis classification d'un PDF | Tous les environnements |
| `POST /api/documents/classify-batch` | Extraction puis classification d'un lot de PDF | Tous les environnements |
| `POST /api/dev/extract/{engine}` | Texte extrait page par page | Development |
| `POST /api/dev/pii/detect` | Plages de PII détectées par les règles et Presidio | Development |
| `POST /api/dev/pii/mask` | Texte avec les PII remplacées par des marqueurs | Development |
| `POST /api/dev/classify` | Classification d'un texte par Flowise | Development |
| [/openapi/v1.json](http://localhost:5080/openapi/v1.json) | Description OpenAPI des routes | Development |
| [/swagger](http://localhost:5080/swagger) | Interface interactive des routes | Development |

Le démarrage fonctionne sans secret ni service externe : la classification répond 503 tant que Flowise n'est pas configuré.

## Documentation

| Document | Contenu |
|---|---|
| [docs/integration.md](docs/integration.md) | consommer l'API depuis un autre service : contrats, erreurs, exemples curl et C#, conteneur |
| [docs/development.md](docs/development.md) | runbook du dev sur le POC : prérequis, réglages, routes de diagnostic, commandes de tous les outils |
| [CONTEXT.md](CONTEXT.md) | glossaire du domaine |
| [docs/flowise-classification.md](docs/flowise-classification.md) | configuration du flow et system prompt de classification |
| [docs/types-documentaires.md](docs/types-documentaires.md) | une fiche par type documentaire du périmètre |
| [docs/corpus.md](docs/corpus.md) | source, licence, traçabilité et sensibilité du corpus |
| [docs/protocole-evaluation.md](docs/protocole-evaluation.md) | métriques et conditions d'exécution de l'évaluation |
| [docs/modele-rapport.md](docs/modele-rapport.md) | template du rapport final |

## Arborescence

```
Detect-Doc/
├── Dockerfile               image Linux de l'API : build, tests, modèles OCR, Presidio
├── compose.yaml             lancement conteneur à partir du .env
├── .env.exemple             configuration à copier en .env, non versionné
├── DetectDoc.slnx           solution de l'API et de ses tests
├── global.json              version du SDK .NET
├── Get-Modeles.ps1          télécharge et vérifie les modèles OCR
├── CONTEXT.md               glossaire du domaine
├── src/
│   ├── DetectDoc.Api/       démarrage, routes HTTP, contrats
│   ├── DetectDoc.Core/      use case de classification, interfaces des adapteurs
│   └── DetectDoc.Infrastructure/  adapteurs : PdfPig, PP-OCR, Presidio, Flowise
├── tests/
│   └── DetectDoc.Tests/     tests unitaires et HTTP, serveur en mémoire
├── corpus/
│   ├── manifest.jsonl       livrable versionné : une ligne JSON par document, sa vérité terrain
│   └── raw/                 documents produits par le générateur, non versionnés
├── docs/                    guides et documents de méthode, cf. table ci-dessus
└── tools/
    ├── CorpusGenerator/          génération du corpus et de son manifest à partir d'une seed
    ├── OcrComparison/            CLI de comparaison des moteurs d'extraction
    ├── PiiMaskingComparison/     comparatif des détections PII et son rapport
    └── ClassificationEvaluation/ runner d'évaluation de la classification sur le corpus
```

`corpus/raw/` n'est pas versionné : le manifest et la seed de génération suffisent à refabriquer les mêmes documents, octet pour octet. Chaque tool a sa propre solution et son propre README ; `DetectDoc.slnx` ne couvre que l'API et ses tests.
