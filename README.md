# Detect-Doc

POC de détection de type documentaire, service qui reçoit un document et une liste de types propre à un client, puis renvoie le type le plus probable et un niveau de confiance, y compris quand ce score est faible. Objectif : réduire l'effort de qualification documentaire au dépôt en GED. 

## Documentation et contexte

## Vue d'ensemble

La chaîne cible est : OCR du document, masquage des PII, puis classification par un modèle de langage (Mistral via Flowise) qui renvoie le type et son score. Elle sera construite brique par brique, chacune mesurée sur le même corpus de test.

Seule la première brique existe à ce jour : le générateur de corpus, qui fabrique les documents de test et leur vérité terrain. L'API de classification, l'intégration OCR et le masquage PII n'existent pas encore.

## Arborescence

```
Detect-Doc/
├── corpus/
│   ├── manifest.jsonl      livrable versionné : une ligne JSON par document, sa vérité terrain
│   └── raw/                documents produits par le générateur, non versionnés
├── docs/
│   ├── types-documentaires.md     une fiche par type documentaire ciblé
│   ├── corpus.md                  source, licence, traçabilité, sensibilité du corpus
│   ├── protocole-evaluation.md    métriques et conditions d'exécution de l'évaluation
│   └── modele-rapport.md          template du rapport final, avec des chiffres fictifs
└── tools/
    └── CorpusGenerator/    l'outil qui fabrique le corpus à partir du manifest et d'une seed
```

`corpus/raw/` n'est pas versionné : le manifest et la seed de génération suffisent à refabriquer les mêmes documents, octet pour octet. Seul `corpus/manifest.jsonl` est le livrable.

## `docs/`

- **`types-documentaires.md`** : une fiche par type documentaire du périmètre, définition, marqueurs, règles de désambiguïsation face aux types voisins, observations tirées des exemples réels dépouillés quand il en existe, et spécification de génération. Pilote l'écriture de chaque type dans `tools/CorpusGenerator/documents/`.
- **`corpus.md`** : source, licence, traçabilité et sensibilité du corpus, avec sa composition actuelle.
- **`protocole-evaluation.md`** : métriques retenues, taille d'échantillon, conditions d'exécution à figer pour comparer deux mesures.
- **`modele-rapport.md`** : template du rapport final, avec des chiffres fictifs qui illustrent le format.

## `tools/CorpusGenerator`

Fabrique les PDF du corpus et le manifest qui les décrit. Il tourne sous **Docker**.

```bash
# depuis tools/CorpusGenerator
docker compose build
docker compose run --rm generator
```

Prérequis système, liste des polices, détail de la configuration TOML, schémas d'architecture et méthode pour ajouter un type documentaire : voir `tools/CorpusGenerator/README.md`.
