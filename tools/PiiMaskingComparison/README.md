# PiiMaskingComparison

Compare des moteurs de détection de PII sur le corpus de `Detect-Doc`, work item 115217. Un run porte un seul moteur et produit sa couverture, ses erreurs et son coût.

L'entrée mesurée est le **texte de référence du manifest**, pas une sortie d'OCR. La dégradation due à l'OCR est un facteur distinct, à mesurer séparément une fois la qualité du détecteur établie sur du texte propre.

## Lancer un run

Le conteneur est la façon de référence de produire un run : tous les moteurs y tourneront dans le même environnement, le prochain en amenant un runtime et un modèle qu'il vaut mieux ne pas installer sur le poste. Depuis ce dossier :

```powershell
docker build --tag detectdoc-pii-comparison .
docker run --rm --volume "$((Resolve-Path ..\..\corpus).Path):/corpus:ro" --volume "${PWD}\PiiMaskingComparison\run:/work/run" detectdoc-pii-comparison /corpus/manifest.jsonl regex
docker run --rm --volume "$((Resolve-Path ..\..\corpus).Path):/corpus:ro" --volume "${PWD}\PiiMaskingComparison\run:/work/run" detectdoc-pii-comparison /corpus/manifest.jsonl presidio
```

Le corpus est monté en lecture seule, `run/` en écriture : les résultats atterrissent dans le même dossier que les runs lancés en local. Le moteur est le dernier argument. Le `Resolve-Path` donne au démon le chemin absolu qu'il attend, un `..` dans une source de montage ne passant pas partout.

Pour mettre au point une règle, `dotnet run` depuis `PiiMaskingComparison/` reste plus rapide :

```powershell
dotnet run -c Release -- ..\..\..\corpus\manifest.jsonl regex
```

Les deux chemins exécutent le même code et donnent la même couverture, qui est déterministe. **Les mesures de coût, elles, ne se comparent pas entre l'hôte et le conteneur** : un chiffre publié vient d'un run conteneurisé.

Moteurs disponibles : `regex` (défaut) et `presidio`. Le split mesuré est `dev` ; le jeu `test` ne s'ouvre qu'au moment de produire une mesure destinée au rapport.

Le moteur `presidio` a besoin de son environnement Python. L'image le porte ; en local, il faut le créer une fois et dire au tool où le trouver :

```powershell
python -m venv C:\Temp\pii-venv
C:\Temp\pii-venv\Scripts\python.exe -m pip install --requirement PiiMaskingComparison\Presidio\requirements.txt
$env:PII_PYTHON = "C:\Temp\pii-venv\Scripts\python.exe"
```

Le chemin est court à dessein : sous `%TEMP%`, l'arborescence de Presidio dépasse la limite de longueur de chemin de Windows et l'installation échoue en cours de route.

Deux variables règlent ce moteur, en local comme dans le conteneur. Elles sont reportées dans `run.json` et dans le récapitulatif, un run dit donc toujours avec quoi il a été produit.

| Variable | Défaut | Effet |
|---|---|---|
| `PII_PRESIDIO_MODELE` | `fr_core_news_sm` | modèle spaCy chargé ; `fr_core_news_md` doit être installé dans le venv pour être utilisable |
| `PII_PRESIDIO_ENTITES` | toutes | liste d'entités Presidio séparées par des virgules, par exemple `PERSON,EMAIL_ADDRESS,PHONE_NUMBER,IBAN_CODE` |

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

La comparaison ne retient que les lettres et les chiffres, casse ignorée. Tout le reste appartient à la mise en page : le manifest porte l'IBAN d'un seul tenant quand le document l'imprime par groupes de quatre, écrit l'adresse `12 rue X, 75000 Ville` que le document coupe sur deux lignes ou sépare d'un point médian, et une carte de visite met le nom de famille en capitales. Ces écarts ne changent pas la valeur, et un détecteur qui la laisse passer laisse fuiter une PII.

