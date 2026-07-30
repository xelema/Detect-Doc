"""Lecture, écriture et validation du manifest du corpus.

Chaque document du corpus y porte sa source, sa licence, sa traçabilité et son statut de
sensibilité. Le manifest est versionné dans Git ; les documents eux-mêmes ne le sont pas,
puisque la seed et le template suffisent à les refabriquer à l'identique. Le hash
SHA-256 est ce qui rend cette promesse vérifiable plutôt que déclarative.

Format : JSON Lines, une ligne par document, trié par identifiant. Ce format se différencie
proprement en revue de pull request, contrairement à un JSON unique réindenté à chaque
régénération.
"""

import hashlib
import json
import random
from collections import Counter
from pathlib import Path

from configuration import (
    DOSSIER_CORPUS,
    UNITE_SUR_N_EN_DEV,
    SHA256 as CONFIGURATION_SHA256,
)

FICHIER_MANIFEST = DOSSIER_CORPUS / "manifest.jsonl"
DOSSIER_DOCUMENTS = DOSSIER_CORPUS / "raw"

# Section de la documentation du corpus réécrite depuis le manifest. Le chemin se déduit
# du dossier de sortie plutôt que d'une clé de configuration : il tombe juste aussi bien
# depuis le dépôt, où `corpus/` et `docs/` sont voisins, que dans le conteneur, où
# `/corpus` et `/docs` le sont également.
FICHIER_DOC_CORPUS = DOSSIER_CORPUS.parent / "docs" / "corpus.md"
BALISE_DEBUT = "<!-- composition:debut -->"
BALISE_FIN = "<!-- composition:fin -->"

# Types produits sans appartenir à la liste métier : ils mesurent le comportement du
# service quand aucun type de la liste ne convient.
TYPES_DISTRACTEURS = {"facture", "devis"}

# Un mot par type produit sous documents/, où chacun vit dans son propre fichier
# (documents/base.py explique le patron). Cette liste reste indépendante de ce dossier :
# manifest.py doit pouvoir valider un corpus déjà produit sans importer les
# bibliothèques de rendu (WeasyPrint, Augraphy) que documents/ entraîne avec lui.
# generer.py vérifie au démarrage que les deux listes concordent.
TYPES_CONNUS = {"facture", "devis", "rib", "sepa", "rib_sepa", "carte_visite",
                "feuille_emar", "offre_emploi"}
SENSIBILITES = {"public", "interne_assaini", "production_valide"}

# Toutes les clés écrites par construire_entree. La liste doit rester alignée avec
# elle : un champ oublié ici est un champ que le validateur laisserait disparaître du
# manifest sans rien dire, pii_injectees en tête.
CHAMPS_OBLIGATOIRES = [
    "id", "fichier", "sha256", "type_attendu", "source", "licence", "sensibilite",
    "langue", "pages", "regime", "seed", "variante", "degradations", "derive_de",
    "tags", "pii_injectees", "split", "configuration_sha256",
]

REGIMES = {"texte-natif", "scanne"}


def hash_fichier(chemin: Path) -> str:
    return hashlib.sha256(chemin.read_bytes()).hexdigest()


def _split(type_attendu: str, seed: int) -> str:
    """Répartition dev / test, scellée dès le premier document.

    Le jeu dev sert au réglage du prompt et de l'OCR, et c'est le seul qu'on s'autorise à
    ouvrir document par document ; le jeu test ne s'ouvre qu'aux jalons de mesure. Un
    type absent de dev est donc un type dont on devra régler les règles sans jamais en
    avoir lu un exemplaire.

    La répartition est stratifiée par type et par bloc d'unités consécutives : dans
    chaque bloc, une unité et une seule part en dev. Chaque type reçoit ainsi sa part
    exacte, garantie dès le premier bloc complet, là où un tirage indépendant laissait
    les types à faible effectif sans aucun document de réglage.

    La position dans le bloc reste tirée, et le flux inclut le type. Deux raisons : une
    règle purement modulaire s'alignerait sur les facettes qui tournent déjà sur le même
    modulo, les quatre fabriques de carte de visite en tête, et le jeu de réglage se
    retrouverait réduit à une seule mise en page ; et un flux commun à tous les types
    leur imposerait les mêmes rangs, ce qui est précisément ce qu'on cherche à éviter.

    Le bloc ne dépend que du rang de l'unité : allonger un type ne redistribue jamais les
    blocs déjà constitués.
    """
    bloc, rang = divmod(seed - 1, UNITE_SUR_N_EN_DEV)
    position = random.Random(f"split-{type_attendu}-{bloc}").randrange(UNITE_SUR_N_EN_DEV)
    return "dev" if rang == position else "test"


