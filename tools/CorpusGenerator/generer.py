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
from data import client_professionnel, generer_facture, generer_rib
from degradation import degrader, profil_degradation
from rendu import logo_svg, rendre_pdf
from styles import style_facture, style_rib


def _jumeau_scanne(propre: dict, seed: int) -> dict | None:
    """Version scannee d'un document, si la seed lui en attribue une."""
    profil = profil_degradation(seed)
    if profil is None:
        return None

    identifiant = f"{propre['id']}-d"
    chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"
    effets = degrader(manifest.DOSSIER_CORPUS / propre["fichier"], chemin, seed, profil)

    return manifest.entree_derivee(
        propre, identifiant, chemin,
        regime="scanne", degradations=effets, tags=propre["tags"] + [profil])


def produire_facture(numero: int) -> dict:
    facture = generer_facture(numero, professionnel=client_professionnel(numero))
    style = style_facture(numero)
    identifiant = f"fac-{numero:04d}"
    chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"

    pages = rendre_pdf(
        style.template, chemin,
        f=facture,
        s=style,
        logo=logo_svg(facture.organisme.raison_sociale, style.couleur),
        # Date portee par le tampon a l'encre, quand le style en pose un.
        date_marque=facture.date_echeance,
    )

    tags = ["b2b" if facture.client.est_professionnel else "b2c"]
    if facture.organisme.exonere_tva:
        tags.append("tva-exoneree")
    if facture.nouvelles_mentions:
        tags.append("mentions-reforme")
    if style.filigrane:
        tags.append("filigrane")

    return manifest.construire_entree(
        identifiant, chemin, "facture", numero, pages,
        variante=style.as_dict(),
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
    style = style_rib(numero)
    identifiant = f"rib-{numero:04d}"
    chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"

    pages = rendre_pdf(
        style.template, chemin,
        r=rib,
        s=style,
        logo=logo_svg(rib.banque, style.couleur),
    )

    return manifest.construire_entree(
        identifiant, chemin, "rib", numero, pages,
        variante=style.as_dict(),
        pii={
            "titulaire": rib.titulaire,
            "iban": rib.iban,
            "bic": rib.bic,
            "adresse_titulaire": f"{rib.adresse_titulaire.ligne1}, "
                                 f"{rib.adresse_titulaire.code_postal} "
                                 f"{rib.adresse_titulaire.ville}",
        },
        tags=["titulaire-personne" if rib.titulaire_est_personne
              else "titulaire-entreprise"],
    )


PRODUCTEURS = {"facture": produire_facture, "rib": produire_rib}

# Les types produits et les types que le manifest accepte sont declares dans deux
# modules, `manifest` ne pouvant pas importer `generer` sans cycle. Le controle echoue
# bruyamment plutot que de laisser le validateur signaler chaque nouveau document comme
# un type inconnu.
assert set(PRODUCTEURS) == manifest.TYPES_CONNUS, (
    f"types produits {sorted(PRODUCTEURS)} et types connus du manifest "
    f"{sorted(manifest.TYPES_CONNUS)} ne concordent pas")


def main() -> None:
    if len(sys.argv) != 3 or sys.argv[1] not in PRODUCTEURS:
        types = ", ".join(PRODUCTEURS)
        print(f"Usage : python generer.py <{types}> <nombre>")
        sys.exit(1)

    producteur = PRODUCTEURS[sys.argv[1]]

    entrees = []
    for numero in range(1, int(sys.argv[2]) + 1):
        propre = producteur(numero)
        entrees.append(propre)
        jumeau = _jumeau_scanne(propre, numero)
        if jumeau is not None:
            entrees.append(jumeau)

    manifest.enregistrer(entrees)

    for entree in entrees:
        print(f"  {entree['id']:<12} {entree['split']:<4} {entree['regime']:<12} "
              f"{', '.join(entree['tags'])}")
    print(f"\n{len(entrees)} document(s) ecrits, manifest mis a jour.")


if __name__ == "__main__":
    main()