Une valeur trouvée au milieu d'un mot ou d'un nombre plus long est écartée : sans cette garde, le numéro de déclaration d'activité d'un organisme contient un ADELI et deux mots voisins se recollent une fois la ponctuation retirée. Le voisinage se juge sur le texte d'origine, seul endroit où la séparation existe encore.

Une occurrence n'est **couverte** que si toute sa plage est détectée. Une valeur à moitié couverte laisse de quoi identifier la personne, elle compte comme manquée.

## Limites de cette mesure

- **Les valeurs non localisées ne sont pas comptées**, ni en couvertes ni en manquées : le texte ne les porte pas sous la forme déclarée. C'est le cas des noms imprimés en capitales alors que le manifest les écrit en casse mixte. Leur volume, publié dans `recap.md`, est la marge d'incertitude du taux de couverture.
- **La catégorie se déduit du nom de la clé** du manifest. Les clés qui ne contiennent ni `email`, ni `iban`, ni `telephone` tombent en `Autre` : leurs occurrences restent attendues, donc visibles en manquées, sans qu'aucune règle ne les vise encore.
- **Le corpus déclare ce qu'il a injecté, pas tout ce qu'il contient.** Une adresse de courriel figée dans un gabarit est une détection correcte comptée en faux positif, faute d'être déclarée. Relire les faux positifs du `resultats.jsonl` avant de conclure sur la précision d'un moteur.
- **Une plage détectée compte pour la valeur qu'elle recouvre, quelle que soit sa catégorie.** Un moteur qui étiquette `Organisme` un BIC le masque quand même : la couverture le crédite, la colonne des catégories ne le dit pas. C'est voulu, le sujet est la fuite, pas l'étiquetage.
- **Les noms sans libellé ne sont pas couverts par le moteur regex.** La baseline ne reconnaît un nom que s'il suit un libellé du document ou une civilité. Un nom en cellule de tableau d'émargement ou en en-tête de carte de visite n'a aucune forme distinctive : c'est la charge du détecteur suivant, pas d'une règle de plus.
- **Les listes de mots-clés ne généralisent pas.** Les règles qui reconnaissent une raison sociale à `Institut`, `Industrie` ou `Conseil` attrapent ce que le générateur produit, pas ce qu'écrit un client.
- Les durées ne sont pas comparables d'un run à l'autre sans moyenner plusieurs exécutions. La couverture, elle, est déterministe.

## Structure

| Dossier | Rôle |
|---|---|
| `Engines/` | un moteur par famille, derrière `IPiiDetectionEngine`. Un moteur rend des plages, il ne masque rien |
| `Presidio/` | le pont Python du moteur `presidio` et ses dépendances épinglées |
| `Manifest/` | lecture de `corpus/manifest.jsonl` |
| `VeriteTerrain/` | localisation des valeurs injectées dans le texte de référence |
| `Benchmark/` | exécution d'un run, mesure du coût, écriture des résultats |

Le moteur `presidio` parle à un processus Python tenu ouvert pour tout le run, un document par ligne sur stdin, ses plages sur stdout. Le modèle spaCy met trois à quatre secondes à charger : un processus par document rendrait le run inexploitable, et la poignée de main envoyée au démarrage sort ce chargement de la fenêtre chronométrée. Le script convertit ses positions en unités UTF-16 avant de les rendre, Python indexant en points de code.

Les règles du moteur regex sont de deux familles. Les unes reconnaissent une forme qui se suffit à elle-même, un IBAN ou une adresse de courriel. Les autres partent d'un libellé du document, `BIC`, `RPPS`, `Formateur`, et ne retiennent que la valeur qui suit, capturée dans le groupe 1 : sans ce libellé, un BIC est huit majuscules et un ADELI neuf chiffres, deux formes trop banales pour être masquées à l'aveugle.

Un moteur expose son nom, sa version, ses paramètres et une méthode qui rend les plages sensibles d'un texte. Le benchmark ne connaît ni les règles ni les modèles : la stratégie de détection appartient au moteur, la confrontation à la vérité terrain appartient au benchmark.
