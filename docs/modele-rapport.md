# Modèle de rapport d'évaluation

Template du rapport produit après chaque exécution du protocole défini dans `protocole-evaluation.md`. Un rapport par exécution, versionné à côté du `run.json` correspondant.

> **Tous les chiffres qui suivent sont fictifs.** Ils illustrent le format, les ordres de grandeur attendus et la façon de commenter un résultat. Aucune mesure n'a été produite à ce jour.

Le corps du rapport se recopie tel quel, en remplaçant les valeurs. Les sections sont dans l'ordre de lecture : identification, corpus, qualité, confiance, coût, consommation, comparaisons, limites, recommandation. Un lecteur pressé doit pouvoir s'arrêter après « Confiance » et avoir l'essentiel de la décision.

Chaque section porte ses chiffres **et leur lecture**. Un tableau sans phrase d'interprétation ne sert à rien : c'est l'interprétation qui se discute en réunion, pas la valeur.

---

# Évaluation, OCR Tesseract et Mistral Small, 03/09/2026

## Identification de l'exécution

| | |
|---|---|
| Identifiant du run | `2026-09-03T14-22-05Z-ocr-tesseract` |
| Ce qui est évalué | pipeline complet, OCR Tesseract 5.3.4 puis classification Mistral Small |
| Ce qui change depuis la mesure précédente | le moteur OCR, PaddleOCR remplacé par Tesseract. Rien d'autre |
| Corpus | `manifest.jsonl` `a4f19c2…`, jeu `test`, 160 documents |
| Liste de types soumise | 14 types |
| Modèle | `mistral-small-2506`, température 0 |
| Empreinte du pré-prompt | `9d81b4e…` |
| Machine | Ubuntu 24.04, 8 cœurs, 16 Go |
| Commit | `a1b2c3d` |

Comparable avec tout rapport portant les mêmes empreintes de manifest et de pré-prompt, sur le même jeu et la même taille de liste.

## Corpus utilisé

160 documents du jeu `test` : 95 dont le type attendu figure dans la liste soumise, et 65 distracteurs hors liste. Toutes les métriques de qualité portent sur les 95, les distracteurs ayant leur propre section.

| Type | Documents | Exactitude | F1 |
|---|---|---|---|
| `CARTE_VISITE` | 15 | 53 % | 0,70 |
| `FEUILLE_EMAR` | 15 | 100 % | 0,97 |
| `OFF_STAG_EMP` | 20 | 100 % | 0,91 |
| `RIB` | 15 | 87 % | 0,84 |
| `SEPA` | 15 | 80 % | 0,77 |
| `RIB_SEPA` | 15 | 67 % | 0,67 |

Régime de fichier : 30 documents à texte natif, 65 numérisés.

## Qualité

| Métrique | Valeur |
|---|---|
| **Exactitude globale** | **82,1 %**, 78 documents sur 95, intervalle à 95 % de 74,4 % à 89,8 % |
| Macro-F1 | 0,81 |
| Exactitude sur les documents à texte natif | 90,0 % |
| Exactitude sur les documents numérisés | 78,5 % |

Exactitude et macro-F1 sont proches parce que les types sont représentés à effectifs comparables. La lecture utile est ailleurs : le F1 de `CARTE_VISITE`, 0,70, est très en dessous de tous les autres, et ce type ne pèse que 15 documents sur 95. Sans la ligne par type, il aurait disparu derrière la moyenne.

L'écart de 11,5 points entre texte natif et documents numérisés est la part que l'OCR fait perdre. Il est quantifié précisément plus bas, par la comparaison à la borne haute.

### Confusions

Paires confondues au moins deux fois. Matrice complète en annexe, `annexes/matrice-confusion.csv`.

| Type attendu | Type prédit | Occurrences | Lecture |
|---|---|---|---|
| `CARTE_VISITE` | `OFF_STAG_EMP` | 4 | cartes portant une mention de recrutement, sur un texte trop court pour trancher |
| `RIB_SEPA` | `SEPA` | 4 | le pavé de codes bancaires n'a pas été vu, ou pas jugé déterminant |
| `SEPA` | `RIB_SEPA` | 3 | l'inverse, sur des mandats dont le corps cite l'IBAN du débiteur |
| `CARTE_VISITE` | `RIB` | 2 | numéros longs de la carte lus comme des coordonnées bancaires |
| `RIB` | `RIB_SEPA` | 2 | |

Deux foyers d'erreur, et un seul relève du modèle.

Le groupe `RIB` / `SEPA` / `RIB_SEPA` porte 10 des 17 erreurs. Sur ses 45 documents, il tombe à 78 % d'exactitude, à ±12 points près. Les confusions sont symétriques et internes au groupe : c'est une règle de désambiguïsation qui manque au pré-prompt, pas une faiblesse de lecture.

`CARTE_VISITE` perd 7 documents sur 15, mais aucun autre type n'est prédit `CARTE_VISITE` à tort, sa précision reste à 1,00. Le modèle ne confond pas, il n'a pas assez de texte : sur une carte océrisée, le signal disponible est de quelques dizaines de mots. Ce type relève de la section suivante, celle du score.

## Confiance

Le tableau de décision : ce que le service peut classer seul, et à quel prix.

| Seuil de score | Couverture | Erreur sur la partie auto-classée | À relire |
|---|---|---|---|
| 0,95 | 48 % | 2,2 % | 52 % |
| 0,90 | 66 % | 3,2 % | 34 % |
| 0,80 | 82 % | 7,7 % | 18 % |
| aucun seuil | 100 % | 17,9 % | 0 % |

Lecture : à un seuil de 0,90, deux documents sur trois partent en GED sans relecture, et 3,2 % d'entre eux portent un type erroné. Descendre à 0,80 gagne 16 points de couverture et double le taux d'erreur.

