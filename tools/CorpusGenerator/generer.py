"""Point d'entree du generateur de corpus.

Usage :
    .venv/bin/python generer.py facture 8
    .venv/bin/python generer.py rib 8

Ecrit les documents dans `corpus/raw/` et met a jour `corpus/manifest.jsonl`.
Le numero d'ordre sert de seed : relancer la commande reecrit exactement les memes
fichiers, avec les memes hash.
"""

import sys

import manifest
from data import generer_facture, generer_rib
from rendu import couleur_logo, logo_svg, rendre_pdf


def produire_facture(numero: int) -> dict:
    # Une facture sur quatre est adressee a un particulier : le B2B domine en GED,
    # mais le B2C existe et ses mentions obligatoires ne sont pas les memes.
    facture = generer_facture(numero, professionnel=(numero % 4 != 0))
    identifiant = f"fac-{numero:04d}"
    chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"

    pages = rendre_pdf(
        "facture_01.html", chemin,
        f=facture,
        logo=logo_svg(facture.organisme.raison_sociale, numero),
    )

    tags = ["b2b" if facture.client.est_professionnel else "b2c"]
    if facture.organisme.exonere_tva:
        tags.append("tva-exoneree")
    if facture.nouvelles_mentions:
        tags.append("mentions-reforme")

    return manifest.construire_entree(
        identifiant, chemin, "facture", numero, pages,
        variante={"template": "facture_01"},
        pii={
            "raison_sociale_emetteur": facture.organisme.raison_sociale,
            "siret_emetteur": facture.organisme.siret,
            "email_emetteur": facture.organisme.email,
            "telephone_emetteur": facture.organisme.telephone,
            "designation_client": facture.client.designation,
            "contact_client": facture.client.contact,
            "iban": facture.coordonnees_bancaires.iban,
            "noms": facture.stagiaires,
        },
        tags=tags,
    )


def produire_rib(numero: int) -> dict:
    rib = generer_rib(numero)
    identifiant = f"rib-{numero:04d}"
    chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"

    pages = rendre_pdf(
        "rib_01.html", chemin,
        r=rib,
        logo=logo_svg(rib.banque, numero),
        couleur=couleur_logo(numero),
        date_edition=rib.date_edition,
    )

    return manifest.construire_entree(
        identifiant, chemin, "rib", numero, pages,
        variante={"template": "rib_01"},
        pii={
            "titulaire": rib.titulaire,
            "iban": rib.iban,
            "bic": rib.bic,
            "adresse_titulaire": f"{rib.adresse_titulaire.ligne1}, "
                                 f"{rib.adresse_titulaire.code_postal} "
                                 f"{rib.adresse_titulaire.ville}",
        },
        tags=["titulaire-personne" if rib.titulaire.startswith(("M.", "Mme"))
              else "titulaire-entreprise"],
    )


PRODUCTEURS = {"facture": produire_facture, "rib": produire_rib}


def main() -> None:
    if len(sys.argv) != 3 or sys.argv[1] not in PRODUCTEURS:
        types = ", ".join(PRODUCTEURS)
        print(f"Usage : python generer.py <{types}> <nombre>")
        sys.exit(1)

    producteur = PRODUCTEURS[sys.argv[1]]
    entrees = [producteur(numero) for numero in range(1, int(sys.argv[2]) + 1)]
    manifest.enregistrer(entrees)

    for entree in entrees:
        print(f"  {entree['id']}  {entree['split']:<4}  {', '.join(entree['tags'])}")
    print(f"\n{len(entrees)} document(s) ecrits, manifest mis a jour.")


if __name__ == "__main__":
    main()