def _chemin_relatif(chemin: Path) -> str:
    return str(chemin.relative_to(DOSSIER_CORPUS)).replace("\\", "/")


def construire_entree(identifiant: str, chemin: Path, type_attendu: str, seed: int,
                      pages: int, variante: dict, pii: dict, tags: list[str]) -> dict:
    """Assemble la ligne de manifest d'un document qui vient d'être produit."""
    return {
        "id": identifiant,
        "fichier": _chemin_relatif(chemin),
        "sha256": hash_fichier(chemin),
        "type_attendu": type_attendu,
        # Documents fabriqués de toutes pièces : aucune donnée personnelle réelle,
        # aucune licence à respecter, sensibilité publique.
        "source": "genere",
        "licence": "interne",
        "sensibilite": "public",
        "langue": "fr",
        "pages": pages,
        "regime": "texte-natif",
        "seed": seed,
        "variante": variante,
        "degradations": [],
        "derive_de": None,
        "tags": tags,
        # Vérité terrain des données personnelles présentes dans le document. Le
        # générateur connaît les valeurs qu'il a injectées, ce qui permettra plus tard
        # d'évaluer un masquage automatique sans annoter le corpus à la main.
        "pii_injectees": pii,
        "split": _split(type_attendu, seed),
        "configuration_sha256": CONFIGURATION_SHA256,
    }


def entree_derivee(original: dict, identifiant: str, chemin: Path, regime: str,
                   degradations: list[str], tags: list[str]) -> dict:
    """Ligne de manifest d'un document produit à partir d'un autre.

    L'entrée est une copie de celle de l'original, dont seul ce qui a réellement changé
    est remplacé. Copier plutôt que reconstruire garantit que les deux documents portent
    le même type attendu, la même vérité terrain et surtout **le même jeu** : séparer un
    document de son dérivé entre le jeu de réglage et le jeu de mesure ferait fuiter le
    contenu de l'un dans l'autre.

    C'est aussi ce qui évite d'avoir à propager ici chaque champ ajouté au manifest.
    """
    return {
        **original,
        "id": identifiant,
        "fichier": _chemin_relatif(chemin),
        "sha256": hash_fichier(chemin),
        "regime": regime,
        "degradations": degradations,
        "derive_de": original["id"],
        "tags": tags,
    }


def lire() -> dict[str, dict]:
    if not FICHIER_MANIFEST.exists():
        return {}
    lignes = FICHIER_MANIFEST.read_text(encoding="utf-8").splitlines()
    entrees = [json.loads(ligne) for ligne in lignes if ligne.strip()]
    return {entree["id"]: entree for entree in entrees}


def enregistrer(nouvelles: list[dict]) -> None:
    """Réécrit le manifest avec le corpus décrit par la configuration globale."""
    entrees = {entree["id"]: entree for entree in nouvelles}
    FICHIER_MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    contenu = "\n".join(
        json.dumps(entrees[identifiant], ensure_ascii=False)
        for identifiant in sorted(entrees)
    )
    FICHIER_MANIFEST.write_text(contenu + "\n", encoding="utf-8")


def valider() -> list[str]:
    """Contrôle le manifest. Retourne la liste des anomalies, vide si tout est bon."""
    anomalies = []

    if not FICHIER_MANIFEST.exists():
        return [f"Manifest absent : {FICHIER_MANIFEST}"]

    entrees = lire()
    if not entrees:
        return ["Manifest vide"]

    for identifiant, entree in sorted(entrees.items()):
        for champ in CHAMPS_OBLIGATOIRES:
            if champ not in entree:
                anomalies.append(f"{identifiant} : champ manquant '{champ}'")

        if entree.get("type_attendu") not in TYPES_CONNUS:
            anomalies.append(f"{identifiant} : type inconnu '{entree.get('type_attendu')}'")
        if entree.get("sensibilite") not in SENSIBILITES:
            anomalies.append(
                f"{identifiant} : sensibilite inconnue '{entree.get('sensibilite')}'")
        if entree.get("split") not in {"dev", "test"}:
            anomalies.append(f"{identifiant} : split invalide '{entree.get('split')}'")
        if entree.get("regime") not in REGIMES:
            anomalies.append(f"{identifiant} : regime inconnu '{entree.get('regime')}'")
        empreinte_configuration = entree.get("configuration_sha256")
        if (not isinstance(empreinte_configuration, str)
                or len(empreinte_configuration) != 64
                or any(c not in "0123456789abcdef" for c in empreinte_configuration)):
            anomalies.append(f"{identifiant} : empreinte de configuration invalide")

        origine = entree.get("derive_de")
        if origine is not None:
            if origine not in entrees:
                anomalies.append(f"{identifiant} : derive d'un document absent '{origine}'")
            # Un document et son jumeau scanné portent le même contenu. Les séparer
            # entre le jeu de réglage et le jeu de mesure ferait fuiter l'un dans
            # l'autre et rendrait toute mesure trop optimiste.
            elif entrees[origine]["split"] != entree["split"]:
                anomalies.append(f"{identifiant} : split different de son original")

        chemin = DOSSIER_CORPUS / entree.get("fichier", "")
        if not chemin.exists():
            anomalies.append(f"{identifiant} : fichier absent ({entree.get('fichier')})")
        elif hash_fichier(chemin) != entree.get("sha256"):
            anomalies.append(f"{identifiant} : hash differente du manifest")

    return anomalies


