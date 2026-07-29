"""Transforme un document propre en document scanne, photocopie ou photographie.

Un PDF a texte natif se lit sans OCR : il ne dit rien de la qualite d'un moteur de
reconnaissance. Ce module produit le jumeau degrade d'un document propre, en le
rasterisant puis en lui appliquant les defauts d'une chaine d'impression et de
numerisation reelle.

Les deux versions coexistent dans le corpus et sont reliees l'une a l'autre. Comparer un
resultat sur le document propre et sur son jumeau isole exactement ce que l'OCR coute en
qualite, sur un contenu strictement identique.

Le nom des effets appliques est releve sur les objets eux-memes plutot que saisi a la
main : une liste ecrite en dur finirait par mentir sur ce que le document a subi.
"""

import hashlib
import os
import random
from pathlib import Path

# Doit etre pose avant l'import d'augraphy, qui charge numba.
#
# Augraphy accelere une partie de ses calculs en compilant du code avec numba. Les
# versions installees ici ne s'accordent pas : ShadowCast et BadPhotoCopy echouent a la
# compilation. Desactiver la compilation les fait passer en Python pur, ce qui coute
# environ 2,8 secondes pour douze effets sur une page A4 a 200 dpi. Negligeable pour un
# outil qui fabrique quelques centaines de documents une fois.
# Affectation directe et non `setdefault` : un shell ou un conteneur qui aurait deja
# pose la variable imposerait sinon son propre reglage, et le corpus produit differerait
# sans que rien ne l'explique.
os.environ["NUMBA_DISABLE_JIT"] = "1"

import cv2  # noqa: E402
import fitz  # noqa: E402
import numpy as np  # noqa: E402
from augraphy import (AugraphyPipeline, DirtyDrum, DirtyRollers,  # noqa: E402
                      Faxify, Geometric, InkBleed, Jpeg, Letterpress,
                      LightingGradient, LowInkRandomLines, NoiseTexturize,
                      ShadowCast, SubtleNoise)

# `BadPhotoCopy` est volontairement absent de ce module. Il choisit son algorithme de
# bruit au hasard et l'une de ses branches echoue avec numpy 2, environ une fois sur
# sept. Un effet qui fait echouer la generation par intermittence n'a pas sa place ici,
# et le rendu de photocopie s'obtient tres bien avec Letterpress et DirtyDrum.

# Resolution des scanners de bureau. En dessous de 150 l'OCR se degrade pour une raison
# qui n'a rien a voir avec le document lui-meme, ce qui fausserait la comparaison.
RESOLUTION_DPI = 200

# Qualite du JPEG qui porte l'image dans le PDF. Les artefacts de compression que l'on
# veut mesurer sont produits par l'effet `Jpeg` de la chaine, pas par ce conteneur.
QUALITE_CONTENEUR = 88


def _scan_propre() -> dict:
    """Numerisation correcte sur un materiel entretenu : le cas favorable."""
    return {
        "ink": [],
        "paper": [],
        "post": [Geometric(rotate_range=(-1, 1)), SubtleNoise(subtle_range=8),
                 Jpeg(quality_range=(70, 85))],
    }


def _scan_degrade() -> dict:
    """Scanner fatigue : rouleaux sales, encre qui bave, compression agressive."""
    return {
        "ink": [InkBleed(intensity_range=(0.4, 0.7))],
        "paper": [NoiseTexturize(sigma_range=(3, 8))],
        "post": [Geometric(rotate_range=(-3, 3)), DirtyRollers(line_width_range=(6, 12)),
                 SubtleNoise(subtle_range=12), Jpeg(quality_range=(45, 65))],
    }


def _photocopie() -> dict:
    """Copie de copie : caracteres ronges, tambour sale, contraste ecrase."""
    return {
        "ink": [Letterpress(n_samples=(200, 500))],
        "paper": [DirtyDrum()],
        "post": [Geometric(rotate_range=(-2, 2)), Jpeg(quality_range=(50, 70))],
    }


