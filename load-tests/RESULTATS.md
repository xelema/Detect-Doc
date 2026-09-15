# Résultats des tests de charge (K6)

Convention reprise de `Valia-Stack/documentation/load-testing/` : un fichier daté par campagne, tableau comparatif par palier, section d'interprétation.

## Index

- [2026-09-15 - detect-doc - classify.js (local)](2026-09-15_detectdoc_classify_local.md)

## Convention (à suivre pour les prochains runs)

Pour chaque campagne de tests :

- Créer un fichier daté `YYYY-MM-DD_<cible>_<script>.md` dans ce dossier.
- Renseigner : contexte (env, ressources), commande de lancement, version/commit, paramètres (VUs/iterations/maxDuration), timeouts, seuils (thresholds).
- Coller les métriques clés sous forme de tableau : `errors`, `http_req_failed`, `http_req_duration p(95)`, `avg`, `req/s`, `checks_succeeded`, plus RAM/CPU max relevés dans `results/stats-<RUN_ID>.csv`.
- Garder une note "interprétation" (seuils OK/KO + cause probable).