def _compte(entrees: list[dict], champ: str, valeur: str) -> int:
    return sum(1 for entree in entrees if entree[champ] == valeur)


def composition() -> str:
    """Tableau de composition du corpus, entièrement dérivé du manifest."""
    documents = list(lire().values())
    jumeaux = sum(1 for entree in documents if entree["derive_de"])
    types = sorted({entree["type_attendu"] for entree in documents})

    lignes = [
        "| Type | Documents | `dev` | `test` | Texte natif | Numérisés | Rôle |",
        "|---|---|---|---|---|---|---|",
    ]
    for type_attendu in types:
        du_type = [e for e in documents if e["type_attendu"] == type_attendu]
        role = "distracteur" if type_attendu in TYPES_DISTRACTEURS else "liste métier"
        lignes.append(
            f"| `{type_attendu}` | {len(du_type)} "
            f"| {_compte(du_type, 'split', 'dev')} "
            f"| {_compte(du_type, 'split', 'test')} "
            f"| {_compte(du_type, 'regime', 'texte-natif')} "
            f"| {_compte(du_type, 'regime', 'scanne')} "
            f"| {role} |")

    lignes += ["", (
        f"**{len(documents)} documents** en {len(types)} types, pour "
        f"{len(documents) - jumeaux} contenus distincts : {jumeaux} documents sont le "
        f"jumeau scanné d'un autre, même contenu et même jeu, reliés par `derive_de`.")]
    return "\n".join(lignes)


def ecrire_composition() -> None:
    """Réécrit la composition dans la documentation du corpus.

    Le corpus change dès qu'on touche aux effectifs de la configuration : une composition
    recopiée à la main y est fausse à la régénération suivante, et une documentation
    fausse est pire qu'une documentation absente. Seul le bloc délimité par les balises
    est remplacé ; tout le reste du fichier est de la prose qui ne se déduit d'aucun
    compteur, et qui reste écrite à la main.
    """
    texte = FICHIER_DOC_CORPUS.read_text(encoding="utf-8")
    debut, fin = texte.find(BALISE_DEBUT), texte.find(BALISE_FIN)
    if debut == -1 or fin == -1:
        raise ValueError(
            f"{FICHIER_DOC_CORPUS} ne porte pas les balises {BALISE_DEBUT} et "
            f"{BALISE_FIN} qui délimitent la composition à réécrire")

    remplacement = f"{BALISE_DEBUT}\n\n{composition()}\n\n"
    FICHIER_DOC_CORPUS.write_text(
        texte[:debut] + remplacement + texte[fin:], encoding="utf-8")


def resume() -> str:
    entrees = lire()
    documents = entrees.values()
    compteurs = {
        "type": Counter(e["type_attendu"] for e in documents),
        "jeu": Counter(e["split"] for e in documents),
        "regime": Counter(e["regime"] for e in documents),
        "template": Counter(e["variante"].get("template", "?") for e in documents),
    }

    lignes = [f"{len(entrees)} document(s) au manifest"]
    for axe, valeurs in compteurs.items():
        detail = ", ".join(f"{cle} {n}" for cle, n in sorted(valeurs.items()))
        lignes.append(f"  par {axe:<9}: {detail}")
    return "\n".join(lignes)


def main() -> None:
    anomalies = valider()
    for anomalie in anomalies:
        print(f"  {anomalie}")
    print(resume())
    print(f"\n{len(anomalies)} anomalie(s).")


if __name__ == "__main__":
    main()