def _photo_smartphone() -> dict:
    """Photo prise a la main : eclairage inegal, ombre portee, cadrage de travers.

    Cas tres frequent des documents deposes depuis un telephone, et l'un des plus durs
    pour un OCR a cause du gradient de luminosite.
    """
    return {
        "ink": [],
        "paper": [],
        "post": [LightingGradient(), ShadowCast(shadow_opacity_range=(0.2, 0.5)),
                 Geometric(rotate_range=(-4, 4), scale=(0.94, 1.0)),
                 Jpeg(quality_range=(55, 75))],
    }


def _fax() -> dict:
    """Telecopie : binarisation brutale et lignes manquantes. Le cas le plus hostile."""
    return {
        "ink": [LowInkRandomLines(count_range=(4, 9))],
        "paper": [],
        "post": [Faxify(monochrome=1), Jpeg(quality_range=(40, 60))],
    }


PROFILS = {
    "scan-propre": _scan_propre,
    "scan-degrade": _scan_degrade,
    "photocopie": _photocopie,
    "photo-smartphone": _photo_smartphone,
    "fax": _fax,
}

# Repartition volontairement inegale : en production, un scan correct est bien plus
# frequent qu'un fax. Un corpus qui donnerait autant de poids a chaque profil
# surestimerait la difficulte moyenne.
POIDS_PROFILS = {
    "scan-propre": 45,
    "scan-degrade": 30,
    "photocopie": 5,
    "photo-smartphone": 15,
    "fax": 5,
}


PROPORTION_DEGRADES = 0.55


def profil_degradation(seed: int) -> str | None:
    """Profil a appliquer au document, ou None s'il reste a texte natif.

    Les deux regimes doivent rester bien representes : sans documents propres, on perd
    la reference qui permet de chiffrer le cout de l'OCR ; sans documents degrades, on
    ne mesure pas l'OCR du tout.
    """
    tirage = random.Random(f"degradation-{seed}")
    if tirage.random() >= PROPORTION_DEGRADES:
        return None
    # Le tirage parcourt `PROFILS` et non `POIDS_PROFILS` : un profil ajoute a la table
    # des poids sans fabrique echoue alors franchement, au lieu de n'etre jamais tire.
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

    Deux precautions pour que le fichier soit reproductible octet pour octet. Les
    metadonnees sont videes, sinon PyMuPDF y ecrit la date du jour. Et l'identifiant de
    document du trailer, que PyMuPDF tire au hasard a chaque sauvegarde, est calcule a
    partir de la seed : c'etait le seul reste de non-determinisme, cinquante-neuf octets
    qui suffisaient a changer l'empreinte du fichier.
    """
    document = fitz.open()
    try:
        for page in pages:
            # OpenCV encode en BGR. Nos images sont en RGB, d'ou l'inversion des canaux,
            # sans laquelle les couleurs d'accent ressortiraient inversees.
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
        # `no_new_id` empeche PyMuPDF de regenerer un identifiant a la sauvegarde,
        # ce qui annulerait celui qu'on vient de fixer.
        document.save(destination, garbage=4, deflate=True, no_new_id=True)
    finally:
        document.close()


def degrader(source: Path, destination: Path, seed: int, profil: str) -> list[str]:
    """Produit le jumeau degrade d'un document. Retourne les effets reellement appliques."""
    phases = PROFILS[profil]()

    # Le `random_seed` de la chaine ne suffit pas : plusieurs effets tirent directement
    # dans les generateurs globaux de Python et de numpy. Sans ces deux lignes, deux
    # executions du meme document produisent deux fichiers differents.
    random.seed(seed)
    np.random.seed(seed)

    chaine = AugraphyPipeline(
        ink_phase=phases["ink"],
        paper_phase=phases["paper"],
        post_phase=phases["post"],
        random_seed=seed,
    )

    pages = [chaine(page) for page in _rasteriser(source)]
    _ecrire_pdf(pages, destination, seed)

    return [type(effet).__name__ for phase in phases.values() for effet in phase]
