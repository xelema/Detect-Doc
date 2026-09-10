---
name: corpus-nouveau-type
description: Ajoute un type de document au CorpusGenerator, depuis un exemple PDF ou image fourni, ou depuis une simple description pour un document qui n'existe pas encore. Écrit le module, les templates, la configuration et les contrôles, puis explique à l'utilisateur comment générer. Déclencheurs, sur le CorpusGenerator, quand la demande parle d'ajouter, créer ou générer un type ou un template de document (feuille d'émargement, attestation, convention, certificat, CERFA, facture, ou n'importe quel autre), de faire entrer un exemple au corpus, ou de produire N variantes d'un document à partir d'un modèle.
---

# Ajouter un type de document au corpus

> **Ce fichier existe en double**, à l'identique, sous `.claude/skills/` et sous `.agents/skills/` : les deux emplacements que se partagent Claude Code, Codex et OpenCode. Toute modification de l'un se reporte dans l'autre.

Lire `AJOUTER-UN-TYPE.md`, à la racine du CorpusGenerator, et l'appliquer. La procédure y vit en entier : ce qu'il faut demander avant d'écrire, les huit étapes, le compte rendu de fin, les pièges. Ce fichier ne fait que la déclencher, il n'en duplique rien.

Deux règles qui gouvernent tout le reste, rappelées ici parce qu'elles décident de la façon de travailler.

**Écrire, ne pas exécuter.** Produire le code, les templates, la configuration et les contrôles, tout fonctionnel, et s'arrêter là. Ne lancer ni la génération, ni le validateur, ni Docker.

**Terminer par un compte rendu** qui rend l'exécution possible sans rien chercher, au format de l'étape 8 du guide : ce qui a été ajouté, comment régler, comment regarder un document, comment générer et contrôler, ce qui reste à faire et sur quoi un retour est attendu.
