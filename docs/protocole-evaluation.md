# Protocole d'évaluation

Ce protocole définit ce qu'on mesure sur chaque solution ou incrément du service, comment on le mesure, et ce qu'il faut figer pour que deux mesures prises à deux semaines d'intervalle restent comparables. Il sert autant à comparer deux moteurs OCR qu'à comparer deux versions du pré-prompt ou deux fournisseurs de modèle.

Le résultat d'une exécution se met en forme selon `modele-rapport.md`.

## La question à laquelle le protocole doit répondre

Le service renvoie toujours un type, celui de meilleur score, même quand ce score est faible. La question utile n'est donc pas « le service se trompe-t-il ? », il se trompera, mais :

> **Quelle proportion des documents peut être classée sans relecture humaine, et à quel taux d'erreur sur cette proportion ?**

Toutes les métriques ci-dessous existent pour répondre à cette question, ou pour expliquer pourquoi la réponse est mauvaise.

## Métriques de qualité

Cinq mesures, toutes calculables à partir d'un seul fichier de résultats : une ligne par document, avec le type attendu, le type prédit et le score de confiance.

### Exactitude globale, avec son intervalle de confiance

Part des documents dont le type prédit est le type attendu. C'est la lecture de premier niveau, et elle ne se lit jamais seule : sur 210 documents, une exactitude mesurée à 88 % vaut « entre 84 % et 92 % ». Deux solutions séparées par deux points ne sont pas départagées, elles sont à égalité.

L'intervalle est l'intervalle binomial habituel, demi-largeur `1,96 × racine(p(1-p)/n)`. Il est écrit à côté de chaque exactitude du rapport, précisément pour couper court aux conclusions tirées d'un écart qui tient dans le bruit.

**Les documents distracteurs sont exclus de ce calcul.** Le corpus contient des documents dont le type n'appartient pas à la liste soumise, factures et devis aujourd'hui : aucune réponse n'est juste pour eux, les compter comme des erreurs mélangerait deux phénomènes. Ils sont mesurés séparément, par la part d'entre eux qui reçoit un score inférieur au seuil envisagé. Un distracteur bien traité n'est pas un distracteur bien classé, c'est un distracteur qui part en relecture.

### Exactitude par type et matrice de confusion

L'exactitude globale dit qu'il y a un problème, la matrice dit lequel. Elle est indispensable ici parce que la difficulté du corpus est concentrée sur des groupes de types voisins : `RIB` contre `SEPA` contre `RIB_SEPA`, les trois conventions, les deux certificats, le contrat contre son CERFA. Une erreur à l'intérieur d'un de ces groupes et une erreur entre deux types sans rapport n'ont ni la même cause ni le même remède.

Le rapport porte l'exactitude par type, et la liste des paires confondues au moins deux fois. La matrice complète reste en annexe : à 14 types elle devient illisible dans un document de synthèse.

### Macro-F1

Moyenne des F1 de chaque type, chaque type comptant pour un, quel que soit son effectif.

Elle existe pour une seule raison : le corpus n'est pas équilibré, et un type peu représenté qui ne marche jamais reste invisible derrière une bonne exactitude globale. Un écart marqué entre exactitude et macro-F1 est le signal qu'un ou deux types sont abandonnés par le modèle.

### Couverture à taux d'erreur donné

La mesure de décision. Elle se calcule en triant les documents par score de confiance décroissant. Pour un seuil `s` :

| Grandeur | Définition |
|---|---|
| couverture | part des documents dont le score est supérieur ou égal à `s`, donc classés automatiquement |
| erreur | taux d'erreur **parmi ces seuls documents** |
| à relire | le complément de la couverture, ce qui repart en qualification manuelle |

Le rapport donne ce tableau à quatre seuils, plus la ligne « aucun seuil » qui correspond à l'exactitude globale. Le seuil d'exploitation n'est pas choisi ici : il se choisit à la mise en production, sur ces chiffres, en fonction du taux d'erreur que le métier accepte. Le protocole fournit la courbe, pas la décision.

