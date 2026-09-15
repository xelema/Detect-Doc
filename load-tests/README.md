# Tests de charge K6 (WI 115219)

Charge progressive sur `POST /api/documents/classify` avec des documents aléatoires du corpus (`split=dev`, `engine=auto` qui enchaîne PdfPig puis PP-OCR tiny). La chaîne complète est mesurée : extraction, masquage PII, appel Flowise réel.

Convention reprise de Valia-Stack (`documentation/load-testing/K6.md`) : un run par palier de concurrence, synthèse texte par run, pas d'InfluxDB.

## Prérequis

- L'API tourne en conteneur local (image `detect-doc:local`, voir le README racine) avec `Flowise__*` configuré et `Flowise__AllowInvalidTls=true` si l'hôte Flowise utilise le certificat interne (sinon 503 `UntrustedRoot` sur l'étape Flowise).
- Le corpus est généré : `corpus/manifest.jsonl` et `corpus/raw/` présents.
- Docker disponible pour l'image `grafana/k6`, ou un binaire `k6` installé localement.

## Un palier de concurrence

Méthode recommandée, tout-en-un (logs du conteneur, stats, run K6, récapitulatif) :

```powershell
.\Invoke-LoadRun.ps1 -Vus 20          # depuis load-tests/
```

Elle produit dans `results/` : `k6-<RunId>.summary.txt`, `k6-<RunId>.requests.csv` (une ligne par requête : statut, durées par étape, moteur, executionId Flowise, titre d'erreur), `stats-<RunId>.csv`, `logs-<RunId>.txt`, `run-<RunId>.meta.json`. Ajouter `-LocalK6` pour utiliser un binaire `k6` local au lieu de Docker.

Méthode manuelle (sans capture de logs ni de stats) :

```powershell
# Run K6 a 5 VUs, 25 requetes
docker run --rm -i `
    -v "${PWD}:/scripts" `
    -v "${PWD}\..\corpus:/corpus:ro" `
    -v "${PWD}\..\tools\ClassificationEvaluation\descriptions-types.json:/scripts/descriptions-types.json:ro" `
    -e DETECTDOC_BASE_URL=http://host.docker.internal:8080 `
    -e K6_CORPUS_DIR=/corpus `
    -e LOAD_VUS=5 `
    -e K6_RUN_ID=vus5 `
    -e K6_RESULTS_DIR=/scripts/results `
    grafana/k6 run /scripts/classify.js
```

Avec un `k6` local plutôt que Docker : `k6 run classify.js` depuis `load-tests/`, `DETECTDOC_BASE_URL` reste `http://localhost:8080`, `K6_CORPUS_DIR=..\corpus` et `K6_TYPES_PATH=..\tools\ClassificationEvaluation\descriptions-types.json`.

Attention : ne pas préfixer les variables de charge par `K6_` (`K6_VUS`, `K6_ITERATIONS` sont natives de K6 et écrasent le scénario). Seuls `K6_RUN_ID`, `K6_RESULTS_DIR`, `K6_CORPUS_DIR`, `K6_TYPES_PATH` gardent ce préfixe, sans collision connue.

## Progression

Paliers par défaut : 1, 2, 5, 10, 15, 20 VUs. Un run par palier, `LOAD_VUS` et `K6_RUN_ID` adaptés. On s'arrête dès qu'un palier dépasse ~10 % d'erreurs ou un p95 > 120 s : le seuil est atteint, le dernier palier sain est retenu pour le rapport.

Chaque run produit `results/k6-<RUN_ID>.summary.txt` (synthèse K6) et `results/stats-<RUN_ID>.csv` (consommation du conteneur échantillonnée). Le ratio concurrence/RAM se lit palier par palier, selon le template Valia-Stack `documentation/load-testing/TEMPLATE_resultats_k6.md`.

## Variables d'environnement

| Variable | Défaut | Rôle |
|---|---|---|
| `DETECTDOC_BASE_URL` | `http://localhost:8080` | URL de l'API (`host.docker.internal` quand K6 tourne en conteneur) |
| `K6_CORPUS_DIR` | `.` | Dossier contenant `manifest.jsonl` et `raw/` |
| `K6_TYPES_PATH` | `descriptions-types.json` | Liste des six types envoyée dans le champ `types` |
| `LOAD_VUS` | `5` | Concurrence du palier |
| `LOAD_ITERATIONS` | `5 x LOAD_VUS` | Nombre total de requêtes du run |
| `LOAD_MAX_DURATION` | `15m` | Borne haute du run |
| `K6_RUN_ID` | `vus<LOAD_VUS>` | Identifiant commun run/synthèse/stats |
| `K6_RESULTS_DIR` | `.` | Dossier d'export des synthèses |
