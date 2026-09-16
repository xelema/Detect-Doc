# Ajouter un type de document au corpus

Chaîne complète, depuis un document reçu jusqu'à un type prêt à générer. Elle a deux entrées possibles et une seule sortie.

| Entrée | Ce dont on part |
|---|---|
| **exemples réels** | un ou plusieurs PDF ou images d'un type qui existe, déposés hors dépôt |
| **description seule** | une phrase décrivant un document qui n'existe pas encore, pour une démonstration ou un essai |

Dans les deux cas, la sortie est la même : une fiche de type, un module Python, un ou plusieurs templates, quatre inscriptions, des contrôles, et un compte rendu qui explique à l'utilisateur comment générer. Le reste de ce fichier décrit les huit étapes qui y mènent. Le [README](README.md) reste la référence sur le fonctionnement du générateur ; ce guide ne décrit que l'ajout d'un type.

## Mode d'emploi

`<CODE>` désigne le nom du type de documents voulu.

1. **Déposer les exemples** dans `exemples/<CODE>/`, un dossier par type, à la racine de `CorpusGenerator`, ignoré par Git. Sans exemple, sauter cette ligne et décrire le document voulu.
2. **Lancer l'agent** : `/corpus-nouveau-type ajoute le type <CODE> à partir de exemples/<CODE>/`. Si l'agent ne charge pas les skills, lui dire de lire ce fichier et de l'appliquer, ce qui suffit. Ajouter « en N variantes » si une seule mise en page ne convient pas.
3. **Régler les effectifs** dans `corpus-generator.toml`, bloc `[types.<nom>]` : `count` pour le nombre de documents, `scanned_share` pour la part numérisée, `profiles` pour les défauts qui peuvent lui arriver.
4. **Générer** : `docker compose build && docker compose run --rm generator`.
5. **Contrôler** : `docker compose run --rm --entrypoint python generator manifest.py`, puis la même commande sur `verifier.py`. Les deux doivent rendre zéro anomalie.

## Conduire le travail avec un agent

Ce guide est la procédure, quel que soit l'assistant. Le skill `corpus-nouveau-type` ne fait que le déclencher, et il est posé dans les deux dossiers que se partagent les trois outils, à l'identique.

| Fichier | Lu par |
|---|---|
| `.claude/skills/corpus-nouveau-type/SKILL.md` | Claude Code, OpenCode |
| `.agents/skills/corpus-nouveau-type/SKILL.md` | Codex, OpenCode |
| `AGENTS.md`, recopié en `CLAUDE.md` | les trois, à chaque session, sans déclencheur |

Codex balaie `.agents/skills` de son dossier courant jusqu'à la racine du dépôt, et Claude Code comme OpenCode lisent le `.claude/skills` du dossier depuis lequel ils sont lancés : les deux dossiers vivent donc ici, à la racine du CorpusGenerator, et non à celle du dépôt. Le `name` du frontmatter doit rester en minuscules à tirets et la `description` sous 1024 caractères, contraintes qu'OpenCode fait respecter. Pour tout autre assistant, pointer ce guide revient au même.

Trois points décident de tout le travail et ne se devinent pas. Les demander en une seule fois, puis avancer.

| À savoir | Défaut si rien n'est dit |
|---|---|
| l'entrée : le dossier d'exemples, ou la description du document voulu | demander, rien ne le remplace |
| le code du type et son libellé | le prendre dans la documentation des types du projet si le type y figure, sinon en proposer un et le faire valider |
| le nombre de mises en page | **une seule**, qui suit l'exemple fourni |

**Le nombre de mises en page se prend au mot.** Sans consigne, produire un clone visuel de l'exemple. Si N variantes sont demandées, en produire exactement N, et franchement distinctes, au sens de l'étape 3.

**Sans exemple**, en mode démonstration, la description fait autorité : inventer une mise en page plausible du domaine de la formation professionnelle, et le dire dans la fiche du type comme dans le compte rendu. Ne jamais présenter un type inventé comme observé sur des exemples réels.

**L'agent écrit, il n'exécute pas.** Il produit le code, les templates, la configuration et les contrôles, tout fonctionnel, puis passe la main : c'est l'utilisateur qui génère, sur sa machine, et qui revient avec ce qu'il a vu. Le travail se termine donc par un compte rendu qui explique comment exécuter, comment régler, comment regarder le résultat et ce qui reste à faire, détail à l'étape 8.

