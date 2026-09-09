# PiiMaskingComparison

Compare des moteurs de détection de PII sur le corpus de `Detect-Doc`, work item 115217. Un run porte un seul moteur et produit sa couverture, ses erreurs et son coût.

L'entrée mesurée est le **texte de référence du manifest**, pas une sortie d'OCR. La dégradation due à l'OCR est un facteur distinct, à mesurer séparément une fois la qualité du détecteur établie sur du texte propre.

## Lancer un run

```powershell
dotnet run -c Release -- <chemin-du-manifest> [moteur]
```

Moteur disponible : `regex` (défaut).

```powershell
dotnet run -c Release -- ..\..\..\corpus\manifest.jsonl regex
```

Le split mesuré est `dev`. Le jeu `test` ne s'ouvre qu'au moment de produire une mesure destinée au rapport.

## Ce qu'un run produit

Un dossier `run/<date>_<moteur>/` contenant trois fichiers :

| Fichier | Contenu |
|---|---|
| `run.json` | conditions du run : moteur, version, paramètres, empreinte du manifest, machine |
| `resultats.jsonl` | une ligne par document : occurrences attendues et leur couverture, faux positifs, coût |
| `recap.md` | couverture par catégorie, chiffres globaux, coût |

## Comment la vérité terrain est construite

Le manifest déclare les valeurs synthétiques injectées dans chaque document, sous `pii_injectees`. Le benchmark les cherche dans le texte de référence et retient **une occurrence par emplacement trouvé** : c'est par occurrence que se compte une fuite, une valeur répétée pouvant être masquée à un endroit et pas à l'autre.

La recherche ignore les blancs des deux côtés. Le manifest porte l'IBAN d'un seul tenant quand le document l'imprime par groupes de quatre ; sans cette tolérance, la catégorie la plus structurée du WI ne serait pas mesurable. La casse, elle, est respectée.

Une occurrence n'est **couverte** que si toute sa plage est détectée. Une valeur à moitié couverte laisse de quoi identifier la personne, elle compte comme manquée.

## Limites de cette mesure

- **Les valeurs non localisées ne sont pas comptées**, ni en couvertes ni en manquées : le texte ne les porte pas sous la forme déclarée. C'est le cas des noms imprimés en capitales alors que le manifest les écrit en casse mixte. Leur volume, publié dans `recap.md`, est la marge d'incertitude du taux de couverture.
- **La catégorie se déduit du nom de la clé** du manifest. Les clés qui ne contiennent ni `email`, ni `iban`, ni `telephone` tombent en `Autre` : leurs occurrences restent attendues, donc visibles en manquées, sans qu'aucune règle ne les vise encore.
- **Le corpus déclare ce qu'il a injecté, pas tout ce qu'il contient.** Une adresse de courriel figée dans un gabarit est une détection correcte comptée en faux positif, faute d'être déclarée. Relire les faux positifs du `resultats.jsonl` avant de conclure sur la précision d'un moteur.
- Les durées ne sont pas comparables d'un run à l'autre sans moyenner plusieurs exécutions. La couverture, elle, est déterministe.

## Structure

| Dossier | Rôle |
|---|---|
| `Engines/` | un moteur par famille, derrière `IPiiDetectionEngine`. Un moteur rend des plages, il ne masque rien |
| `Manifest/` | lecture de `corpus/manifest.jsonl` |
| `VeriteTerrain/` | localisation des valeurs injectées dans le texte de référence |
| `Benchmark/` | exécution d'un run, mesure du coût, écriture des résultats |

Un moteur expose son nom, sa version, ses paramètres et une méthode qui rend les plages sensibles d'un texte. Le benchmark ne connaît ni les règles ni les modèles : la stratégie de détection appartient au moteur, la confrontation à la vérité terrain appartient au benchmark.
