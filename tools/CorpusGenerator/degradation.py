"""Transforme un document propre en document scanné, photocopié ou photographié.

Un PDF à texte natif se lit sans OCR : il ne dit rien de la qualité d'un moteur de
reconnaissance. Ce module produit le jumeau dégradé d'un document propre, en le
rasterisant puis en lui appliquant les défauts d'une chaîne d'impression et de
numérisation réelle.

Les deux versions coexistent dans le corpus et sont reliées l'une à l'autre. Comparer un
résultat sur le document propre et sur son jumeau isole exactement ce que l'OCR coûte en
qualité, sur un contenu strictement identique.

Ce module décide aussi **qui** est numérisé et **comment**, à partir des parts et des
poids déclarés type par type dans le fichier TOML. Le reste de l'outil ne lit jamais ces
réglages lui-même : il pose la question ici, pour ne pas avoir à connaître la règle.

Le nom des effets appliqués est relevé sur les objets eux-mêmes plutôt que saisi à la
main : une liste écrite en dur finirait par mentir sur ce que le document a subi.
"""

import os
import random
from pathlib import Path
from configuration import (
    DEGRADATION_ACTIVE,
    DOCUMENTS,
    POIDS_PROFILS_DEFAUT,
    QUALITE_JPEG as QUALITE_CONTENEUR,
    RESOLUTION_DPI,
    entier,
    flux,
    part_numerisee,
    part_pivotee,
    poids_profils,
)

# Doit être posé avant l'import d'augraphy, qui charge numba.
#
# Augraphy accélère une partie de ses calculs en compilant du code avec numba. Les
# versions installées ici ne s'accordent pas : ShadowCast et BadPhotoCopy échouent à la
# compilation. Désactiver la compilation les fait passer en Python pur, ce qui coûte
# environ 2,8 secondes pour douze effets sur une page A4 à 200 dpi. Négligeable pour un
# outil qui fabrique quelques centaines de documents une fois.
# Affectation directe et non setdefault : un shell ou un conteneur qui aurait déjà
# posé la variable imposerait sinon son propre réglage, et le corpus produit différerait
# sans que rien ne l'explique.
os.environ["NUMBA_DISABLE_JIT"] = "1"

import cv2  # noqa: E402
import fitz  # noqa: E402
import numpy as np  # noqa: E402
from augraphy import (AugraphyPipeline, DirtyDrum, DirtyRollers,  # noqa: E402
                      Faxify, Geometric, InkBleed, Jpeg, Letterpress,
                      LightingGradient, LowInkRandomLines, NoiseTexturize,
                      ShadowCast, SubtleNoise)

# BadPhotoCopy est volontairement absent de ce module. Il choisit son algorithme de
# bruit au hasard et l'une de ses branches échoue avec numpy 2, environ une fois sur
# sept. Un effet qui fait échouer la génération par intermittence n'a pas sa place ici,
# et le rendu de photocopie s'obtient très bien avec Letterpress et DirtyDrum.


def _scan_propre() -> dict:
    """Numérisation correcte sur un matériel entretenu : le cas favorable."""
    return {
        "ink": [],
        "paper": [],
        "post": [Geometric(rotate_range=(-1, 1)), SubtleNoise(subtle_range=8),
                 Jpeg(quality_range=(70, 85))],
    }


def _scan_degrade() -> dict:
    """Scanner fatigué : rouleaux sales, encre qui bave, compression agressive."""
    return {
        "ink": [InkBleed(intensity_range=(0.4, 0.7))],
        "paper": [NoiseTexturize(sigma_range=(3, 8))],
        "post": [Geometric(rotate_range=(-3, 3)), DirtyRollers(line_width_range=(6, 12)),
                 SubtleNoise(subtle_range=12), Jpeg(quality_range=(45, 65))],
    }


def _photocopie() -> dict:
    """Copie de copie : caractères rongés, tambour sale, contraste écrasé."""
    return {
        "ink": [Letterpress(n_samples=(200, 500))],
        "paper": [DirtyDrum()],
        "post": [Geometric(rotate_range=(-2, 2)), Jpeg(quality_range=(50, 70))],
    }


def _photo_smartphone() -> dict:
    """Photo prise à la main : éclairage inégal, ombre portée, cadrage de travers.

    Cas très fréquent des documents déposés depuis un téléphone, et l'un des plus durs
    pour un OCR à cause du gradient de luminosité.
    """
    return {
        "ink": [],
        "paper": [],
        "post": [LightingGradient(), ShadowCast(shadow_opacity_range=(0.2, 0.5)),
                 Geometric(rotate_range=(-4, 4), scale=(0.94, 1.0)),
                 Jpeg(quality_range=(55, 75))],
    }


def _fax() -> dict:
    """Télécopie : binarisation brutale et lignes manquantes. Le cas le plus hostile."""
    return {
        "ink": [LowInkRandomLines(count_range=(4, 9))],
        "paper": [],
        "post": [Faxify(monochrome=1), Jpeg(quality_range=(40, 60))],
    }