Deux interdits. Ne jamais commiter les exemples fournis ni recopier une de leurs valeurs dans un fichier suivi. Ne pas commiter sans demande explicite.

## Ce que la chaîne produit

| Artefact | Fichier | Écrit par |
|---|---|---|
| fiche du type, marqueurs et confusions | documentation des types du projet | à la main |
| modèle de données, style, production | `documents/<nom>.py` | à la main |
| mise en page | `templates/<nom>_NN.html` | à la main |
| inscription au registre | `generer.py`, `manifest.py`, `corpus-generator.toml` | à la main |
| contrôles de cohérence | `verifier.py` | à la main |
| documents et manifest | le dossier de sortie déclaré par `output_dir` | `generer.py` |
| composition du corpus | `docs/corpus.md`, à côté du dossier de sortie | `generer.py` |

## Étape 1, rassembler l'entrée

Les exemples fournis vont dans `exemples/<CODE>/`, à la racine de `CorpusGenerator`, où `<CODE>` est le code du type. Le dossier est ignoré par Git et doit le rester : un exemple métier réel contient des données personnelles, et rien de ce qu'il porte ne doit entrer dans le dépôt. Les exemples dépouillés avant l'écriture de ce guide sont restés dans `tmp/samples/` à la racine du dépôt, emplacement que citent les fiches déjà écrites.

Un exemple se regarde page par page, pas seulement en texte extrait : la mise en page est la moitié de l'information, et un exemple numérisé n'a aucune couche texte à extraire.

Sans exemple, l'entrée est la description. Elle doit au minimum répondre à ce que l'étape 3 demande : ce que le document atteste, qui l'émet, quels blocs le composent, et par quoi on le reconnaît d'un coup d'œil.

## Étape 2, écrire la fiche du type

La fiche va dans la documentation des types du projet, `Detect-Doc/docs/types-documentaires.md` dans ce dépôt, au format des fiches déjà écrites. Elle porte six rubriques : définition, tableau des marqueurs avec leur force, tableau des confusions et de ce qui tranche, règle opérationnelle, ce que montrent les exemples, ce qu'il faut générer.

Deux règles.

**La fiche est anonyme.** Elle décrit des formes, jamais des valeurs : « bloc adresse de l'employeur », jamais le nom lu sur l'exemple. Aucune raison sociale, aucun nom de personne, aucun identifiant relevé sur un document réel n'entre dans le dépôt, ni dans la fiche, ni dans un commentaire de code, ni dans un template.

**La fiche décide de ce que le générateur produit.** Un marqueur qu'elle donne pour fort doit être présent sur presque tous les documents générés, un marqueur faible sur une minorité. C'est le seul lien entre l'analyse et le code, et c'est ce qui empêche de produire un corpus qui mesure autre chose que ce que la fiche annonce.

Si le nouveau type a un voisin confondable déjà présent, la règle qui les sépare s'écrit dans la section de leur groupe, pas dans les deux fiches : c'est là que se joue la classification.

## Étape 3, les décisions de conception

Six questions, toutes à trancher avant d'écrire une ligne de code.

**1. Le code et le nom.** Le code du type est celui de la liste métier, `FEUILLE_EMAR` par exemple. Le nom technique en est la version en minuscules avec tirets bas, `feuille_emar` : c'est la valeur de `type_attendu`, la clé du bloc TOML et l'entrée de `manifest.TYPES_CONNUS`, les trois doivent coïncider. Le préfixe des identifiants de fichiers fait trois lettres, `fem-0001`, et ne doit être pris par aucun type existant.

**2. La nature du type**, qui décide de tout le reste de la chaîne.

| Nature | Quand | Ce que `generer.py` en fait |
|---|---|---|
| classique | le document circule comme fichier bureautique, et sa version numérisée coexiste avec l'original | une part des unités se dédouble en jumeau numérisé, relié par `derive_de`, part plafonnée à 50 % |
| `physique = True` | le document est la reprise d'un objet, carton, feuille signée à la main | le rendu propre est écrasé par sa numérisation, une seule entrée par unité, `rotated_share` applicable |
| `autonome = True` | `produire` tranche lui-même le régime final, sans reprise d'objet, comme une capture d'écran | l'entrée rendue est définitive, aucune table `profiles` n'est acceptée |