C'est cette lecture qui traduit le besoin GED, où le gain se mesure en documents qu'un opérateur n'a plus à ouvrir.

### Fiabilité du score

Le tableau précédent ne vaut que si le score veut dire quelque chose. On le vérifie en regroupant les documents par tranche de score, et en comparant dans chaque tranche le score moyen annoncé à l'exactitude réellement observée.

| Tranche de score | Score moyen annoncé | Exactitude observée |
|---|---|---|
| 0,9 à 1,0 | | |
| 0,8 à 0,9 | | |
| 0,7 à 0,8 | | |
| moins de 0,7 | | |

Deux colonnes proches sur toutes les lignes : le score est exploitable comme seuil. Un modèle qui annonce 0,95 et se trompe une fois sur cinq rend tout seuillage inopérant, et c'est un défaut qui ne se voit sur aucune autre métrique. La moyenne des écarts entre les deux colonnes, pondérée par l'effectif de chaque tranche, est l'ECE, à utiliser si un chiffre unique est nécessaire pour comparer deux versions.

Le type `CARTE_VISITE` est le révélateur naturel de ce défaut : une carte de visite océrisée donne quelques dizaines de mots, c'est exactement là qu'un score élevé serait suspect.

## Métriques de coût

Le coût monétaire vient des appels au modèle. Les moteurs OCR évalués sont locaux et open source : ils ne coûtent rien à l'appel, mais ils coûtent du CPU et de la RAM, comptés dans la section suivante. Si un OCR facturé à l'appel est évalué un jour, il prend sa propre ligne dans ce tableau.

| Mesure | Comment |
|---|---|
| tokens d'entrée par document | renvoyés par l'API du modèle, moyenne et p95 |
| tokens de sortie par document | idem |
| coût par document | tokens × tarif du modèle, le tarif étant recopié dans le run |
| **coût pour 1000 documents** | la même valeur, à l'échelle où la question se pose vraiment |

La décomposition entrée / sortie n'est pas cosmétique : la sortie du service est courte, un type et un score. L'essentiel du coût est donc dans l'entrée, c'est-à-dire dans le pré-prompt et le texte OCR. C'est ce que confirme ou infirme la mesure, et c'est ce qui dit s'il faut travailler le prompt ou tronquer le texte.

### Coût en fonction de la taille de la liste de types

La liste des types est injectée dans le pré-prompt et fournie par le client : sa taille est une entrée du service, pas une constante. La même mesure est donc reprise à quatre paliers, avec le même corpus et le même modèle :

| Palier | Ce qu'il représente |
|---|---|
| 7 types | priorité 1 |
| 14 types | priorités 1 et 2, le périmètre du corpus |
| 24 types | la liste priorisée complète |
| 97 types | tous les types de la production, le pire cas |

Ce que ça donne : le coût par document en fonction de la taille de liste, et surtout l'effet de cette taille sur la qualité. Un service qui tient à 14 types et s'effondre à 97 n'est pas déployable en l'état, et c'est le genre de découverte qui coûte cher en septembre et rien du tout maintenant.

## Métriques de consommation

| Mesure | Comment | Pourquoi |
|---|---|---|
| latence par document, p50 et p95 | horodatage autour de chaque étape | la moyenne cache les documents lourds, le p95 est ce que l'utilisateur ressent |
| latence décomposée par étape : OCR, masquage, appel modèle | idem | savoir si les quatre secondes viennent de l'OCR ou du modèle change complètement l'action à mener |
| pic mémoire par document | working set échantillonné pendant l'extraction, plus haut relevé retenu | critère de sélection de l'OCR |
| mémoire propre au document | écart entre ce pic et le working set relevé juste avant | seule part comparable d'un moteur à l'autre, le pic absolu portant aussi le socle déjà occupé |
| temps processeur consommé | `Process.TotalProcessorTime`, relevé par document | distingue un traitement lent d'un traitement gourmand |

