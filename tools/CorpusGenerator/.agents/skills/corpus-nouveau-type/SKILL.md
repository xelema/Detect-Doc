---
name: corpus-nouveau-type
description: Ajoute un type de document au CorpusGenerator, depuis des exemples PDF ou images déposés sous exemples/ ou depuis une simple description. Écrit le module, les templates, la configuration et les contrôles, puis explique comment générer. À déclencher quand la demande porte sur un nouveau type ou template de document, sur des exemples à faire entrer au corpus, ou sur N variantes d'un document.
argument-hint: "<code> [en N variantes]"
---

# Ajouter un type de document au corpus

> **Ce fichier existe en double**, à l'identique, sous `.claude/skills/` et sous `.agents/skills/` : les deux emplacements que se partagent Claude Code, Codex et OpenCode. Toute modification de l'un se reporte dans l'autre.

Lire `AJOUTER-UN-TYPE.md`, à la racine du CorpusGenerator, et l'appliquer : la procédure y vit en entier, ce fichier ne fait que la déclencher.

Déduire d'abord, demander ensuite : la présence de `exemples/<code>/` tranche l'entrée, le code et le libellé se prennent dans la documentation des types du projet ou se proposent, une mise en page par défaut. Ne poser en une fois que les questions restées ouvertes.

Deux garde-fous. **Écrire, ne pas exécuter** : ni génération, ni validateur, ni Docker. **Terminer par le compte rendu de l'étape 8** : aperçu, réglages, génération du type seul puis du corpus, suite attendue.
