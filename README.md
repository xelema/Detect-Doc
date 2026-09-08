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
| `POST /api/dev/extract/{engine}` | Texte extrait page par page | Development |
| `POST /api/dev/classify` | Classification d'un texte par Flowise | Development |
| `POST /api/documents/classify` | Extraction puis classification d'un PDF | Tous les environnements |
| [/openapi/v1.json](http://localhost:5080/openapi/v1.json) | Description OpenAPI des routes | Development |
| [/swagger](http://localhost:5080/swagger) | Interface interactive des routes | Development |

`/health` indique que l'application répond. En Development, Swagger permet d'envoyer un PDF à la route d'extraction avec `pdfpig` ou `ppocr-tiny`. Le démarrage fonctionne sans secret ni service externe. Arrêter le serveur avec `Ctrl+C`.

PP-OCRv6 tiny demande ses modèles sous `models/v6/`. Pour utiliser un autre répertoire en développement, définir `PpOcr__ModelsDirectory` sur le dossier qui contient `v6/`, puis lancer l'API. Le modèle d'orientation fourni par RapidOcrNet reste copié à côté du binaire. `PpOcr__ReconnaissancesParalleles` vaut `1` par défaut et règle le nombre de lignes reconnues simultanément.

La route `POST /api/dev/classify` attend `{"texte":"...","types":["RIB","FEUILLE_EMAR"]}`. Elle appelle le flow configuré par `Flowise__BaseUrl` et `Flowise__FlowId`, sans authentification dans cet essai. Par exemple :

```powershell
$env:Flowise__BaseUrl = "https://votre-hote-flowise"
$env:Flowise__FlowId = "votre-flow-id"
```

`POST /api/documents/classify` est un formulaire multipart avec `file`, `types` et `engine`. `types` contient un tableau JSON, par exemple `["RIB","FEUILLE_EMAR"]`, et `engine` vaut `pdfpig` ou `ppocr-tiny`.
La réponse porte aussi `extractionMs`, `classificationMs`, `totalMs` et l’`executionId` retourné par Flowise lorsqu’il est disponible.

`src/DetectDoc.Api/Program.cs` assemble l'application et expose les routes avec les Minimal APIs d'ASP.NET Core. `tests/DetectDoc.Tests` démarre un serveur en mémoire et vérifie les réponses HTTP en Development et en Production.

## Conteneur Docker

Le `Dockerfile` à la racine utilise le SDK Linux .NET 10.0.302 pour construire l'application et le runtime Linux .NET 10.0.10 pour l'exécuter. La phase de build exécute les tests, puis l'image finale ne contient que l'API et s'exécute avec un utilisateur non privilégié.

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