Le temps processeur se relève avec les API .NET standard, sans profileur ni outillage tiers.

**Le pic mémoire demande un échantillonnage, pas un compteur.** `Process.PeakWorkingSet64` est le pic du processus depuis son démarrage et ne redescend jamais : relevé par document, il fait hériter chaque document du pic de tous ceux qui l'ont précédé, et le maximum sur un run vaut alors toujours la valeur du dernier document mesuré. Aucun système ne tient ce pic à l'échelle d'un intervalle, et les compteurs du GC ne voient pas la mémoire allouée en non managé par les moteurs OCR, qui en est l'essentiel. Le runner échantillonne donc le working set toutes les 25 ms pendant l'extraction et garde le maximum, ce que ferait de toute façon un profileur externe. Contrôlé contre `PeakWorkingSet64` sur une charge d'une seconde et demie à pic unique : les deux rendent la même valeur.

Cette mesure impose deux conditions au runner, sans quoi les chiffres publiés ne décrivent pas le moteur. Rien d'autre que l'extraction ne doit se produire dans la fenêtre chronométrée, en particulier aucune écriture de fichier de mise au point. Et le processus ne doit rien accumuler d'un document à l'autre : ce qu'il retient reste dans le working set et gonfle le pic de tous les documents suivants.

## Ce que le corpus permet de conclure

La précision d'une mesure dépend du nombre de documents sur lesquels elle porte. Pour une exactitude autour de 90 % :

| Mesure portant sur | Effectif | Demi-largeur de l'intervalle |
|---|---|---|
| l'ensemble d'un jeu de mesure | 200 | ±4 points |
| | 160 | ±5 points |
| un groupe de types confondables | 45 | ±9 points |
| un type isolé | 15 | ±15 points |

L'intervalle s'élargit quand l'exactitude s'éloigne de 90 %. À 78 % sur 45 documents, il vaut ±12 points.

À lire comme une limite du protocole, pas comme un détail méthodologique : le corpus départage deux solutions sur l'ensemble, et diagnostique un groupe confondable. Il ne permet pas de conclure type par type. C'est un choix assumé, la question intéressante étant « le modèle sépare-t-il `CERTIF_REAL` de `CERTIF_FORM` », qui porte sur un groupe, et non « quel score exact sur `CERTIF_REAL` ».

Si une première mesure montre qu'un groupe pose vraiment problème, le renforcer coûte peu, le générateur produit les documents à la demande.

## Conditions d'exécution

### Le jeu de test est scellé

Le corpus est découpé en `dev` et `test` selon une règle arithmétique sur la seed, écrite dans le manifest. Tout le réglage, pré-prompt, choix d'OCR, paramètres d'appel, se fait sur `dev`. Le jeu `test` ne s'ouvre qu'au moment de produire une mesure destinée au rapport.

Sans cette séparation, les chiffres du rapport final sont ceux d'un prompt ajusté aux documents qui servent à le noter, et ils s'effondrent en production. C'est gratuit à tenir dès le premier jour, et irrattrapable ensuite.

### Une variable à la fois

Entre deux exécutions comparées, une seule chose change : le moteur OCR, ou la version du prompt, ou le modèle. Deux changements simultanés donnent un écart qu'on ne sait attribuer à aucun des deux.

### Ce que chaque exécution enregistre

Un fichier `run.json` par exécution, écrit par le runner, sans saisie manuelle :

```json
{
  "run_id": "2026-09-03T14-22-05Z-ocr-tesseract",
  "date": "2026-09-03T14:22:05Z",
  "git_sha": "a1b2c3d",
  "manifest_sha256": "…",
  "split": "test",
  "prompt_sha256": "…",
  "nombre_types_dans_la_liste": 14,
  "modele": "mistral-small-2506",
  "parametres_modele": { "temperature": 0, "max_tokens": 64 },
  "tarif_modele_eur_par_million_tokens": { "entree": 0.1, "sortie": 0.3 },
  "ocr": { "moteur": "tesseract", "version": "5.3.4", "langues": ["fra"] },
  "machine": { "os": "…", "cpu": "…", "coeurs": 8, "ram_go": 16 },
  "cache": "actif"
}
```

