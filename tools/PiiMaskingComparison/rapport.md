# Comparatif des moteurs de masquage PII

Résultats de `PiiMaskingComparison` sur le corpus de Detect-Doc. Ce document répond à une seule question : **quel moteur choisir pour retirer les PII d'un texte avant de l'envoyer à Flowise, et à quel prix.**

Méthode, règles de la vérité terrain et commandes dans le [README](README.md).

## Conditions de mesure

| | |
|---|---|
| Corpus | split `dev`, 67 documents, manifest `e5aefaf7e571` |
| Entrée | le champ `texte_reference` du manifest, pas une sortie d'OCR |
| Occurrences attendues | 528, localisées dans le texte à partir des valeurs déclarées dans `pii_injectees` |
| Environnement | conteneur `detectdoc-pii-comparison`, Docker Desktop, mesures par `docker stats` |
| Suite de référence | `2026-09-10_12-29-05` à `2026-09-10_12-34-43`, sept configurations enchaînées |

Une occurrence n'est comptée **couverte** que si toute sa plage est détectée : une valeur à moitié masquée laisse de quoi identifier la personne. Les mesures de coût qui suivent viennent du conteneur ; celles annotées « mesure hôte » ont été prises sur Windows et ne servent qu'en comparaison relative.

## Les moteurs comparés

| Moteur | Nature | Modèle | Dépendances |
|---|---|---|---|
| `regex` | dix-sept règles .NET, formes normées et valeurs annoncées par un libellé | aucun | aucune |
| `presidio` | Presidio 2.2.364 et spaCy 3.8.16 | `fr_core_news_md`, 45 Mo, pipeline réduit à `tok2vec, ner` | Python |
| `gliner2` | GLiNER2.5 | `fastino/gliner2.5-multi-v1`, révision `aaecfe45db1d`, 287M paramètres | Python, torch |
| `hybride-*` | union des plages des règles et d'un moteur NER, sans arbitrage | selon le moteur composé | selon le moteur composé |

Presidio tourne avec les entités restreintes à `PERSON, EMAIL_ADDRESS, PHONE_NUMBER, IBAN_CODE`, GLiNER2.5 avec les labels `person, organization, address, email, phone number, iban`, seuil 0,5, texte découpé en blocs de 1200 caractères.

## Couverture par catégorie

| Catégorie | Occurrences | `regex` | `presidio` | `gliner2` | hybride Presidio | hybride GLiNER2.5 |
|---|---|---|---|---|---|---|
| Nom | 235 | 20,0 % | 86,8 % | 94,5 % | 94,0 % | **95,3 %** |
| Organisme | 64 | 57,8 % | 18,8 % | 70,3 % | 70,3 % | **81,2 %** |
| Email | 22 | 100,0 % | 95,5 % | 95,5 % | 100,0 % | 100,0 % |
| Telephone | 25 | 100,0 % | 100,0 % | 100,0 % | 100,0 % | 100,0 % |
| Adresse | 20 | 100,0 % | 0,0 % | 95,0 % | 100,0 % | 100,0 % |
| Iban | 30 | 100,0 % | 100,0 % | 40,0 % | 100,0 % | 100,0 % |
| Bic | 17 | 100,0 % | 0,0 % | 0,0 % | 100,0 % | 100,0 % |
| Siret | 17 | 100,0 % | 0,0 % | 5,9 % | 100,0 % | 100,0 % |
| Identifiants professionnels | 86 | 97,7 % | 18,6 % | 90,7 % | 97,7 % | **98,8 %** |
| Numéro de document, RUM | 11 | 100,0 % | 18,2 % | 0,0 % | 100,0 % | 100,0 % |
| Réseau social | 1 | 100,0 % | 0,0 % | 0,0 % | 100,0 % | 100,0 % |
| **Global** | **528** | **58,9 %** | **58,7 %** | **80,1 %** | **93,4 %** | **95,5 %** |
| Documents laissant fuiter | 67 | 50 | 53 | 45 | 29 | **16** |

