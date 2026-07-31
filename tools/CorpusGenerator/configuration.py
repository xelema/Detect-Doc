"""Configuration globale lue une fois au démarrage.

Le fichier TOML se lit sur trois étages, du plus général au plus précis : les tables
`corpus`, `split` et `degradation` règlent la génération entière, `defaults` porte la
valeur de tout type qui ne dit rien, et `types.<nom>` ce qui est propre à un type. Les
accesseurs de ce module résolvent cet héritage, personne d'autre n'a à le connaître.

Ce module ne dépend que de la bibliothèque standard, et cela doit le rester : tout l'outil
l'importe, y compris le validateur du manifest, qui doit pouvoir contrôler un corpus déjà
produit sans tirer les bibliothèques de rendu.

Les erreurs de configuration sont levées ici, à l'import : une clé mal orthographiée dans
un fichier où presque tout est facultatif se croirait sinon appliquée pendant des semaines.
"""

import random
import tomllib
from hashlib import sha256
from pathlib import Path


FICHIER = Path(__file__).with_name("corpus-generator.toml")
SOURCE = FICHIER.read_bytes()
TOML = tomllib.loads(SOURCE.decode("utf-8"))
SHA256 = sha256(SOURCE).hexdigest()

# Les tables globales sont contrôlées avant d'être lues : une clé mal orthographiée y est
# silencieuse et laisse sa valeur d'origine s'appliquer, ce qui est exactement le genre de
# réglage qu'on croit avoir posé. Les réglages par type le sont plus bas, sur le même
# principe.
for _table, _cles in [
    ("corpus", {"output_dir", "seed", "write_composition"}),
    ("split", {"dev_every"}),
    ("degradation", {"enabled", "dpi", "container_jpeg_quality"}),
]:
    if set(TOML[_table]) != _cles:
        raise ValueError(
            f"[{_table}] doit déclarer exactement {sorted(_cles)}, "
            f"et porte {sorted(TOML[_table])}")

chemin_corpus = Path(TOML["corpus"]["output_dir"])
DOSSIER_CORPUS = (
    chemin_corpus if chemin_corpus.is_absolute() else FICHIER.parent / chemin_corpus
).resolve()

SEED = int(TOML["corpus"]["seed"])
ECRIRE_COMPOSITION = bool(TOML["corpus"]["write_composition"])

UNITE_SUR_N_EN_DEV = int(TOML["split"]["dev_every"])

DEGRADATION_ACTIVE = bool(TOML["degradation"]["enabled"])
RESOLUTION_DPI = int(TOML["degradation"]["dpi"])
QUALITE_JPEG = int(TOML["degradation"]["container_jpeg_quality"])

_DEFAUTS = TOML["defaults"]
_TYPES = TOML["types"]

# Les quatre réglages d'un type. Tous héritent de `[defaults]`, y compris l'effectif : un
# bloc de type vide est donc légal et vaut le corpus par défaut. Toute autre clé est une
# faute de frappe.
_REGLAGES = {"count", "scanned_share", "rotated_share", "profiles"}


def _valider_reglage(chemin: str, cle: str, valeur: object) -> None:
    """Contrôle un réglage, qu'il vienne de `[defaults]` ou d'un type.

    Les noms de profils, eux, sont contrôlés par `degradation.py`, seul module qui sache
    lesquels existent.
    """
    if cle == "count":
        if not isinstance(valeur, int) or valeur < 0:
            raise ValueError(f"{chemin} doit être un entier positif ou nul")
    elif cle == "profiles":
        if not isinstance(valeur, dict) or not valeur:
            raise ValueError(f"{chemin} : table de poids vide ou mal formée")
        if any(not isinstance(poids, int) or poids < 0 for poids in valeur.values()):
            raise ValueError(f"{chemin} : les poids doivent être des entiers positifs")
        if sum(valeur.values()) == 0:
            raise ValueError(f"{chemin} : tous les poids sont nuls, aucun profil ne sort")
    elif not isinstance(valeur, int) or not 0 <= valeur <= 100:
        raise ValueError(f"{chemin} : {valeur!r} n'est pas un pourcentage entier de 0 à 100")


if set(_DEFAUTS) != _REGLAGES:
    raise ValueError(
        f"[defaults] doit déclarer exactement {sorted(_REGLAGES)}, "
        f"et porte {sorted(_DEFAUTS)}")

for _cle, _valeur in _DEFAUTS.items():
    _valider_reglage(f"[defaults] {_cle}", _cle, _valeur)

for _nom, _reglages in _TYPES.items():
    _inconnues = set(_reglages) - _REGLAGES
    if _inconnues:
        raise ValueError(
            f"[types.{_nom}] : clé(s) inconnue(s) {sorted(_inconnues)}, "
            f"attendu parmi {sorted(_REGLAGES)}")
    for _cle, _valeur in _reglages.items():
        _valider_reglage(f"[types.{_nom}] {_cle}", _cle, _valeur)

POIDS_PROFILS_DEFAUT = _DEFAUTS["profiles"]


def reglages_explicites(type_document: str) -> set[str]:
    """Réglages que le fichier pose lui-même sur ce type, hors valeurs héritées.

    Sert aux contrôles de cohérence de `generer.py`, qui seul sait de quelle nature est
    chaque type : un réglage posé sur un type qui ne peut rien en faire doit se voir.
    """
    return set(_TYPES.get(type_document, {}))


def _reglage(type_document: str, cle: str):
    return _TYPES.get(type_document, {}).get(cle, _DEFAUTS[cle])


# Nombre de documents écrits au manifest pour chaque type, jumeaux numérisés compris.
DOCUMENTS = {nom: _reglage(nom, "count") for nom in _TYPES}


def part_numerisee(type_document: str) -> int:
    """Part des documents de ce type qui sortent numérisés, en pourcentage."""
    return _reglage(type_document, "scanned_share")


def part_pivotee(type_document: str) -> int:
    """Part des documents numérisés de ce type posés de travers d'un quart de tour."""
    return _reglage(type_document, "rotated_share")


def poids_profils(type_document: str) -> dict[str, int]:
    """Poids des profils de numérisation applicables à ce type."""
    return _reglage(type_document, "profiles")


def flux(nom: str, valeur) -> random.Random:
    """Générateur d'un flux nommé, seedé sur la seed globale.

    Chaque facette d'un document tire dans son propre flux : plusieurs facettes dérivées
    d'une même seed finiraient sinon par s'aligner, et le jeu de réglage se retrouverait
    réduit à une seule mise en page.

    La seed globale entre dans le nom du flux, ce qui suffit à décorréler entièrement deux
    corpus tirés sur deux seeds différentes : `random.seed` sur une chaîne passe par un
    hachage, il ne s'agit pas d'un décalage.
    """
    return random.Random(f"{nom}-{valeur}-{SEED}")


def entier(nom: str, valeur) -> int:
    """Entier reproductible tiré d'un flux nommé.

    Pour les bibliothèques qui exigent un entier plutôt qu'une chaîne. La plage s'arrête
    à 31 bits, la plus étroite qu'elles imposent : OpenCV refuse une seed qui ne tient pas
    dans un `int` signé du C, et numpy en refuse une négative.
    """
    return flux(nom, valeur).randrange(2 ** 31)