Deux points méritent l'attention :

- **La version du modèle est épinglée**, jamais un alias mouvant. Un alias qui change entre deux mesures rend la comparaison fausse sans que rien ne le signale.
- **Le hash du manifest et celui du pré-prompt** sont ce qui prouve que deux rapports portent bien sur les mêmes documents et le même prompt.

Les réponses OCR et modèle sont mises en cache sur disque, indexées par le hash du document et celui des paramètres. Rejouer une évaluation dont rien n'a changé doit être gratuit et instantané, sinon plus personne ne la rejoue.

### Variabilité résiduelle

Le modèle est appelé à température 0, ce qui limite la variabilité sans la supprimer. Elle s'estime en rejouant le jeu `dev` trois fois, cache désactivé, et en reportant l'écart entre les trois exactitudes obtenues. Un écart supérieur à l'intervalle de confiance rendrait toute comparaison fine illusoire, et c'est une chose qu'il vaut mieux savoir avant de comparer deux solutions.

### Deux rapports comparables

Deux rapports se comparent si et seulement si `manifest_sha256`, `split` et `nombre_types_dans_la_liste` sont identiques. Ces trois valeurs sont recopiées en tête de chaque rapport, avant tout chiffre.

## Points de comparaison obligatoires

Un chiffre seul ne dit rien. Chaque mesure de qualité se lit contre deux repères, produits une fois et rejoués à chaque incrément.

| Repère | Ce qu'il est | Ce qu'il apprend |
|---|---|---|
| **Baseline sans IA** | mots-clés et expressions régulières sur le texte OCR, une demi-journée d'écriture | si le modèle ne la bat pas nettement, la recommandation du POC change de nature, et le coût, la latence et le risque PII disparaissent avec elle |
| **Borne haute sans OCR** | classification directe sur le texte natif des documents qui en ont un | l'écart avec le pipeline complet isole exactement ce que l'OCR fait perdre en qualité, et permet de trancher le choix du moteur sur un chiffre |

Une troisième comparaison s'ajoutera le jour où le pipeline pourra être évalué avec et sans masquage PII : la même évaluation dans les deux configurations, pour mesurer ce que le masquage coûte en qualité de classification.

## Déroulé d'une évaluation

1. Vérifier le corpus : le validateur du manifest doit passer sans anomalie, chaque fichier présent et son hash conforme.
2. Choisir le jeu, `dev` pour régler, `test` pour mesurer, et le nombre de types de la liste.
3. Lancer le runner, qui produit `run.json` et un fichier de résultats à une ligne par document : identifiant, type attendu, type prédit, score, tokens entrée et sortie, latence par étape, pic mémoire.
4. Calculer les métriques à partir de ce seul fichier de résultats.
5. Remplir `modele-rapport.md` et le versionner à côté du `run.json` correspondant.

Le fichier de résultats est le seul intermédiaire entre l'exécution et le rapport : n'importe quelle métrique de ce document se recalcule à partir de lui, sans relancer une seule fois le modèle.

Le runner existe : `tools/ClassificationEvaluation`, qui envoie le lot à `/api/documents/classify-batch` et écrit `run.json`, `resultats.jsonl`, `recap.md` et `matrice-confusion.csv` (voir son README). Le format décrit ici reste la cible de ce qu'un run doit consigner : l'API n'exposant ni tokens, ni coût, ni version du flow ni empreinte du prompt, le `run.json` actuel se limite à `run_id`, `date`, `manifest_sha256`, `api_url`, `engine`, `split`, `types`, `descriptions_sha256` et `machine`, et les lignes de résultats ne portent ni tokens ni pic mémoire.