**3. Le nombre de mises en page.** Par défaut, une seule, qui suit l'exemple fourni. Plusieurs variantes se justifient quand la diversité de structure est une caractéristique du type lui-même, comme les cinq familles de feuille d'émargement observées sur les exemples. Des variantes demandées doivent être **franchement distinctes** : structure de page, ordre des blocs, présence ou absence de blocs, vocabulaire des intitulés. Trois déclinaisons de la même grille n'apprennent rien à un classifieur, elles lui font surapprendre une mise en page.

Le template est choisi en tournant sur la seed, `TEMPLATES[(seed - 1) % len(TEMPLATES)]`, jamais tiré au hasard : sur un effectif modeste, un tirage laisse une mise en page sous-représentée.

**4. Les données et leurs clés de contrôle.** Tout identifiant à clé de contrôle doit la porter juste, via `cles.py`, sinon le corpus est inexploitable pour mesurer le masquage et se repère immédiatement comme factice. Ce qui est utile à au moins deux types va dans `documents/commun.py`, jamais un axe propre à un seul.

**5. Les PII et les tags.** `pii_injectees` est la vérité terrain des données personnelles réellement présentes dans le document : une donnée listée doit s'y trouver, et une donnée présente ne doit pas manquer. Les tags servent à mesurer plus tard le poids d'un axe, mise en page, régime, bloc absent, cas volontairement difficile.

**6. Les effectifs.** `count`, `scanned_share`, `rotated_share` et `profiles`, dont seuls les écarts au défaut s'écrivent dans le bloc TOML. Un effectif multiple du nombre de mises en page les couvre toutes également.

## Étape 4, écrire le module

Un fichier par type sous `documents/`, sur le patron des existants. `documents/rib.py` est le plus simple, `documents/feuille_emargement.py` le plus complet.

```python
"""Une phrase sur ce qu'est ce document, et pourquoi il est fabriqué comme ça."""

from dataclasses import dataclass

import manifest
from configuration import flux
from documents.base import DocumentType
from documents.commun import COULEURS, POLICES_SANS, faker, generer_organisme
from rendu import logo_svg, rendre_pdf, texte_natif

TYPE_ATTENDU = "mon_type"

TEMPLATES = ["mon_type_01.html"]


@dataclass
class MonType:
    """Le modèle de données du document."""
    ...


@dataclass
class StyleMonType:
    """Ce qui varie dans la présentation. Sérialisé tel quel dans le manifest."""
    template: str
    police: str
    corps_pt: float
    couleur: str

    def as_dict(self) -> dict:
        return asdict(self)


def generer_mon_type(seed: int) -> MonType:
    fake = faker(seed)
    ...


def style_mon_type(seed: int) -> StyleMonType:
    tirage = flux("mon-type", seed)
    return StyleMonType(
        template=TEMPLATES[(seed - 1) % len(TEMPLATES)],
        police=tirage.choice(POLICES_SANS),
        corps_pt=tirage.choice([9.0, 9.5, 10.0]),
        couleur=tirage.choice(COULEURS),
    )


class MonTypeDocument(DocumentType):
    type_attendu = TYPE_ATTENDU

    def produire(self, numero: int) -> dict:
        document = generer_mon_type(numero)
        style = style_mon_type(numero)
        identifiant = f"mtp-{numero:04d}"
        chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"

        pages = rendre_pdf(style.template, chemin, d=document, s=style,
                           logo=logo_svg(document.organisme.raison_sociale, style.couleur))

        return manifest.construire_entree(
            identifiant, chemin, self.type_attendu, numero, pages,
            variante=style.as_dict(),
            pii={"nom": document.nom},
            tags=[style.template.removesuffix(".html")],
            texte_reference=texte_natif(chemin),
        )


DOCUMENT = MonTypeDocument()
```

Les règles que le rendu impose, toutes déjà éprouvées par les types existants.

**Chaque facette tire dans son propre flux nommé.** `flux("mon-type", seed)` pour le style, un autre nom pour le contenu, un autre pour un axe indépendant. Deux facettes qui partagent un flux ou un modulo finissent par s'aligner, et un axe entier n'est alors jamais exercé dans le jeu de réglage.