PROFILS = {
    "scan-propre": _scan_propre,
    "scan-degrade": _scan_degrade,
    "photo-smartphone": _photo_smartphone,
    "fax": _fax,
    "photocopie": _photocopie,
}

# Le nom des profils est écrit dans le TOML, leur fabrique ici : un nom qui n'existe pas
# de ce côté ne serait jamais tiré, et un type se croirait numérisé d'une façon dont il ne
# l'est pas. Contrôlé à l'import, sur les poids de chaque type comme sur ceux par défaut.
for _origine, _poids in [("[defaults.profiles]", POIDS_PROFILS_DEFAUT)] + [
        (f"[types.{_nom}] profiles", poids_profils(_nom)) for _nom in DOCUMENTS]:
    _inconnus = set(_poids) - set(PROFILS)
    if _inconnus:
        raise ValueError(f"{_origine} : profil(s) inconnu(s) {sorted(_inconnus)}, "
                         f"attendu parmi {sorted(PROFILS)}")

# Le profil décide aussi de la façon dont un objet physique a été repris : photographier
# un carton ne produit pas les mêmes défauts que le passer au scanner. Un seul tirage pour
# les deux questions, sinon le document et le manifest finissent par se contredire.
ACQUISITION_PAR_PROFIL = {"photo-smartphone": "photo"}


