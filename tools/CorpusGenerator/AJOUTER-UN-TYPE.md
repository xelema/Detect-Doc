# Ajouter un type de document au corpus

Chaîne complète, depuis un document reçu jusqu'à un type prêt à générer. Elle a deux entrées possibles et une seule sortie.

| Entrée | Ce dont on part |
|---|---|
| **exemples réels** | un ou plusieurs PDF ou images d'un type qui existe, déposés hors dépôt |
| **description seule** | une phrase décrivant un document qui n'existe pas encore, pour une démonstration ou un essai |

Dans les deux cas, la sortie est la même : une fiche de type, un module Python, un ou plusieurs templates, quatre inscriptions, des contrôles, et un compte rendu qui explique à l'utilisateur comment générer. Le [README](README.md) reste la référence sur le fonctionnement du générateur ; ce guide ne décrit que l'ajout d'un type.

## Mode d'emploi

`<CODE>` désigne le code métier du type voulu.

1. **Déposer les exemples** dans `exemples/<CODE>/`, un dossier par type, ignoré par Git. Sans exemple, sauter cette ligne.
2. **Lancer l'agent** : `/corpus-nouveau-type <CODE>`. Ajouter « en N variantes » si une seule mise en page ne convient pas. Si l'agent ne charge pas le skill, lui dire de lire ce fichier et de l'appliquer, ce qui suffit. Pour un utilisateur qui n'exécute rien lui-même, `/corpus-nouveau-type-auto <CODE>` : l'agent mène aussi le build, l'aperçu et la génération.
3. **Régler les effectifs** dans `corpus-generator.toml`, bloc `[types.<nom>]` : `count` pour le nombre de documents, `scanned_share` pour la part numérisée, `profiles` pour les défauts qui peuvent lui arriver.
4. **Générer** : `docker compose build && docker compose run --rm generator`.
5. **Contrôler** : `docker compose run --rm --entrypoint python generator manifest.py`, puis la même commande sur `verifier.py`. Les deux doivent rendre zéro anomalie.

## Conduire le travail avec un agent

Ce guide est la procédure, quel que soit l'assistant. Deux skills le déclenchent, posés à l'identique sous `.claude/skills/` et sous `.agents/skills/`, à la racine du CorpusGenerator où Claude Code, Codex et OpenCode les cherchent : `corpus-nouveau-type` pour écrire sans exécuter, `corpus-nouveau-type-auto` pour mener aussi l'exécution à la place d'un utilisateur qui ne le peut pas. Le `name` du frontmatter reste en minuscules à tirets et la `description` sous 1024 caractères, contraintes qu'OpenCode fait respecter.

**Déduire d'abord, demander ensuite.** L'agent rassemble seul ce qui se lit dans le dépôt, et ne pose en une seule fois que les questions restées ouvertes.

| À savoir | Comment le déduire | Quand demander |
|---|---|---|
| l'entrée | `exemples/<CODE>/` existe : exemples réels ; sinon la description portée par la demande | ni dossier ni description exploitable |
| le code du type et son libellé | le type figure dans la documentation des types du projet | sinon en proposer un et le faire valider |
| le nombre de mises en page | « en N variantes » dans la demande | jamais : une seule par défaut, qui suit l'exemple fourni |

**Le nombre de mises en page se prend au mot.** Sans consigne, produire un clone visuel de l'exemple. Si N variantes sont demandées, en produire exactement N, et franchement distinctes, au sens de l'étape 3.

**Sans exemple**, en mode démonstration, la description fait autorité : inventer une mise en page plausible du domaine de la formation professionnelle, et le dire dans la fiche du type comme dans le compte rendu. Ne jamais présenter un type inventé comme observé sur des exemples réels.

**L'agent écrit, il n'exécute pas.** Il produit le code, les templates, la configuration et les contrôles, tout fonctionnel, puis passe la main : c'est l'utilisateur qui génère, sur sa machine, et qui revient avec ce qu'il a vu. Le travail se termine donc par le compte rendu de l'étape 8. Sauf sous `corpus-nouveau-type-auto`, pensé pour un utilisateur qui ne peut rien exécuter : l'agent y mène le build, l'aperçu et la génération, et le compte rendu se résume au dossier des documents produits.

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

Les exemples fournis vont dans `exemples/<CODE>/`, ignoré par Git et qui doit le rester : un exemple métier réel contient des données personnelles, et rien de ce qu'il porte ne doit entrer dans le dépôt.

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
2. **Comment regarder le rendu, générer ce type seul et générer le corpus entier** : les trois commandes ci-dessous, recopiées avec le vrai nom du type.
3. **Comment régler le corpus** : le bloc `[types.<nom>]` écrit dans le TOML, ce que chaque réglage change, et lesquels valent la peine d'être touchés en premier.
4. **La suite** : ce sur quoi un retour est attendu, la mise en page tient-elle la page et ressemble-t-elle à l'exemple, puis ce qui viendra ensuite, régler les effectifs, régénérer le corpus complet, passer le type en revue.

**Les trois commandes.** `apercu.py` produit dans `apercu/`, dossier de travail ignoré par Git, des PDF et un PNG par page. Le montage de `$(pwd)` sur `/app` rend les corrections de templates et de code visibles sans rebuild : seule une nouvelle police ou dépendance impose un `docker compose build`.

```bash
# Regarder le rendu : trois documents propres, PDF + PNG page par page.
docker compose run --rm -v "$(pwd):/app" --entrypoint python generator apercu.py <nom>

# Générer ce type seul comme le corpus le produirait : à l'effectif du TOML,
# jumeaux numérisés ou reprises compris, entrées dans apercu/manifest.jsonl.
docker compose run --rm -v "$(pwd):/app" --entrypoint python generator apercu.py <nom> --complet

# Générer le corpus entier, puis le contrôler : zéro anomalie attendu.
docker compose build && docker compose run --rm generator
docker compose run --rm --entrypoint python generator manifest.py
docker compose run --rm --entrypoint python generator verifier.py
```

Sous le venv WSL, `.venv/bin/python apercu.py <nom>` fait la même chose sans conteneur.

Les PNG sont ce sur quoi la correction se joue. Un débordement de page, un tableau qui casse, un bloc qui se chevauche : ni le manifest ni les contrôles ne les signalent, seul un œil sur l'image les voit. L'utilisateur les regarde ou les redonne à l'agent, qui corrige, et la boucle recommence jusqu'à ce que la page tienne debout.

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