### Ce que chaque moteur sait faire

**Les règles couvrent tout ce qui a une forme normée, et rien d'autre.** IBAN, BIC, SIRET, RPPS, ADELI, RUM, courriel, téléphone, adresse postale : entre 97,7 et 100 %. Les noms leur échappent presque entièrement, 20 %, et seulement quand un libellé les annonce, `Formateur :`, `Titulaire du compte`, une civilité, ou une énumération après `PARTICIPANTS :`.

**Presidio n'apporte que les noms.** 86,8 %, et rien ailleurs : aucun reconnaisseur pour les formats français, et son `LOCATION` n'étiquette que la commune, jamais la voie, d'où 0 % sur l'adresse complète. Sans son modèle spaCy il tombe à 17,4 % et devient une bibliothèque de motifs dominée par les règles du comparatif, mesure hôte.

**GLiNER2.5 est le meilleur détecteur de langage naturel** : 94,5 % sur les noms, 95 % sur l'adresse postale entière, 70,3 % sur les raisons sociales. Il ignore les identifiants structurés faute de label, et confond les nombres : ses détections `iban` tombent sur des RPPS à onze chiffres, ce qui lui donne 90,7 % d'identifiants professionnels par accident et fait chuter sa couverture des vrais IBAN à 40 %. La valeur est masquée, l'étiquette est fausse.

**Les deux familles sont complémentaires, pas concurrentes.** Aucune ne dépasse 81 % seule, leur union atteint 95,5 %.

## Ce que coûte chaque solution

Mesures du conteneur, corpus complet, un document à la fois.

| | Durée du run | Débit | Latence médiane | RAM maximale | CPU médian | Chargement |
|---|---|---|---|---|---|---|
| `regex` | 0,1 s | 712 doc/s | 0,25 ms | non mesurable | non mesurable | néant |
| `presidio` | 2,1 s | 32,7 doc/s | 27 ms | 612 Mo | 100 % | 7,0 s |
| hybride Presidio | 2,1 s | 32,6 doc/s | 26 ms | 614 Mo | 100 % | 6,9 s |
| `gliner2` | 71,0 s | 0,94 doc/s | 618 ms | 2165 Mo | 397 % | 9,1 s |
| hybride GLiNER2.5 | 103,5 s | 0,65 doc/s | 908 ms | 2528 Mo | 398 % | 9,2 s |

**Le premier hybride est 50 fois plus rapide et prend 4 fois moins de RAM, pour seulement 2 % de couverture en moins.** Autrement dit, GLiNER2.5 évite 13 fuites de données sur 67, mais cela coûte 1,9 Go de mémoire et 880 ms de plus par document.

*Mesure hôte.* La mémoire d'un moteur NER est un coût fixe, payé au chargement et non par document : Python nu 13 Mo, `torch` importé 193 Mo, modèle chargé 1869 Mo, puis 1825 Mo après un document comme après six. Au moment du chargement, la coexistence des fichiers lus et du modèle en mémoire crée un pic temporaire qui dépasse ce niveau stable d'environ 1 Go.

## La concurrence, et pourquoi elle ne paie pas

Un modèle chargé une seule fois, N documents confiés en même temps.

| | Débit | Latence médiane | RAM maximale | CPU médian | CPU par document |
|---|---|---|---|---|---|
| hybride Presidio, 1 document | 32,6 doc/s | 26 ms | 614 Mo | 100 % | 0,03 s |
| hybride Presidio, 8 documents | 19,7 doc/s | 340 ms | 732 Mo | 100 % | 0,05 s |
| hybride GLiNER2.5, 1 document | 0,65 doc/s | 908 ms | 2528 Mo | 398 % | 6,15 s |
| hybride GLiNER2.5, 8 documents | 0,42 doc/s | 3250 ms | 3239 Mo | 798 % | 18,99 s |

