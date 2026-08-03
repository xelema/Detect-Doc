# Corpus

Le corpus de test est décrit par `corpus/manifest.jsonl`, une ligne JSON par document. C'est le seul livrable versionné : `corpus/raw/`, qui contient les PDF eux-mêmes, n'est pas suivi par Git, il se refabrique à l'identique à partir du manifest et de `tools/CorpusGenerator`.

## Source

Tous les documents du corpus sont générés de toutes pièces par `tools/CorpusGenerator` (organisme, client, montants, identifiants bancaires, participants). Aucune donnée personnelle réelle n'entre dans le corpus. Les identifiants produits (IBAN, SIREN, SIRET, TVA, ICS) portent des clés de contrôle justes mais ne désignent personne.

Des exemples réels ont été reçus pour quatre types (carte de visite, feuille d'émargement, offre d'emploi, RIB) et ont servi à régler la forme du générateur : mise en page, vocabulaire, régime de fichier. Ils restent hors du corpus et hors du dépôt Git.

Deux types hors périmètre de la liste métier, facture et devis, sont conservés au corpus comme documents distracteurs : ils mesurent le comportement du service quand aucun type de la liste ne convient.

## Licence

Chaque entrée porte `"licence": "interne"` : un document généré n'a pas de licence externe à respecter, il appartient au POC. Le champ existe pour accueillir une valeur différente le jour où une source externe, spécimen public ou exemple assaini, entrera au corpus.

## Sensibilité

Trois niveaux, un champ `sensibilite` par entrée :

| Niveau | Signification | Sort chez un fournisseur IA |
|---|---|---|
| `public` | public ou entièrement synthétique | sans masquage |
| `interne_assaini` | valeurs réelles remplacées | masquage obligatoire |
| `production_valide` | validé nominativement | masquage obligatoire, ne quitte jamais le partage réseau |

Les 96 entrées actuelles sont toutes `public` : le corpus ne contient à ce jour aucun document réel, assaini ou non.

## Traçabilité

Le validateur, `tools/CorpusGenerator/manifest.py`, vérifie pour chaque entrée que le fichier déclaré existe et que son `sha256` correspond. `configuration_sha256` fixe l'empreinte du `corpus-generator.toml` utilisé, seed globale comprise : à code, configuration et versions de dépendances identiques, une régénération redonne les mêmes fichiers, octet pour octet.

`pii_injectees` enregistre les valeurs personnelles injectées dans le document, nom, IBAN, e-mail. C'est la vérité terrain qui permettra de mesurer le rappel du masquage PII sans détecteur dédié, une fois ce chantier ouvert.

`texte_reference` porte le texte natif du document au moment de son rendu, avant qu'une éventuelle numérisation ne l'écrase avec sa version dégradée. C'est la vérité terrain du comparatif OCR : le générateur connaît par construction le contenu exact de chaque document, aucune extraction séparée n'est nécessaire pour l'obtenir.

Le split `dev` / `test` est stratifié par type : dans chaque bloc de quatre documents d'un même type, un part au réglage. Chaque type est donc présent dans `dev`, y compris les types à faible effectif, et un document ne change jamais de camp d'une exécution à l'autre. Tout le réglage de prompt et d'OCR se fait sur `dev`, seul jeu qu'on s'autorise à ouvrir document par document ; `test` ne s'ouvre qu'aux mesures.

## Composition actuelle

Le tableau qui suit est réécrit par le générateur à chaque exécution, à partir du manifest lui-même. Le modifier à la main n'a pas de sens : il sera remplacé à la prochaine génération.

<!-- composition:debut -->

| Type | Documents | `dev` | `test` | Texte natif | Numérisés | Rôle |
|---|---|---|---|---|---|---|
| `carte_visite` | 12 | 3 | 9 | 2 | 10 | liste métier |
| `devis` | 12 | 2 | 10 | 8 | 4 | distracteur |
| `facture` | 12 | 4 | 8 | 8 | 4 | distracteur |
| `feuille_emar` | 15 | 4 | 11 | 3 | 12 | liste métier |
| `offre_emploi` | 15 | 4 | 11 | 12 | 3 | liste métier |
| `rib` | 8 | 2 | 6 | 6 | 2 | liste métier |
| `rib_sepa` | 8 | 3 | 5 | 6 | 2 | liste métier |
| `sepa` | 8 | 1 | 7 | 6 | 2 | liste métier |

**90 documents** en 8 types, pour 76 contenus distincts : 14 documents sont le jumeau scanné d'un autre, même contenu et même jeu, reliés par `derive_de`.

<!-- composition:fin -->

Huit types de la liste métier, la priorité 2 et `EVAL_FORM` de la priorité 1, ne sont pas encore produits.

Les documents numérisés le sont en scan, photocopie, fax ou photo au smartphone. Les deux régimes coexistent volontairement : c'est l'écart entre eux qui mesure ce que coûte l'OCR, et les jumeaux le mesurent à contenu strictement identique.

La part de documents numérisés et les profils applicables se déclarent type par type dans `tools/CorpusGenerator/corpus-generator.toml`, avec la part de pages posées de travers et l'effectif de chaque type. Une carte de visite arrive donc presque toujours numérisée et jamais faxée, une facture reste majoritairement à texte natif : ce sont des décisions de corpus, pas des propriétés du code. Le détail des réglages est dans le README de l'outil.

## Reproduire le corpus

```bash
cd tools/CorpusGenerator
docker compose run --rm generator
docker compose run --rm --entrypoint python generator manifest.py
```

Détail de l'exécution, avec et sans Docker : `tools/CorpusGenerator/README.md`.
