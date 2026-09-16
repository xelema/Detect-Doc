# CorpusGenerator, consignes aux agents

> **`AGENTS.md` et `CLAUDE.md` portent le même contenu**, le premier lu par Codex et OpenCode, le second par Claude Code. Toute modification de l'un se reporte à l'identique dans l'autre.

Cet outil fabrique des documents de test synthétiques et le manifest qui les décrit. Il s'utilise seul, sans rien connaître du projet qui l'héberge.

**Ajouter un type de document** : lire `AJOUTER-UN-TYPE.md` et l'appliquer, la procédure y vit en entier. Deux skills la déclenchent, posés à l'identique sous `.claude/skills/` et `.agents/skills/` : `corpus-nouveau-type` écrit sans exécuter, `corpus-nouveau-type-auto` mène aussi l'exécution pour un utilisateur qui ne le peut pas.

**Comprendre l'outil** : `README.md`, qui porte l'architecture, la configuration et les schémas.

**Écrire, ne pas exécuter.** L'agent produit le code, les templates, la configuration et les contrôles, tout fonctionnel, et s'arrête là. Il ne lance ni la génération, ni le validateur, ni Docker : c'est l'utilisateur qui exécute, sur sa machine, et qui revient avec ce qu'il a vu. Une seule exception, le skill `corpus-nouveau-type-auto`, où l'agent mène l'exécution de bout en bout.

**Terminer par un compte rendu** qui rend l'exécution possible sans rien chercher : ce qui a été ajouté, comment régler la configuration, la commande pour regarder un document sans tout régénérer, celle pour générer et contrôler, ce qui reste à faire et sur quoi un retour est attendu. Le détail de ce compte rendu est à l'étape 8 du guide.

**Ne jamais commiter** les exemples déposés dans `exemples/`, ni recopier une de leurs valeurs dans un fichier suivi : ils peuvent porter des données personnelles réelles.