**La génération ne dépend que de la seed et d'arithmétique entière.** Ni horloge, ni fuseau, ni locale, ni parcours d'un ensemble non trié. `Faker.date_between` est proscrite : elle convertit en heure locale et ne redonne pas le même résultat d'une machine à l'autre. Les dates se calculent en jours ajoutés à `DEBUT_PERIODE`.

**Une signature manuscrite est un tracé, jamais du texte.** `rendu.paraphe_svg` produit une courbe. Une signature écrite dans une police cursive reste du texte, et un copier-coller sur le PDF livrerait le nom du signataire, ce qu'aucun document signé à la main ne fait. Les annotations portées au stylo, elles, restent du texte : c'est ce que l'OCR doit apprendre à lire.

**Toute police employée doit être installée**, déclarée dans une liste de `documents/`, et couvrir les capitales accentuées. Une police absente est remplacée en silence par la police de secours, et le corpus sort avec bien moins de variété que le manifest n'en annonce. Une police ajoutée doit l'être aussi au `Dockerfile` et à la commande d'installation du README.

**Ce que le manifest déclare doit correspondre au document.** Un filigrane annoncé doit être lisible, une PII listée doit être présente, un nombre de pages annoncé doit être celui rendu.

Le template suit les conventions des existants : `@page { size: A4; margin: ... }`, la police et le corps pris sur le style, aucune ressource externe, et `Markup` pour tout SVG injecté, l'auto-échappement de Jinja2 imprimant sinon le source en clair. Un type dont le support compte autant que le contenu, comme la carte de visite posée sur une feuille, se découpe en un template de support dont héritent les mises en page.

## Étape 5, inscrire le type

Quatre endroits, et la génération échoue bruyamment si l'un manque.

1. `generer.py` : importer `DOCUMENT` et l'ajouter à la liste de `TYPES`.
2. `manifest.py` : ajouter le nom à `TYPES_CONNUS`. La liste est volontairement séparée du dossier `documents/`, que le validateur du manifest ne doit pas importer.
3. `corpus-generator.toml` : ouvrir un bloc `[types.<nom>]`, vide s'il prend tous les défauts, avec un commentaire sur ce qui le distingue s'il en pose.
4. `verifier.py` : ajouter les contrôles de l'étape 6.

## Étape 6, les contrôles

`verifier.py` reçoit ce que la relecture d'un rendu ne peut pas voir. Pour un nouveau type, au minimum :

- la cohérence interne du modèle de données, dates ordonnées, totaux qui tombent juste, cardinalités qui correspondent au style ;
- les clés de contrôle de chaque identifiant produit, via `stdnum` ;
- la reproductibilité, deux rendus du même document donnant le même hash, contrôle qui casse sans lever la moindre erreur ;
- le nombre de pages, s'il est décidé par le style ;
- l'absence de paraphe extractible comme texte, si le document est signé ;
- les caractères produits, à ajouter à `_caracteres_produits` pour que le contrôle des polices couvre le nouveau vocabulaire.

## Étape 7, ce qui reste à écrire

- la prose de la documentation du corpus si le type change ce qu'elle décrit ; le tableau de composition, lui, est réécrit par la génération ;
- une section du [README](README.md) si le type porte une particularité de conception, à la façon de la feuille d'émargement ou de l'offre d'emploi, et les schémas Mermaid si la carte des modules change ;
- le `Dockerfile` pour toute police ou tout paquet système ajouté, et la commande d'installation du README qui doit rester alignée sur lui : une police déclarée dans `documents/` mais absente de l'image sort remplacée en silence par la police de secours.

Le dépôt qui héberge l'outil peut demander plus, une documentation de projet à tenir à jour par exemple. Ses consignes disent lesquelles.

## Étape 8, passer la main

Le travail s'arrête ici : tout est écrit, rien n'a été exécuté. C'est l'utilisateur qui lance la génération, sur sa machine et sous son Docker, puis revient avec ce qu'il a vu. Le compte rendu de fin doit donc lui donner de quoi le faire sans rien chercher.

**Ce qu'il contient.**

