"""Transforme un document propre en document scanné, photocopié ou photographié.

Un PDF à texte natif se lit sans OCR : il ne dit rien de la qualité d'un moteur de
reconnaissance. Ce module produit le jumeau dégradé d'un document propre, en le
rasterisant puis en lui appliquant les défauts d'une chaîne d'impression et de
numérisation réelle.

Les deux versions coexistent dans le corpus et sont reliées l'une à l'autre. Comparer un
résultat sur le document propre et sur son jumeau isole exactement ce que l'OCR coûte en
qualité, sur un contenu strictement identique.

Le nom des effets appliqués est relevé sur les objets eux-mêmes plutôt que saisi à la
main : une liste écrite en dur finirait par mentir sur ce que le document a subi.
"""

import hashlib
import os
import random
from pathlib import Path
from configuration import (
    POIDS_PROFILS,
    PROBABILITE_JUMEAU as PROPORTION_DEGRADES,
    QUALITE_JPEG as QUALITE_CONTENEUR,
    RESOLUTION_DPI,
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

# Résolution des scanners de bureau. En dessous de 150 l'OCR se dégrade pour une raison
# qui n'a rien à voir avec le document lui-même, ce qui fausserait la comparaison.
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

# Répartition volontairement inégale : en production, un scan correct est bien plus
# fréquent qu'un fax. Un corpus qui donnerait autant de poids à chaque profil
# surestimerait la difficulté moyenne.
if set(POIDS_PROFILS) != set(PROFILS):
    raise ValueError("les poids doivent déclarer exactement les cinq profils")


# Profils compatibles avec la façon dont un original physique a été repris. Photographier
# un carton ne produit pas les mêmes défauts que le passer au scanner : appliquer les uns
# à la place des autres donnerait un document que son propre manifest décrit de travers.
PROFILS_PAR_ACQUISITION = {
    "photo": ["photo-smartphone"],
    "scan": ["scan-propre", "scan-degrade", "photocopie", "fax"],
}


def profil_acquisition(seed: int, acquisition: str) -> str:
    """Profil de numérisation d'un document qui n'existe que sous forme d'objet physique.

    La question n'est pas ici de savoir *si* le document est numérisé, il l'est toujours,
    mais *comment*. Le style a déjà décidé de la reprise, photo posée sur un bureau ou
    passage au scanner, et il ne reste qu'à choisir les défauts cohérents avec elle.
    """
    candidats = PROFILS_PAR_ACQUISITION[acquisition]
    return random.Random(f"acquisition-{seed}").choices(
        candidats, weights=[POIDS_PROFILS[nom] for nom in candidats])[0]


def profil_degradation(seed: int) -> str | None:
    """Profil à appliquer au document, ou None s'il reste à texte natif.

    Les deux régimes doivent rester bien représentés : sans documents propres, on perd
    la référence qui permet de chiffrer le coût de l'OCR ; sans documents dégradés, on
    ne mesure pas l'OCR du tout.
    """
    tirage = random.Random(f"degradation-{seed}")
    if tirage.random() >= PROPORTION_DEGRADES:
        return None
    # Le tirage parcourt PROFILS et non POIDS_PROFILS : un profil ajouté à la table
    # des poids sans fabrique échoue alors franchement, au lieu de n'être jamais tiré.
    return tirage.choices(list(PROFILS), weights=[POIDS_PROFILS[n] for n in PROFILS])[0]


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
    document du trailer, que PyMuPDF tire au hasard à chaque sauvegarde, est calculé à
    partir de la seed : c'était le seul reste de non-déterminisme, cinquante-neuf octets
    qui suffisaient à changer l'empreinte du fichier.
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
        empreinte = hashlib.sha256(str(seed).encode()).hexdigest()[:32].upper()
        document.xref_set_key(-1, "ID", f"[<{empreinte}><{empreinte}>]")

        destination.parent.mkdir(parents=True, exist_ok=True)
        # 'no_new_id' empêche PyMuPDF de régénérer un identifiant à la sauvegarde,
        # ce qui annulerait celui qu'on vient de fixer.
        document.save(destination, garbage=4, deflate=True, no_new_id=True)
    finally:
        document.close()


def degrader(source: Path, destination: Path, seed: int, profil: str,
             rotation_page_deg: int = 0) -> list[str]:
    """Produit le jumeau dégradé d'un document. Retourne les effets réellement appliqués."""
    if rotation_page_deg not in {0, 90, 180, 270}:
        raise ValueError(f"rotation de page invalide : {rotation_page_deg}")

    phases = PROFILS[profil]()

    # Le random_seed de la chaîne ne suffit pas : plusieurs effets tirent directement
    # dans les générateurs globaux de Python et de numpy. Sans ces deux lignes, deux
    # exécutions du même document produisent deux fichiers différents.
    random.seed(seed)
    np.random.seed(seed)

    chaine = AugraphyPipeline(
        ink_phase=phases["ink"],
        paper_phase=phases["paper"],
        post_phase=phases["post"],
        random_seed=seed,
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
