# 2026-09-15 - Tests de charge K6 - detect-doc - classify.js

## Contexte

- Date : 2026-09-15
- Environnement : local (conteneur `detect-doc:local`, K6 en conteneur `grafana/k6`)
- Script : `load-tests/classify.js`
- Cible : `POST http://host.docker.internal:8080/api/documents/classify` (`engine=auto`, six types décrits, documents aléatoires `split=dev` du corpus, 67 documents)
- Exécuteur : `shared-iterations` (1 itération = 1 requête multipart)
- Matrice exécutée : `vus = N`, `iterations = 5 x N`
- Timeout côté requête : `120s`
- Durée maximale de scénario : `15m`
- Version déployée : branche `feature/115219-loadtests`, commit `2192869`
- Ressources de l'hôte : 15,4 Gio visibles par `docker stats`
- Commande de lancement : voir `load-tests/README.md`
- Seuils K6 (thresholds) :
  - `errors: rate < 0.1`
  - `http_req_failed: rate < 0.1`
- Checks applicatifs :
  - `status is 200`
  - `response is json`

## Résumé comparatif

| VUs | Thresholds | errors | http_req_failed | http_req_duration p(95) | http_req_duration avg | req/s | checks_succeeded | RAM max | CPU max | Notes |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | OK | 0.00% (0/5) | 0.00% (0/5) | 7.18s | 3.53s | 0.220 | 100.00% (10/10) | ~390 Mio | ~100% | RAS |
| 2 | OK | 0.00% (0/10) | 0.00% (0/10) | 36.79s | 14.03s | 0.119 | 100.00% (20/20) | ~858 Mio | ~135% | latence x4 vs 1 VU |
| 5 | OK | 0.00% (0/25) | 0.00% (0/25) | 29.44s | 11.45s | 0.301 | 100.00% (50/50) | ~1.9 Gio | ~127% | max 1m2s |
| 10 | OK | 0.00% (0/50) | 0.00% (0/50) | 1m2s | 16.22s | 0.458 | 100.00% (100/100) | ~2.2 Gio | ~205% | max 1m36s, p90 51s |
| 15 | OK | 6.66% (5/75) | 6.66% (5/75) | 1m59s | 28.9s | 0.391 | 93.33% (140/150) | ~2.9 Gio | ~125% | 5 réponses non-200 ; p95 au bord du timeout 120s |
| 20 | OK | 8.00% (8/100) | 8.00% (8/100) | 1m59s | 34.52s | 0.437 | 92.00% (184/200) | ~3.1 Gio | ~169% | 8 réponses non-200 ; p95 au bord du timeout 120s |

## Détails (sorties K6)

Fichiers `results/k6-vus<N>.summary.txt` et `results/stats-vus<N>.csv`.

### Local - 1 VU

- Thresholds : OK. `http_req_duration` avg=3.53s, p(95)=7.18s. 0 erreur. RAM max ~390 Mio, pic CPU ~100% pendant l'OCR.

### Local - 2 VUs

- Thresholds : OK. avg=14.03s, p(95)=36.79s. Débit 0.119 req/s, inférieur à 1 VU : les requêtes se sont recouvertes et l'OCR sérialise.

### Local - 5 VUs

- Thresholds : OK. avg=11.45s, p(95)=29.44s, max 1m2s. Débit 0.301 req/s.

### Local - 10 VUs

- Thresholds : OK. avg=16.22s, p(95)=1m2s, max 1m36s. Débit 0.458 req/s, meilleur de la campagne.

### Local - 15 VUs

- Thresholds : OK de justesse. `errors` et `http_req_failed` a 6.66% (< 10%).
- avg=28.9s, p(95)=1m59s, max 1m59s. Débit 0.391 req/s, en recul vs 10 VUs.
- 5 réponses non-200 sur 75. RAM max ~2.9 Gio.

### Local - 20 VUs

- Thresholds : OK de justesse.
  - `errors` 8.00% (< 10%).
  - `http_req_failed` 8.00% (< 10%).
- avg=34.52s, p(95)=1m59s, max 1m59s : le p95 touche le timeout requête de 120s. Débit 0.437 req/s, en recul vs 10 VUs.
- RAM max ~3.1 Gio.
- **Nature des non-200 (run instrumenté, `Invoke-LoadRun.ps1`)** : tous les échecs sont des **499** dans les logs du conteneur, requêtes abandonnées par le client au timeout de 120s pendant que le serveur traitait encore. Aucune erreur serveur. Durées par étape sur les réponses 200 : extraction avg 20.6s / max 1m52s, PII avg 0.36s, classification Flowise avg 2.7s / max 12s, zéro échec d'appel Flowise. Goulot : l'extraction OCR, qui fait la queue sous contention CPU.

## Interprétation rapide

- Le dernier palier sans erreur est 10 VUs. Dès 15 VUs apparaissent des réponses non-200 (6.66%) et le p95 atteint le timeout de 120s ; à 20 VUs, 8 % d'erreurs. La dégradation visible côté client commence donc entre 10 et 15 documents en parallèle.
- Les non-200 sont tous des **499 (timeout client de 120s)**, pas des erreurs du service : sous contention, l'extraction OCR fait la queue (avg 20.6s, max 1m52s au palier 20) tandis que PII (avg 0.36s) et Flowise/Mistral (avg 2.7s, zéro échec) restent sains. Le goulot est l'extraction, CPU-bound.
- Le débit plafonne autour de 0.44-0.46 req/s à 10 VUs et ne progresse plus ensuite (0.39 a 15, 0.44 a 20).
- La RAM croît avec la concurrence (~390 Mio a 1 VU, ~2.2 Gio a 10, ~3.1 Gio a 20 VUs, soit environ 140 Mio par document en vol au-delà du socle).
- Sans volumétrie cible, le seuil retenu est : stable jusqu'à ~10 documents en parallèle, requêtes individuelles dépassant le timeout client dès 15.