Sous charge, la latence médiane compte l'attente en file : seul le débit se compare.

**Traiter huit documents de front fait perdre du débit dans les deux cas**, pour deux raisons différentes. Presidio reste bloqué à 100 % de processeur, soit un cœur : huit threads Python ne parallélisent pas spaCy, ils font la queue sur le verrou global de l'interpréteur et ajoutent de la coordination. GLiNER double bien son processeur, à 798 %, mais `torch` utilisait déjà quatre cœurs pour une seule inférence : huit inférences demandent trente-deux threads sur huit cœurs, et le coût processeur par document est **multiplié par trois** pour un débit en baisse.

La mémoire, elle, ne double pas : les poids sont partagés et seules les activations se multiplient, d'où 118 Mo de plus pour Presidio et 711 Mo pour GLiNER2.5, ce dernier écart venant de la taille des activations d'un transformeur.

La couverture est identique à 1 et à 8 documents, 493 et 504 occurrences, ce qui vérifie au passage que le chemin concurrent ne perd ni ne duplique aucune détection.

## Le surajustement au corpus

Chaque règle porte sa nature. Les règles dites de vocabulaire reconnaissent une raison sociale à des mots relevés dans ce corpus, `Institut`, `Académie`, `Industrie`, `Conseil` : elles apprennent le vocabulaire du générateur, pas celui d'un client.

| | Couverture | Hors règles de vocabulaire | Écart |
|---|---|---|---|
| `regex` | 58,9 % | 52,5 % | 6,4 points |
| hybride Presidio | 93,4 % | 87,7 % | 5,7 points |
| hybride GLiNER2.5 | 95,5 % | **94,5 %** | **1,0 point** |

**C'est l'argument le plus fort en faveur de GLiNER2.5, plus que ses deux points de couverture.** Avec lui, le score cesse de dépendre du vocabulaire du corpus : les règles de vocabulaire ne valent plus qu'une occurrence sur 528 et peuvent être supprimées, avec le surajustement qu'elles portent. Le modèle, lui, n'a pas été entraîné par le projet et ne doit rien au corpus.

Une limite à rappeler à chaque publication de chiffre : **le jeu `test` scellé ne protège pas de ce risque**, puisqu'il sort du même générateur, des mêmes gabarits et du même vocabulaire. Il mesure la généralisation à de nouveaux tirages, pas à des documents de production.

## Faux positifs et sur-masquage

| | Détections | dont fausses | Part du texte masquée |
|---|---|---|---|
| `regex` | 550 | 41,8 % | 11,7 % |
| `presidio` | 660 | 49,5 % | 11,6 % |
| `gliner2` | 934 | 53,6 % | 19,5 % |
| hybride Presidio | 1210 | 46,0 % | 18,7 % |
| hybride GLiNER2.5 | 1484 | 49,3 % | 21,5 % |

**Ces taux ne se lisent pas comme une précision.** Est compté faux positif ce qui ne recouvre aucune valeur déclarée au manifest ; or le corpus déclare ce qu'il a injecté, pas tout ce qu'il contient. L'adresse de l'organisme émetteur, l'employeur d'un participant ou le BIC de la banque sont détectés à juste titre sans y figurer. Une précision au sens strict demanderait une vérité terrain annotée sur le texte, pas déduite des valeurs injectées.

La part de texte masquée, elle, ne dépend d'aucune déclaration : c'est la mesure objective du sur-masquage. L'hybride GLiNER2.5 retire un cinquième du texte, contre un neuvième pour les règles seules.

Il reste du vrai bruit à traiter dans les détections du NER, des mots courants pris pour des personnes, `Assistant`, `Signature`, `Fils`, `Émargements`. C'est le seul défaut qui dégraderait le texte envoyé à Mistral.

## Ce qui résiste encore

