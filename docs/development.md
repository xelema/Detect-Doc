# Développement sur Detect-Doc

Runbook du développeur qui travaille sur le POC : prérequis, lancement, réglages et point d'entrée de chaque outil. Consommer l'API depuis un autre service relève de `docs/integration.md`.

## Prérequis

- **SDK .NET 10** : `global.json` épingle la version (10.0.302, patches de la série acceptés).
- **Modèles OCR** : pas dans le dépôt. `Get-Modeles.ps1` à la racine les télécharge dans `models/`, vérifie leurs empreintes SHA-256, et le build copie `models/v6` vers la sortie de l'API. Sans accès à GitHub ou ModelScope : `.\Get-Modeles.ps1 -Source \\serveur\partage\modeles-ocr`.
- **Python pour le masquage PII** : la détection Presidio tourne dans un sous-processus. Créer l'environnement une fois, sous un chemin court (l'arborescence de Presidio dépasse la limite de longueur de chemin Windows sous `%TEMP%`) :

```powershell
python -m venv C:\Temp\detect-doc-pii
C:\Temp\detect-doc-pii\Scripts\python.exe -m pip install -r src\DetectDoc.Infrastructure\Pii\Ner\requirements.txt
$env:PII_PYTHON = "C:\Temp\detect-doc-pii\Scripts\python.exe"
```

- **Flowise** : la classification passe par un flow Mistral. `cp .env.exemple .env` puis renseigner `Flowise__BaseUrl` et `Flowise__FlowId` (le FlowId se récupère auprès de l'équipe, jamais commité). Sans elles, l'API démarre mais la classification répond 503.

## Lancer l'API

```powershell
dotnet restore DetectDoc.slnx
dotnet build DetectDoc.slnx --no-restore
dotnet test DetectDoc.slnx --no-build --filter "Category!=Corpus"
$env:Flowise__BaseUrl = "https://valia-flowise.val.local"
$env:Flowise__FlowId = "<flow-id>"
dotnet run --project src/DetectDoc.Api -- --environment Development --urls http://localhost:5080
```

Le test marqué `Category=Corpus` dépend de `corpus/raw/`, non versionné : il est exclu du filtre. En `Development`, Swagger UI répond sur `/swagger` et sert à envoyer un PDF depuis le navigateur.

En conteneur, l'image embarque modèles et Python : `docker build --tag detect-doc:local .` puis `docker run --rm --publish 8080:8080 --env-file .env detect-doc:local`, ou `docker compose --env-file .env up -d --build`. Détail et variables d'environnement dans `docs/integration.md`.

## Réglages

| Variable | Défaut | Effet |
|---|---|---|
| `PII_PYTHON` | `python3` | exécutable Python du sous-processus Presidio |
| `PII_PYTHON_SCRIPT` | `Pii/detecteur.py` à côté du binaire | script servi par le sous-processus, surtout utile aux tests |
| `PII_PRESIDIO_MODELE` | `fr_core_news_md` | modèle spaCy chargé par `detecteur.py` |
| `PII_PRESIDIO_ENTITES` | toutes | entités Presidio demandées, séparées par des virgules |
| `PII_PRESIDIO_EXCLURE` | composants hors `tok2vec` et `ner` | composants spaCy à ne pas charger |
| `Flowise__BaseUrl`, `Flowise__FlowId` | absents | cible du flow de classification |
| `Flowise__AllowInvalidTls` | `false` | accepte un certificat interne, admis seulement en `Development` et `Recette` |
| `PpOcr__ModelsDirectory` | `models/` à côté du binaire | dossier contenant `v6/` |
| `PpOcr__ReconnaissancesParalleles` | `1` | lignes reconnues en parallèle par PP-OCR |

## Routes de diagnostic

Exposées en `Development` et `Recette` uniquement, pour tester chaque brique sans la chaîne complète :

| Route | Entrée | Sortie |
|---|---|---|
| `POST /api/dev/extract/{engine}` | `file` multipart, `engine` = `pdfpig` ou `ppocr-tiny` | pages `{numeroPage, texte, confianceOcr}` |
| `POST /api/dev/pii/detect` | `{"text":"..."}` | détections `{category, rule, kind, start, length, value}` |
| `POST /api/dev/pii/mask` | `{"text":"..."}` | `{"text":"..."}` avec les marqueurs envoyés à Flowise |
| `POST /api/dev/classify` | `{"text":"...","types":["RIB","SEPA"]}` | `{type, confiance, executionId}` brut du classifieur |

## Où vivent les choses

`src/DetectDoc.Api/Program.cs` assemble l'application, `ServiceCollectionExtensions.cs` enregistre les services, `Endpoints/` porte les routes en Minimal APIs. L'orchestration vit dans `DocumentClassification` (Core) : `ExecuteAsync` enchaîne extraction, masquage PII et classification, et retourne un `DocumentClassificationOutcome` (`Success`, `Rejected`, `Faulted` par étape) que `HttpProblems.ForOutcome` traduit en `ProblemDetails`. Les systèmes externes (PdfPig, ONNX, sous-processus Presidio, HTTP Flowise) sont des adapteurs derrière des interfaces dans `DetectDoc.Infrastructure`. Le glossaire du domaine est dans `CONTEXT.md`.

`tests/DetectDoc.Tests` démarre un serveur en mémoire et vérifie les réponses HTTP en Development et en Production. La couture du détecteur Presidio est pilotée par `PII_PYTHON`, ce qui permet de tester l'absence d'exécutable, un handshake illisible ou un délai dépassé sans Python réel.

## Toutes les commandes

| Cible | Commande | Référence |
|---|---|---|
| API en local | `dotnet run --project src/DetectDoc.Api -- --environment Development --urls http://localhost:5080` | ci-dessus |
| API en conteneur | `docker build --tag detect-doc:local .` puis `docker run --rm --publish 8080:8080 --env-file .env detect-doc:local`, ou `docker compose --env-file .env up -d --build` | `docs/integration.md` |
| Tests | `dotnet test DetectDoc.slnx --filter "Category!=Corpus"` | ci-dessus |
| Modèles OCR | `.\Get-Modeles.ps1` | `tools/OcrComparison/README.md` |
| Générer le corpus | `docker compose build` puis `docker compose run --rm generator`, depuis `tools/CorpusGenerator` ; contrôles : `manifest.py` et `verifier.py` via `--entrypoint python` | `tools/CorpusGenerator/README.md` |
| Comparer les moteurs OCR | `dotnet run -c Release -- <corpus> <manifest> <moteur>`, depuis `tools/OcrComparison` | `tools/OcrComparison/README.md` |
| Comparer les détecteurs PII | `docker build --tag detectdoc-pii-comparison .` puis `docker run --rm --volume <corpus>:/corpus:ro --volume <run>:/work/run detectdoc-pii-comparison /corpus/manifest.jsonl <moteur>`, depuis `tools/PiiMaskingComparison` | `tools/PiiMaskingComparison/README.md` |
| Mesurer la classification | `dotnet run -- --corpus <corpus> --manifest <manifest>`, depuis `tools/ClassificationEvaluation/ClassificationEvaluation`, l'API démarrée et configurée | `tools/ClassificationEvaluation/README.md` |

Chaque tool a sa propre solution et son propre README : `DetectDoc.slnx` ne couvre que l'API et ses tests. Les réglages des prompts et de l'OCR se font sur le split `dev` du corpus ; le split `test` reste scellé pour les mesures du rapport.
