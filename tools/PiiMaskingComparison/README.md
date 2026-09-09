# PiiMaskingComparison

Compare des moteurs de détection de PII sur le corpus de `Detect-Doc`, work item 115217. Un run porte un seul moteur et produit sa couverture, ses erreurs et son coût.

L'entrée mesurée est le **texte de référence du manifest**, pas une sortie d'OCR. La dégradation due à l'OCR est un facteur distinct, à mesurer séparément une fois la qualité du détecteur établie sur du texte propre.

## Lancer un run

Le conteneur est la façon de référence de produire un run : tous les moteurs y tourneront dans le même environnement, le prochain en amenant un runtime et un modèle qu'il vaut mieux ne pas installer sur le poste. Depuis ce dossier :

```powershell
docker build --tag detectdoc-pii-comparison .
docker run --rm --volume "$((Resolve-Path ..\..\corpus).Path):/corpus:ro" --volume "${PWD}\PiiMaskingComparison\run:/work/run" detectdoc-pii-comparison /corpus/manifest.jsonl regex
```

Le corpus est monté en lecture seule, `run/` en écriture : les résultats atterrissent dans le même dossier que les runs lancés en local. Le moteur est le dernier argument. Le `Resolve-Path` donne au démon le chemin absolu qu'il attend, un `..` dans une source de montage ne passant pas partout.

Pour mettre au point une règle, `dotnet run` depuis `PiiMaskingComparison/` reste plus rapide :

```powershell
dotnet run -c Release -- ..\..\..\corpus\manifest.jsonl regex
```

Les deux chemins exécutent le même code et donnent la même couverture, qui est déterministe. **Les mesures de coût, elles, ne se comparent pas entre l'hôte et le conteneur** : un chiffre publié vient d'un run conteneurisé.

Moteur disponible : `regex` (défaut). Le split mesuré est `dev` ; le jeu `test` ne s'ouvre qu'au moment de produire une mesure destinée au rapport.

## Ce qu'un run produit

Un dossier `run/<date>_<moteur>/` contenant trois fichiers :

| Fichier | Contenu |
|---|---|
| `run.json` | conditions du run : moteur, version, paramètres, empreinte du manifest, machine |
| `resultats.jsonl` | une ligne par document : occurrences attendues et leur couverture, faux positifs, coût |
| `recap.md` | couverture par catégorie, détail par règle, chiffres globaux, coût |

Le récapitulatif liste **toutes** les catégories, même celles qu'aucune valeur du split n'alimente et celles qu'aucune règle ne vise : un zéro affiché est une information, une ligne absente se lit comme un oubli. Une clé du manifest que le tool ne sait pas classer apparaît en `Inconnue` et demande une reprise du mapping.

| Catégorie | Clés du manifest |
|---|---|
| `Nom` | `nom_complet`, `nom_formateur`, `noms`, `noms_participants`, `contact_client`, `contact_nom_complet`, `contact_prenom`, `titulaire` |
| `Organisme` | `organisme`, `raison_sociale_emetteur`, `designation_client` |
| `Email` | `email`, `email_emetteur` |
| `Telephone` | `telephone_mobile`, `telephone_fixe`, `telephone_emetteur` |
| `Adresse` | `adresse`, `adresse_titulaire` |
| `Iban` | `iban` |
| `Bic` | `bic` |
| `Siret` | `siret_emetteur` |
| `IdentifiantProfessionnel` | `rpps`, `adeli` |
| `NumeroDocument` | `rum` |
| `ReseauSocial` | `reseau_social` |

## Comment la vérité terrain est construite

Le manifest déclare les valeurs synthétiques injectées dans chaque document, sous `pii_injectees`. Le benchmark les cherche dans le texte de référence et retient **une occurrence par emplacement trouvé** : c'est par occurrence que se compte une fuite, une valeur répétée pouvant être masquée à un endroit et pas à l'autre.

La comparaison porte sur les seuls caractères signifiants, casse ignorée : le manifest porte l'IBAN d'un seul tenant quand le document l'imprime par groupes de quatre, l'adresse `12 rue X, 75000 Ville` s'imprime sur deux lignes sans virgule, et une carte de visite écrit le nom de famille en capitales. Ces écarts sont de mise en page, pas de contenu. Une suite de chiffres trouvée au milieu d'une suite plus longue est écartée : le numéro de déclaration d'activité d'un organisme contient par hasard un ADELI une fois les espaces retirés.

Une occurrence n'est **couverte** que si toute sa plage est détectée. Une valeur à moitié couverte laisse de quoi identifier la personne, elle compte comme manquée.

## Limites de cette mesure

- **Les valeurs non localisées ne sont pas comptées**, ni en couvertes ni en manquées : le texte ne les porte pas sous la forme déclarée. C'est le cas des noms imprimés en capitales alors que le manifest les écrit en casse mixte. Leur volume, publié dans `recap.md`, est la marge d'incertitude du taux de couverture.
- **La catégorie se déduit du nom de la clé** du manifest. Les clés qui ne contiennent ni `email`, ni `iban`, ni `telephone` tombent en `Autre` : leurs occurrences restent attendues, donc visibles en manquées, sans qu'aucune règle ne les vise encore.
- **Le corpus déclare ce qu'il a injecté, pas tout ce qu'il contient.** Une adresse de courriel figée dans un gabarit est une détection correcte comptée en faux positif, faute d'être déclarée. Relire les faux positifs du `resultats.jsonl` avant de conclure sur la précision d'un moteur.
- **Les noms sans libellé ne sont pas couverts.** La baseline ne reconnaît un nom que s'il suit un libellé du document ou une civilité. Un nom en cellule de tableau d'émargement ou en en-tête de carte de visite n'a aucune forme distinctive : c'est la charge du détecteur suivant, pas d'une règle de plus.
- **Les listes de mots-clés ne généralisent pas.** Les règles qui reconnaissent une raison sociale à `Institut`, `Industrie` ou `Conseil` attrapent ce que le générateur produit, pas ce qu'écrit un client.
- Les durées ne sont pas comparables d'un run à l'autre sans moyenner plusieurs exécutions. La couverture, elle, est déterministe.

## Structure

| Dossier | Rôle |
|---|---|
| `Engines/` | un moteur par famille, derrière `IPiiDetectionEngine`. Un moteur rend des plages, il ne masque rien |
| `Manifest/` | lecture de `corpus/manifest.jsonl` |
| `VeriteTerrain/` | localisation des valeurs injectées dans le texte de référence |
| `Benchmark/` | exécution d'un run, mesure du coût, écriture des résultats |

Les règles du moteur regex sont de deux familles. Les unes reconnaissent une forme qui se suffit à elle-même, un IBAN ou une adresse de courriel. Les autres partent d'un libellé du document, `BIC`, `RPPS`, `Formateur`, et ne retiennent que la valeur qui suit, capturée dans le groupe 1 : sans ce libellé, un BIC est huit majuscules et un ADELI neuf chiffres, deux formes trop banales pour être masquées à l'aveugle.

Un moteur expose son nom, sa version, ses paramètres et une méthode qui rend les plages sensibles d'un texte. Le benchmark ne connaît ni les règles ni les modèles : la stratégie de détection appartient au moteur, la confrontation à la vérité terrain appartient au benchmark.