Vingt-quatre occurrences sur 528 échappent au meilleur hybride.

| Catégorie | Occurrences | Nature |
|---|---|---|
| Organisme | 12 | raisons sociales dans un nom de domaine, `reynaud-competences` dans une adresse de site, ou dans un logo imprimé lettre par lettre |
| Nom | 11 | noms de participants en cellule de tableau d'émargement, sans libellé ni forme distinctive |
| Identifiants professionnels | 1 | localisation parasite, un numéro de déclaration d'activité contenant par hasard les chiffres d'un ADELI |

## Recommandation

**Retenir l'hybride règles plus GLiNER2.5** pour la qualité de masquage : 95,5 % de couverture, 94,5 % sans dépendance au vocabulaire du corpus, et deux fois moins de documents laissant fuiter une PII que l'alternative.

**Si la latence ou la mémoire deviennent contraignantes, l'hybride règles plus Presidio reste défendable** : 93,4 %, cinquante fois plus rapide et quatre fois plus léger. Il paie ce gain par une dépendance de 5,7 points au vocabulaire du corpus, qui ne se transposera pas à des documents de production.

Le choix relève donc de la latence acceptable pour la route de classification, pas de la qualité de détection. Il se tranchera avec les tests de charge du WI 115219.

## Conséquences pour l'API

**Charger le modèle une fois**, dans un singleton résolu paresseusement, comme le fait déjà `PpOcrEngine` pour l'OCR. Le coût mémoire est fixe et payé au démarrage : environ 650 Mo avec Presidio, 2,5 Go avec GLiNER2.5, plus un pic transitoire d'un gigaoctet au chargement.

**Sérialiser le masquage derrière un `SemaphoreSlim`** plutôt que de laisser les requêtes concurrentes se battre. La mesure montre que le parallélisme fait perdre du débit et triple le coût processeur par document. La k-ième requête simultanée attend alors k-1 fois la durée d'un masquage : environ 200 ms au pire pour huit requêtes avec Presidio, mais douze secondes avec GLiNER2.5, à rapprocher du timeout de 60 secondes du client Flowise.

**Ne pas déployer de pool de processus Python** : chacun rechargerait sa propre copie du modèle, multipliant les gigaoctets sans gagner de débit, le processeur étant déjà saturé par un seul document.

## Limites et cas non couverts dans l'API

Le masquage réduit les données personnelles présentes dans `form.texte` avant Flowise. Il ne constitue pas une anonymisation et ne garantit pas qu'une personne ne puisse plus être reconnue à partir du contexte restant.

| Limite | Conséquence |
|---|---|
| Détection probabiliste des noms, organismes et lieux | Un nom rare, isolé, mal segmenté ou confondu avec un mot courant peut rester dans le texte. |
| Règles dépendantes de la forme et du contexte | Un identifiant sans libellé, avec une mise en forme inconnue ou fortement altéré par l'OCR peut ne pas être reconnu. |
| Entrée mesurée sur du texte synthétique propre | Les taux publiés ne prouvent pas la même couverture sur une sortie OCR ni sur des documents de production. |
| Catégories limitées | Dates de naissance, données médicales libres, numéros de carte bancaire, plaques d'immatriculation, adresses IP et informations biométriques ne disposent pas de catégorie dédiée. |
| Sur-masquage du NER | Des mots comme `Assistant`, `Signature`, `Fils` ou `Émargements` peuvent être remplacés à tort et retirer des indices utiles à la classification. |
| Marqueurs typés fondés sur la catégorie détectée | Une catégorie erronée peut produire un marqueur trompeur pour le LLM, même lorsque la valeur sensible est bien retirée. |
| Protection limitée au texte envoyé | Le document original, les images, signatures, QR codes, métadonnées et fichiers stockés restent sensibles dans les systèmes qui les détiennent. L'API n'envoie pas le document à Flowise. |
| Champs hors `form.texte` | La liste libre `types` n'est pas masquée. Elle doit contenir uniquement des identifiants documentaires sans donnée personnelle. |
| Routes de diagnostic | Les routes `/api/dev/extract` et `/api/dev/pii/detect` exposent volontairement du texte brut en réponse. Elles sont disponibles uniquement en environnement Development. |
| Journaux et traces | Un proxy, une journalisation HTTP ou un outil d'observabilité peut conserver les prompts. Les validations Langfuse utilisent exclusivement des valeurs synthétiques et les traces conservées doivent rester conformes à la politique d'accès et de rétention. |