1. **Ce qui a été ajouté** : le type, son code, sa nature, le nombre de mises en page, les fichiers créés et modifiés.
2. **Comment regarder un document sans tout régénérer**, la commande ci-dessous, à recopier telle quelle avec le vrai nom du module.
3. **Comment régler le corpus** : le bloc `[types.<nom>]` écrit dans le TOML, ce que chaque réglage change, et lesquels valent la peine d'être touchés en premier.
4. **Comment générer et contrôler** : `docker compose build`, `docker compose run --rm generator`, puis `manifest.py` et `verifier.py` par `--entrypoint python`. Les deux doivent rendre zéro anomalie.
5. **La suite** : ce sur quoi un retour est attendu, la mise en page tient-elle la page et ressemble-t-elle à l'exemple, puis ce qui viendra ensuite, régler les effectifs, régénérer le corpus complet, passer le type en revue.

**L'aperçu à donner.** Régénérer le corpus entier pour juger d'une mise en page coûte plusieurs minutes pour rien. Un rendu isolé suffit, et s'écrit dans `apercu/`, dossier de travail ignoré par Git, monté le temps de la commande :

```bash
mkdir -p apercu
docker compose build
docker compose run --rm -T -v "$(pwd)/apercu:/apercu" --entrypoint python generator - <<'PY'
from pathlib import Path

import fitz

from documents.mon_type import generer_mon_type, style_mon_type
from rendu import logo_svg, rendre_pdf

for seed in range(1, 4):
    document, style = generer_mon_type(seed), style_mon_type(seed)
    chemin = Path(f"/apercu/apercu-{seed}.pdf")
    pages = rendre_pdf(style.template, chemin, d=document, s=style,
                       logo=logo_svg(document.organisme.raison_sociale, style.couleur))
    # PyMuPDF plutôt que pdftoppm : il est déjà une dépendance du générateur, là où
    # poppler ne serait installé dans l'image que pour cet aperçu.
    with fitz.open(chemin) as pdf:
        for numero, page in enumerate(pdf, start=1):
            page.get_pixmap(dpi=110).save(f"/apercu/apercu-{seed}-{numero}.png")
    print(seed, style.template, pages, "page(s)")
PY
```

`-T` coupe l'allocation d'un terminal, sans quoi le script passé sur l'entrée standard n'arriverait pas jusqu'à Python. `docker compose build` est à relancer après chaque correction, le code étant copié dans l'image : seules les dernières couches se reconstruisent, c'est l'affaire de quelques secondes.

Les PNG produits sont ce sur quoi la correction se joue. Un débordement de page, un tableau qui casse, un bloc qui se chevauche : ni le manifest ni les contrôles ne les signalent, seul un œil sur l'image les voit. L'utilisateur les regarde ou les redonne à l'agent, qui corrige, et la boucle recommence jusqu'à ce que la page tienne debout.

**Un raccourci utile à signaler** : `enabled = false` sous `[degradation]` supprime toute rastérisation et accélère franchement une génération complète, le temps d'itérer sur les templates. À remettre à `true` avant la génération finale, sans quoi le corpus sort sans un seul document numérisé.

## Les pièges

| Symptôme | Cause |
|---|---|
| deux exécutions donnent des fichiers différents | une horloge, une locale, un ensemble non trié, ou une facette qui tire hors flux nommé |
| le corpus a bien moins de variété que le manifest n'annonce | une police déclarée mais non installée, servie en silence par la police de secours |
| un `É` d'un autre corps au milieu d'un mot | une police qui ne dessine pas les capitales accentuées |
| le nom d'un signataire ressort d'un copier-coller | une signature écrite en police cursive au lieu d'un tracé `paraphe_svg` |
| un SVG s'imprime en clair dans le document | un fragment injecté sans `Markup` |
| la génération refuse un réglage du TOML | un réglage sans effet sur la nature du type, `rotated_share` sur un type non physique, `profiles` sur un type autonome, `scanned_share` au-delà de 50 sur un type qui garde son original |
| le validateur signale un type inconnu | `manifest.TYPES_CONNUS` n'a pas été mis à jour |
| un classifieur excellent sur ce type et mauvais en production | des variantes trop proches, ou un marqueur que la fiche donne pour instable et que le générateur pose systématiquement |