### Fiabilité du score

| Tranche de score | Score moyen annoncé | Exactitude observée | Documents |
|---|---|---|---|
| 0,9 à 1,0 | 0,96 | 96,8 % | 63 |
| 0,8 à 0,9 | 0,85 | 73,3 % | 15 |
| 0,7 à 0,8 | 0,75 | 55,6 % | 9 |
| moins de 0,7 | 0,58 | 12,5 % | 8 |

Écart moyen pondéré, ECE, 0,08.

Le score est fiable au-dessus de 0,90, où les deux colonnes coïncident, et **nettement sur-confiant en dessous** : un document annoncé à 0,85 n'a en réalité que trois chances sur quatre d'être bien classé, et un document sous 0,70 est faux sept fois sur huit. La conséquence est pratique : seul un seuil haut est exploitable en l'état, et les 17 documents des deux tranches basses, en majorité des cartes de visite, doivent partir en relecture sans discussion.

### Documents distracteurs

65 documents dont le type n'appartient pas à la liste soumise. Le service leur attribue nécessairement un type ; le comportement attendu est un score bas, qui les envoie en relecture.

| Comportement | Documents | Part |
|---|---|---|
| score inférieur à 0,80, partent en relecture | 46 | 71 % |
| score entre 0,80 et 0,90 | 11 | 17 % |
| score supérieur à 0,90, auto-classés à tort | 8 | 12 % |

Au seuil de 0,90, huit distracteurs sur 65 recevraient un type de la liste sans relecture. C'est le risque résiduel d'un client dont la liste ne couvre pas tout ce qu'il dépose, et il s'ajoute au taux d'erreur du tableau de décision.

## Coût

| Mesure | Valeur |
|---|---|
| Tokens d'entrée par document | 1 840 en moyenne, 3 200 au p95 |
| Tokens de sortie par document | 18 en moyenne |
| Coût par document | 0,00019 € |
| **Coût pour 1000 documents** | **0,19 €** |

Tarifs appliqués, recopiés du run : 0,10 € par million de tokens en entrée, 0,30 € en sortie. L'OCR est local, il ne coûte rien à l'appel.

Le coût est presque entièrement en entrée, dont environ 600 tokens pour le pré-prompt et la liste des types, le reste pour le texte OCR. Optimiser la sortie n'a aucun effet, elle vaut 1 % du total.

### Effet de la taille de la liste de types

Même corpus, même modèle, seule la liste injectée change.

| Types dans la liste | Tokens d'entrée | Coût pour 1000 documents | Exactitude |
|---|---|---|---|
| 7 | 1 490 | 0,15 € | 85,3 % |
| 14 | 1 840 | 0,19 € | 82,1 % |
| 24 | 2 310 | 0,24 € | 78,9 % |
| 97 | 5 700 | 0,58 € | 71,6 % |

À 97 types, le coût est multiplié par trois et l'exactitude perd 10,5 points. Le coût reste négligeable en valeur absolue ; c'est la qualité qui décroche. Un client à liste large ne peut pas être servi par le même prompt qu'un client à liste courte, et c'est le principal point à instruire avant un déploiement.

## Consommation

| Mesure | p50 | p95 |
|---|---|---|
| Latence totale par document | 3,1 s | 6,8 s |
| dont OCR | 2,4 s | 5,9 s |
| dont appel modèle | 0,6 s | 0,9 s |
| Pic mémoire du processus | 410 Mo | 680 Mo |
| Temps processeur par document | 2,8 s | 6,2 s |

L'OCR représente 77 % de la latence et la totalité de la consommation mémoire. Toute optimisation de temps ou de ressources se joue là, l'appel au modèle est déjà négligeable.

## Comparaison aux repères

| Configuration | Exactitude | Couverture au seuil 0,90 | Coût pour 1000 documents | Latence p95 |
|---|---|---|---|---|
| Baseline sans IA, mots-clés | 58,9 % | 21 % | 0 € | 2,5 s |
| **Pipeline évalué** | **82,1 %** | **66 %** | **0,19 €** | **6,8 s** |
| Borne haute, texte natif sans OCR | 90,0 % | 79 % | 0,17 € | 1,0 s |

Le modèle dépasse la baseline de 23 points, et surtout il triple la part de documents exploitables sans relecture. L'appel à un modèle de langage est justifié.

L'écart avec la borne haute chiffre ce que coûte l'OCR : environ 8 points d'exactitude et 13 points de couverture. C'est la référence sur laquelle se comparera le moteur OCR suivant.

## Limites de cette mesure

- 95 documents classables : toute conclusion globale s'entend à ±8 points, un groupe de types à ±12 points. Deux configurations séparées par moins de 8 points ne sont pas départagées par cette mesure.
- 6 types sur les 14 du périmètre sont représentés. Les huit autres n'ont pas encore de documents, dont les quatre types de convention et de certificat, réputés les plus difficiles.
- Le corpus est entièrement généré. Des mises en page absentes du générateur existent en production.
- Le masquage PII n'est pas en place : cette mesure ne dit rien de ce qu'il coûtera en qualité.

## Recommandation

Trois phrases au plus, qui répondent explicitement à la question du protocole. Formulation attendue :

> Le pipeline classe automatiquement 66 % des documents avec 3,2 % d'erreur, pour 0,19 € les mille documents. Le groupe `RIB` / `SEPA` / `RIB_SEPA` porte 10 des 17 erreurs et relève d'une règle de désambiguïsation à ajouter au pré-prompt, pas d'un changement de modèle. La latence et la mémoire viennent à plus des trois quarts de l'OCR : c'est le seul chantier de performance qui vaille, et le seuil d'exploitation ne peut pas descendre sous 0,90 tant que le score reste sur-confiant en dessous.
