"""Point d'entrée du générateur de corpus.

Usage :
    .venv/bin/python generer.py

Les types et quantités viennent du fichier TOML global.
"""

import manifest
from configuration import DOCUMENTS, FICHIER, SHA256
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


def _produire(type_document: str, nombre: int) -> list[dict]:
    producteur = PRODUCTEURS[type_document]
    entrees = []
    for numero in range(1, nombre + 1):
        propre = producteur(numero)
        entrees.append(propre)
        jumeau = _jumeau_scanne(propre, numero)
        if jumeau is not None:
            entrees.append(jumeau)
    return entrees


def main() -> None:
    types_inconnus = set(DOCUMENTS) - set(PRODUCTEURS)
    if types_inconnus:
        raise ValueError(
            f"type(s) inconnu(s) dans [documents] : {', '.join(sorted(types_inconnus))}")

    entrees = []
    for type_document, nombre in sorted(DOCUMENTS.items()):
        entrees.extend(_produire(type_document, nombre))

    manifest.enregistrer(entrees)

    for entree in entrees:
        print(f"  {entree['id']:<12} {entree['split']:<4} {entree['regime']:<12} "
              f"{', '.join(entree['tags'])}")
    print(f"\nConfiguration : {FICHIER}")
    print(f"Empreinte     : {SHA256}")
    print(f"{len(entrees)} document(s) écrits, manifest mis à jour.")


if __name__ == "__main__":
    main()
