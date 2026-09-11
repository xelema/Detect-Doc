# Detect-Doc

POC de détection de type documentaire, service qui reçoit un document et une liste de types propre à un client, puis renvoie le type le plus probable et un niveau de confiance, y compris quand ce score est faible. Objectif : réduire l'effort de qualification documentaire au dépôt en GED. 

## Vue d'ensemble

La chaîne cible est : OCR du document, masquage des PII, puis classification par Mistral qui renvoie le type et son score. Chaque brique s'évalue sur le même corpus de test.

Le projet contient le socle HTTP de l'API, le générateur de corpus et le CLI de comparaison OCR.

## API

Prérequis : SDK .NET 10.0.302, épinglé dans `global.json` (les patches de la même série sont acceptés). Depuis le dossier `Detect-Doc` :

```bash
dotnet restore DetectDoc.slnx
dotnet build DetectDoc.slnx --no-restore
dotnet test DetectDoc.slnx --no-build
dotnet run --project src/DetectDoc.Api -- --environment Development --urls http://localhost:5080
```

| Route | Réponse | Disponibilité |
|---|---|---|
| [/health](http://localhost:5080/health) | HTTP 200, `{"statut":"ok"}` | Tous les environnements |
| `POST /api/dev/pii/detect` | Plages de PII détectées par les règles et Presidio | Development |
| `POST /api/dev/pii/mask` | Texte avec les PII remplacées par des marqueurs | Development |
| `POST /api/dev/extract/{engine}` | Texte extrait page par page | Development |
| `POST /api/dev/classify` | Classification d'un texte par Flowise | Development |
| `POST /api/documents/classify` | Extraction puis classification d'un PDF | Tous les environnements |
| [/openapi/v1.json](http://localhost:5080/openapi/v1.json) | Description OpenAPI des routes | Development |
| [/swagger](http://localhost:5080/swagger) | Interface interactive des routes | Development |

`/health` indique que l'application répond. En Development, Swagger permet d'envoyer un PDF à la route d'extraction avec `pdfpig` ou `ppocr-tiny`. Le démarrage fonctionne sans secret ni service externe. Arrêter le serveur avec `Ctrl+C`.

Avant le premier lancement local, exécuter `./Get-Modeles.ps1` depuis la racine du dépôt. Le script installe les modèles dans `models/`, vérifie leurs empreintes SHA-256, puis le build copie PP-OCRv6 tiny vers le dossier de sortie de l’API. Pour utiliser un autre répertoire en développement, définir `PpOcr__ModelsDirectory` sur le dossier qui contient `v6/`, puis lancer l'API. Le modèle d'orientation fourni par RapidOcrNet reste copié à côté du binaire. `PpOcr__ReconnaissancesParalleles` vaut `1` par défaut et règle le nombre de lignes reconnues simultanément.

Les routes `POST /api/dev/pii/detect` et `POST /api/dev/pii/mask` attendent `{"text":"..."}`. La première retourne les plages détectées, leur catégorie, leur règle et leur valeur. La seconde retourne `{"text":"..."}` avec les PII remplacées par les marqueurs qui seront envoyés à Flowise. Elles utilisent les règles du comparatif avec Presidio et `fr_core_news_md`. Installer son environnement Python local, puis indiquer son exécutable avant de démarrer l'API :

```powershell
python -m venv "$env:TEMP\detect-doc-pii"
& "$env:TEMP\detect-doc-pii\Scripts\python.exe" -m pip install -r src/DetectDoc.Infrastructure/Pii/Ner/requirements.txt
$env:PII_PYTHON = "$env:TEMP\detect-doc-pii\Scripts\python.exe"
```

La route `POST /api/dev/classify` attend `{"texte":"...","types":["RIB","FEUILLE_EMAR"]}`. Elle appelle le flow configuré par `Flowise__BaseUrl` et `Flowise__FlowId`, sans authentification dans cet essai. Par exemple :

```powershell
$env:Flowise__BaseUrl = "https://votre-hote-flowise"
$env:Flowise__FlowId = "votre-flow-id"
```

Pour un test local contre un certificat HTTPS interne non reconnu, définir aussi `Flowise__AllowInvalidTls=true` et lancer l’API en Development. Cette option est refusée dans les autres environnements et reste désactivée par défaut.

`POST /api/documents/classify` est un formulaire multipart avec `file`, `types` et `engine`. `types` accepte un tableau JSON, par exemple `["RIB","FEUILLE_EMAR"]`, ou la liste séparée par des virgules envoyée par Swagger, par exemple `RIB,FEUILLE_EMAR`. `engine` est optionnel et vaut `auto` par défaut : l’API extrait une fois avec PdfPig, puis utilise PP-OCR tiny si au moins une page ne contient pas de texte natif. `pdfpig` et `ppocr-tiny` conservent un choix explicite pour un essai reproductible.
La réponse porte aussi le moteur effectivement utilisé, `extractionMs`, `classificationMs`, `totalMs` et l’`executionId` retourné par Flowise lorsqu’il est disponible. Le champ `moteur` emploie les identifiants de `engine`, `pdfpig` ou `ppocr-tiny`, y compris après un choix `auto` : la valeur reçue peut être renvoyée telle quelle pour rejouer l’extraction avec un moteur explicite.

`src/DetectDoc.Api/Program.cs` assemble l'application ; `ServiceCollectionExtensions.cs` enregistre les services et `Endpoints/` porte les routes, écrites avec les Minimal APIs d'ASP.NET Core. `tests/DetectDoc.Tests` démarre un serveur en mémoire et vérifie les réponses HTTP en Development et en Production.

## Conteneur Docker

Le `Dockerfile` à la racine utilise le SDK Linux .NET 10.0.302 pour construire l'application et le runtime Linux .NET 10.0.10 pour l'exécuter. La phase de build exécute les tests, télécharge les trois fichiers du modèle PP-OCRv6 tiny et vérifie leur empreinte SHA-256. L'image finale contient aussi Python, Presidio et `fr_core_news_md`, puis s'exécute avec un utilisateur non privilégié.

```powershell
docker build --tag detect-doc:local .
docker run --rm --publish 8080:8080 detect-doc:local
Invoke-RestMethod http://localhost:8080/health
```

La dernière commande retourne `statut : ok`. Arrêter le conteneur avec `Ctrl+C`.

## Arborescence

```
Detect-Doc/
├── Dockerfile               image Linux de l'API
├── .dockerignore            contexte de build minimal
├── DetectDoc.slnx          solution de l'API et de ses tests
├── global.json            version du SDK .NET
├── src/
│   ├── DetectDoc.Api/      démarrage et routes HTTP
│   ├── DetectDoc.Core/     contrat d'extraction
│   └── DetectDoc.Infrastructure/ extraction PdfPig
├── tests/
│   └── DetectDoc.Tests/    tests HTTP avec serveur en mémoire
├── corpus/
│   ├── manifest.jsonl      livrable versionné : une ligne JSON par document, sa vérité terrain
│   └── raw/                documents produits par le générateur, non versionnés
├── docs/
│   ├── types-documentaires.md     une fiche par type documentaire ciblé
│   ├── corpus.md                  source, licence, traçabilité, sensibilité du corpus
│   ├── protocole-evaluation.md    métriques et conditions d'exécution de l'évaluation
│   └── modele-rapport.md          template du rapport final, avec des chiffres fictifs
└── tools/
    ├── CorpusGenerator/    génération du corpus et de son manifest à partir d'une seed
    └── OcrComparison/      CLI de comparaison des moteurs d'extraction
```

`corpus/raw/` n'est pas versionné : le manifest et la seed de génération suffisent à refabriquer les mêmes documents, octet pour octet. Seul `corpus/manifest.jsonl` est le livrable.

## `docs/`

- **`types-documentaires.md`** : une fiche par type documentaire du périmètre, définition, marqueurs, règles de désambiguïsation face aux types voisins, observations tirées des exemples réels dépouillés quand il en existe, et spécification de génération. Pilote l'écriture de chaque type dans `tools/CorpusGenerator/documents/`.
- **`corpus.md`** : source, licence, traçabilité et sensibilité du corpus, avec sa composition actuelle.
- **`protocole-evaluation.md`** : métriques retenues, taille d'échantillon, conditions d'exécution à figer pour comparer deux mesures.
- **`modele-rapport.md`** : template du rapport final, avec des chiffres fictifs qui illustrent le format.

## `tools/CorpusGenerator`

Fabrique les PDF du corpus et le manifest qui les décrit. Il tourne sous **Docker**.

```bash
# depuis tools/CorpusGenerator
docker compose build
docker compose run --rm generator
```

Prérequis système, liste des polices, détail de la configuration TOML, schémas d'architecture et méthode pour ajouter un type documentaire : voir `tools/CorpusGenerator/README.md`.