Le traitement échoue de manière fermée : si la détection ou le remplacement n'aboutit pas, la route retourne HTTP 503 et n'appelle pas Flowise. Un test capture aussi le corps HTTP sérialisé par le vrai client Flowise et vérifie que la valeur synthétique brute est absente tandis que son marqueur est présent.

La preuve jusqu'au fournisseur a été observée le 11/09/2026 dans Langfuse, trace [ff5de3de-4146-40fd-b9d9-2f9115dfe6e6](https://valia-langfuse.val.local/project/cmke3ik3h000eps07drtyc8p4/traces?peek=ff5de3de-4146-40fd-b9d9-2f9115dfe6e6), modèle `mistral-small-latest`, sur une feuille d'émargement synthétique du corpus envoyée à `POST /api/documents/classify` avec la liste `FACTURE, DEVIS, CV, FEUILLE_EMARGEMENT`. L'entrée du nœud Mistral est le texte masqué par l'API : les marqueurs `[PERSON]`, `[ORGANIZATION]`, `[ADDRESS]` et `[PROFESSIONAL_ID]` y figurent et aucune des valeurs remplacées n'apparaît. Cette recette vérifie un chemin observé ; elle ne transforme pas le masquage probabiliste en garantie universelle.

La même trace illustre les limites du tableau ci-dessus. Restent lisibles chez le fournisseur : des noms alignés sans libellé dans la liste des intervenants (`Tessier Théodore Marion`, `Mercier Julien Pierre Édouard Ruíz`, `Peron Alfred Boutin`), un patronyme dans une raison sociale (`Établissements Pereira`), un identifiant ADELI collé à son libellé (`ADELI182771883`) alors que les RPPS voisins sont masqués, ainsi que le numéro de déclaration d'activité et les références de l'action, qui ne relèvent pas des catégories protégées. À l'inverse, le mot `Feuille` de « Feuille collective d'émargement » est remplacé par `[PERSON]` sur la seconde page, ce qui retire un indice direct du type attendu.

## Limites de ces mesures

L'entrée est le texte propre du manifest, pas une sortie d'OCR : la dégradation due à la reconnaissance est un facteur distinct, non mesuré ici.

Dix des soixante-sept documents `dev` sont des dérivés, des dégradations d'image dont le texte de référence est celui de leur parent : ces textes pèsent double dans les totaux tant que l'entrée mesurée est le manifest.

Quinze valeurs déclarées au manifest ne sont localisées sous aucune forme comparable dans le texte et sortent de la mesure : elles constituent la marge d'incertitude des taux publiés.

Les temps dépendent de la charge de la machine ; les écarts d'un ordre de grandeur sont significatifs, ceux de quelques pourcents ne le sont pas.

## Reproduire

Depuis `tools/PiiMaskingComparison` :

```powershell
docker build --tag detectdoc-pii-comparison .
.\Mesurer-Suite.ps1
```

Chaque dossier de run porte `run.json` avec la configuration exacte, `resultats.jsonl` avec le détail par occurrence et par détection, `recap.md`, `stats.csv` et `stats.md`. Tout chiffre de ce rapport est recalculable depuis ces fichiers et le manifest, sans faire confiance au tool.

*Mesures du 10/09/2026. Mettre ce rapport à jour lorsqu'un moteur, un réglage ou le corpus change.*
