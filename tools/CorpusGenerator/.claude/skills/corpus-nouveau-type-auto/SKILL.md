---
name: corpus-nouveau-type-auto
description: Ajoute un type de document au CorpusGenerator puis exécute toute la chaîne à la place de l'utilisateur : build Docker, boucle d'aperçu, génération des PDF. À déclencher quand l'utilisateur ne peut ou ne veut rien exécuter lui-même, ou quand la demande couvre aussi la production des documents et pas seulement le code. Pour écrire le type sans exécuter, corpus-nouveau-type suffit.
argument-hint: "<code> [en N variantes]"
---

# Ajouter un type de document, exécution comprise

> **Ce fichier existe en double**, à l'identique, sous `.claude/skills/` et sous `.agents/skills/` : les deux emplacements que se partagent Claude Code, Codex et OpenCode. Toute modification de l'un se reporte dans l'autre.

Même procédure que `corpus-nouveau-type`, mais pour un utilisateur qui n'exécute rien : l'agent mène aussi le build, la boucle d'aperçu et la génération. Lire `AJOUTER-UN-TYPE.md`, à la racine du CorpusGenerator, et l'appliquer en entier — entrée, fiche, module, templates, inscriptions, contrôles — aux différences près que voici.

**Le build part dès le début.** Lancer `docker compose build` en tâche de fond ou dans un second shell, sans attendre sa fin pour écrire : l'image ne porte que les dépendances et les polices, le code entre dans le conteneur par le montage `$(pwd):/app`. Attendre la fin du build avant le premier aperçu, et ne le relancer que si le `Dockerfile` ou les polices ont changé.

**La boucle d'aperçu est autonome.** `docker compose run --rm -v "$(pwd):/app" --entrypoint python generator apercu.py <nom>` écrit PDF et PNG sous `apercu/` : lire les PNG soi-même, corriger template ou module, relancer, jusqu'à ce que la page tienne. Puis `verifier.py` par le même patron, et corriger ce qui remonte.

**La quantité se demande une fois le rendu validé** : « combien de documents voulez-vous générer ? ». Reporter la réponse dans `count` du bloc `[types.<nom>]`, puis produire la tranche complète du type, numérisations comprises : `docker compose run --rm -v "$(pwd):/app" --entrypoint python generator apercu.py <nom> --complet`.

**Le dernier message nomme le dossier** : « vos N documents se trouvent dans `tools/CorpusGenerator/apercu/` », plus le bloc TOML écrit et la liste des fichiers créés.
