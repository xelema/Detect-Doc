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

Le validateur, `tools/CorpusGenerator/manifest.py`, vérifie pour chaque entrée que le fichier déclaré existe et que son `sha256` correspond. `configuration_sha256` fixe l'empreinte du `corpus-generator.toml` utilisé : à seed, code, configuration et versions de dépendances identiques, une régénération redonne les mêmes fichiers, octet pour octet.

`pii_injectees` enregistre les valeurs personnelles injectées dans le document, nom, IBAN, e-mail. C'est la vérité terrain qui permettra de mesurer le rappel du masquage PII sans détecteur dédié, une fois ce chantier ouvert.

Le split `dev` / `test` suit une règle arithmétique sur la seed, pas un tirage aléatoire : un document ne change jamais de camp d'une exécution à l'autre. Tout le réglage de prompt et d'OCR se fait sur `dev`, `test` ne s'ouvre qu'aux mesures.

## Composition actuelle

96 documents au 30/07/2026, huit types, 10 en `dev` et 86 en `test` :

| Type | Documents | Dans la liste métier |
|---|---|---|
| `carte_visite` | 10 | oui, priorité 1 |
| `feuille_emar` | 10 | oui, priorité 1 |
| `offre_emploi` | 15 | oui, priorité 1 |
| `rib` | 7 | oui, priorité 1 |
| `sepa` | 7 | oui, priorité 1 |
| `rib_sepa` | 7 | oui, priorité 1 |
| `facture` | 20 | non, document distracteur |
| `devis` | 20 | non, document distracteur |

Huit types de la liste métier, la priorité 2 et `EVAL_FORM` de la priorité 1, ne sont pas encore produits.

Régime de fichier : 50 documents à texte natif, 46 numérisés, scan, photocopie, fax ou photo au smartphone. Les deux régimes coexistent volontairement : c'est l'écart entre les deux qui mesure ce que coûte l'OCR.

## Reproduire le corpus

```bash
cd tools/CorpusGenerator
docker compose run --rm generator
docker compose run --rm --entrypoint python generator manifest.py
```

Détail de l'exécution, avec et sans Docker : `tools/CorpusGenerator/README.md`.
