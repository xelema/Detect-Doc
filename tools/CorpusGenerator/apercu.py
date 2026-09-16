"""Aperçu d'un type sans régénérer le corpus, pour la boucle de mise au point.

Usage :
    apercu.py <type> [nombre]            N documents propres rendus sous apercu/
    apercu.py <type> [nombre] --complet  la tranche du corpus pour ce type, à
                                         l'effectif du TOML ou de <nombre>,
                                         jumeaux numérisés ou reprises compris,
                                         et son manifest sous apercu/manifest.jsonl

Chaque PDF rendu est aussi exporté en PNG page par page : ni le manifest ni les
contrôles ne voient un débordement de mise en page, seule l'image le montre.
Tout est écrit sous apercu/, ignoré par Git ; le corpus et son manifest ne sont
jamais touchés.
"""

import sys
from pathlib import Path

import fitz

import manifest
from configuration import DOCUMENTS
from generer import TYPES, _produire

DOSSIER_APERCU = Path("apercu")


def _png(entree: dict) -> None:
    chemin = DOSSIER_APERCU / entree["fichier"]
    with fitz.open(chemin) as pdf:
        for numero, page in enumerate(pdf, start=1):
            page.get_pixmap(dpi=110).save(
                str(DOSSIER_APERCU / f"{entree['id']}-{numero}.png"))


def main() -> None:
    complet = "--complet" in sys.argv
    args = [a for a in sys.argv[1:] if a != "--complet"]
    if not args or args[0] not in TYPES:
        types = ", ".join(sorted(TYPES))
        raise SystemExit(
            f"usage : apercu.py <type> [nombre] [--complet] ; types : {types}")

    type_attendu = args[0]

    # produire() écrit sous manifest.DOSSIER_DOCUMENTS et déclare son chemin relatif
    # à manifest.DOSSIER_CORPUS : rediriger les deux, et le fichier du manifest en
    # mode complet, détourne toute la production vers apercu/ sans toucher au
    # corpus réel.
    manifest.DOSSIER_CORPUS = DOSSIER_APERCU
    manifest.DOSSIER_DOCUMENTS = DOSSIER_APERCU
    manifest.FICHIER_MANIFEST = DOSSIER_APERCU / "manifest.jsonl"

    if complet:
        nombre = int(args[1]) if len(args) > 1 else DOCUMENTS.get(type_attendu, 0)
        entrees = _produire(type_attendu, nombre)
        manifest.enregistrer(entrees)
    else:
        nombre = int(args[1]) if len(args) > 1 else 3
        entrees = [TYPES[type_attendu].produire(numero)
                   for numero in range(1, nombre + 1)]

    for entree in entrees:
        _png(entree)
        print(f"  {entree['id']:<12} {entree['regime']:<12} "
              f"{entree['pages']} page(s)  {', '.join(entree['tags'])}")

    if complet:
        print(f"manifest : {manifest.FICHIER_MANIFEST}")
    print(f"{len(entrees)} document(s) sous {DOSSIER_APERCU}/")


if __name__ == "__main__":
    main()
