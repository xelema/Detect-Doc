"""Point d'entree du generateur de corpus.

Usage :
    py generer.py facture 3

Produit les documents dans `out/`. Le numero d'ordre sert de graine, donc relancer la
commande reecrit exactement les memes fichiers.
"""

import sys
from pathlib import Path

from data import generer_facture, generer_rib
from rendu import couleur_logo, logo_svg, rendre_pdf

DOSSIER_SORTIE = Path(__file__).parent / "out"


def produire_facture(numero: int) -> Path:
    # Une facture sur quatre est adressee a un particulier : le B2B domine en GED,
    # mais le B2C existe et ses mentions obligatoires ne sont pas les memes.
    facture = generer_facture(numero, professionnel=(numero % 4 != 0))

    return rendre_pdf(
        "facture_01.html",
        DOSSIER_SORTIE / f"facture-{numero:04d}.pdf",
        f=facture,
        logo=logo_svg(facture.organisme.raison_sociale, numero),
    )


def produire_rib(numero: int) -> Path:
    rib = generer_rib(numero)

    return rendre_pdf(
        "rib_01.html",
        DOSSIER_SORTIE / f"rib-{numero:04d}.pdf",
        r=rib,
        logo=logo_svg(rib.banque, numero),
        couleur=couleur_logo(numero),
        date_edition=rib.date_edition,
    )


PRODUCTEURS = {"facture": produire_facture, "rib": produire_rib}


def main() -> None:
    if len(sys.argv) != 3 or sys.argv[1] not in PRODUCTEURS:
        types = ", ".join(PRODUCTEURS)
        print(f"Usage : python generer.py <{types}> <nombre>")
        sys.exit(1)

    producteur = PRODUCTEURS[sys.argv[1]]
    for numero in range(1, int(sys.argv[2]) + 1):
        chemin = producteur(numero)
        print(f"  {chemin.name}")


if __name__ == "__main__":
    main()