def _retenu(rang: int, numerateur: int, denominateur: int) -> bool:
    """Vrai pour `numerateur` rangs sur `denominateur`, répartis le plus régulièrement
    possible et sans jamais redistribuer les rangs déjà passés.

    C'est ce qui rend les parts déclarées exactes plutôt qu'espérées. Sur quatre unités,
    un tirage à 55 % en donne aussi bien deux que quatre, et chaque type se retrouverait
    avec sa propre proportion de documents numérisés : un type plus mal classé qu'un autre
    serait alors impossible à départager de sa part de scans. La part étant arithmétique,
    le nombre de paires propre et numérisé est connu avant la génération, ce qui fixe la
    précision de la comparaison entre les deux régimes.

    Le demi-dénominateur ajouté aux deux membres arrondit au plus proche au lieu de
    tronquer. Sans lui, la règle attend d'avoir accumulé un rang entier de retard avant de
    retenir le premier, ce qui coûte un document sur les effectifs modestes du corpus :
    une part de 33 % en donnait 25 sur douze factures.
    """
    moitie = denominateur // 2
    return ((rang * numerateur + moitie) // denominateur
            > ((rang - 1) * numerateur + moitie) // denominateur)


def numerise(type_document: str, numero: int) -> bool:
    """Ce document part-il en numérisation, plutôt que de rester à texte natif ?

    Pour les types qui n'existent qu'en un seul exemplaire, repris d'un objet physique ou
    capturés à l'écran. Les types qui conservent leur original passent par `jumeau`.
    """
    return DEGRADATION_ACTIVE and _retenu(numero, part_numerisee(type_document), 100)


def jumeau(type_document: str, numero: int) -> bool:
    """Cette unité se dédouble-t-elle en un jumeau numérisé du même contenu ?

    La part demandée porte sur les documents écrits au manifest, or une unité dédoublée en
    produit deux : il faut donc dédoubler `part / (100 - part)` unités pour que la part des
    documents numérisés tombe juste. D'où le plafond de 50 %, au-delà duquel il faudrait
    plus d'un jumeau par unité ; `generer.py` le refuse.
    """
    part = part_numerisee(type_document)
    return DEGRADATION_ACTIVE and _retenu(numero, part, 100 - part)


def profil(type_document: str, numero: int) -> str:
    """Profil de numérisation à appliquer, tiré dans les poids déclarés pour ce type.

    L'aléa est ici à sa place : il produit de la variété de défauts, il ne décide pas de
    la structure du corpus. Les noms sont triés pour que réordonner la table du TOML, qui
    ne change rien à ce qu'elle déclare, ne change pas non plus le corpus produit.
    """
    poids = poids_profils(type_document)
    noms = sorted(poids)
    return flux(f"profil-{type_document}", numero).choices(
        noms, weights=[poids[nom] for nom in noms])[0]


def acquisition(type_document: str, numero: int) -> tuple[str, str | None]:
    """Comment un document repris d'un objet physique est arrivé, et sous quel profil.

    Trois modes. `photo` est le carton ou la feuille photographiée à la main, `scan` le
    passage à plat, et `fichier` l'export bureautique déposé tel quel, qui reste à texte
    natif et ne reçoit donc aucun profil. Le mode se déduit du profil tiré plutôt que
    l'inverse : c'est la table de poids du type, dans le TOML, qui décide de tout.
    """
    if not numerise(type_document, numero):
        return "fichier", None
    nom = profil(type_document, numero)
    return ACQUISITION_PAR_PROFIL.get(nom, "scan"), nom


def quart_de_tour(type_document: str, numero: int) -> bool:
    """Ce document numérisé est-il posé de travers d'un quart de tour ?

    À n'appeler que sur un document effectivement numérisé : un export bureautique est
    droit, et la part déclarée porte sur les seuls documents numérisés du type.
    """
    return flux(f"pivot-{type_document}", numero).randrange(100) < part_pivotee(type_document)


def _rasteriser(source: Path) -> list[np.ndarray]:
    document = fitz.open(source)
    try:
        pages = []
        for page in document:
            pixmap = page.get_pixmap(dpi=RESOLUTION_DPI)
            image = np.frombuffer(pixmap.samples, dtype=np.uint8)
            image = image.reshape(pixmap.height, pixmap.width, pixmap.n)
            pages.append(image[:, :, :3].copy())
        return pages
    finally:
        document.close()


def _ecrire_pdf(pages: list[np.ndarray], destination: Path, seed: int) -> None:
    """Enveloppe les images dans un PDF, comme le ferait un scanner.

    Deux précautions pour que le fichier soit reproductible octet pour octet. Les
    métadonnées sont vidées, sinon PyMuPDF y écrit la date du jour. Et l'identifiant de
    document du trailer, que PyMuPDF tire au hasard à chaque sauvegarde, est dérivé de la
    seed : c'était le seul reste de non-déterminisme, cinquante-neuf octets qui suffisaient
    à changer l'empreinte du fichier.
    """
    document = fitz.open()
    try:
        for page in pages:
            # OpenCV encode en BGR. Nos images sont en RGB, d'où l'inversion des canaux,
            # sans laquelle les couleurs d'accent ressortiraient inversées.
            succes, tampon = cv2.imencode(
                ".jpg", page[:, :, ::-1],
                [int(cv2.IMWRITE_JPEG_QUALITY), QUALITE_CONTENEUR])
            if not succes:
                raise RuntimeError("Encodage JPEG impossible")

            hauteur, largeur = page.shape[:2]
            points_par_pouce = 72 / RESOLUTION_DPI
            feuille = document.new_page(width=largeur * points_par_pouce,
                                        height=hauteur * points_par_pouce)
            feuille.insert_image(feuille.rect, stream=tampon.tobytes())

        document.set_metadata({})
        empreinte = f"{flux('identifiant-pdf', seed).getrandbits(128):032X}"
        document.xref_set_key(-1, "ID", f"[<{empreinte}><{empreinte}>]")

        destination.parent.mkdir(parents=True, exist_ok=True)
        # 'no_new_id' empêche PyMuPDF de régénérer un identifiant à la sauvegarde,
        # ce qui annulerait celui qu'on vient de fixer.
        document.save(destination, garbage=4, deflate=True, no_new_id=True)
    finally:
        document.close()


def capturer_ecran(source: Path, destination: Path, seed: int) -> None:
    """Rastérise un PDF propre en image nette, sans aucune dégradation.

    Réservé à `OFF_STAG_EMP` : contrairement à `degrader`, qui imite l'impression et la
    numérisation d'un papier, cette fonction ne simule aucun défaut. Une offre d'emploi
    capturée en image est la capture d'écran d'une page web, jamais la reprise d'un
    objet physique, et le rendu reste net, seule la couche texte disparaît. Réutilise la
    rastérisation et l'écriture reproductible de `degrader`, sans chaîne Augraphy et sans
    passer par le catalogue de profils : c'est pourquoi déclarer une table `profiles` sur
    ce type est refusé, aucun profil ne saurait s'y appliquer.
    """
    _ecrire_pdf(_rasteriser(source), destination, seed)


def degrader(source: Path, destination: Path, seed: int, nom_profil: str,
             rotation_page_deg: int = 0) -> list[str]:
    """Produit le jumeau dégradé d'un document. Retourne les effets réellement appliqués."""
    if rotation_page_deg not in {0, 90, 180, 270}:
        raise ValueError(f"rotation de page invalide : {rotation_page_deg}")

    phases = PROFILS[nom_profil]()

    # Le random_seed de la chaîne ne suffit pas : plusieurs effets tirent directement
    # dans les générateurs globaux de Python et de numpy. Sans ces trois lignes, deux
    # exécutions du même document produisent deux fichiers différents.
    graine = entier("effets", seed)
    random.seed(graine)
    np.random.seed(graine)

    chaine = AugraphyPipeline(
        ink_phase=phases["ink"],
        paper_phase=phases["paper"],
        post_phase=phases["post"],
        random_seed=graine,
    )

    pages = []
    for page in _rasteriser(source):
        if rotation_page_deg:
            page = np.rot90(page, k=-(rotation_page_deg // 90)).copy()
        pages.append(chaine(page))
    _ecrire_pdf(pages, destination, seed)

    effets = [type(effet).__name__ for phase in phases.values() for effet in phase]
    if rotation_page_deg:
        effets.insert(0, f"RotationPage{rotation_page_deg}")
    return effets
