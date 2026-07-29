"""Point d'entrée du générateur de corpus.

Usage :
    .venv/bin/python generer.py

Les types et quantités viennent du fichier TOML global.
"""

import manifest
from configuration import DOCUMENTS, FICHIER, SHA256
from data import (client_professionnel, generer_carte_visite, generer_devis,
                  generer_facture, generer_rib)
from degradation import degrader, profil_acquisition, profil_degradation
from rendu import logo_svg, portrait_svg, qr_svg, rendre_pdf
from styles import style_carte_visite, style_devis, style_facture, style_rib


def _jumeau_scanne(propre: dict, seed: int) -> dict | None:
    """Version scannée d'un document, si la seed lui en attribue une."""
    profil = profil_degradation(seed)
    if profil is None:
        return None

    identifiant = f"{propre['id']}-d"
    chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"
    effets = degrader(manifest.DOSSIER_CORPUS / propre["fichier"], chemin, seed, profil)

    return manifest.entree_derivee(
        propre, identifiant, chemin,
        regime="scanne", degradations=effets, tags=propre["tags"] + [profil])


def _pii_piece(piece) -> dict:
    """Vérité terrain des données personnelles d'une facture ou d'un devis.

    Les deux types portent exactement les mêmes identifiants aux mêmes endroits : c'est
    d'ailleurs ce qui les rend confondables. Un seul extracteur pour les deux évite que
    l'un des deux se mette à déclarer moins que l'autre sans raison.
    """
    return {
        "raison_sociale_emetteur": piece.organisme.raison_sociale,
        "siret_emetteur": piece.organisme.siret,
        "email_emetteur": piece.organisme.email,
        "telephone_emetteur": piece.organisme.telephone,
        "designation_client": piece.client.designation,
        "contact_client": piece.client.contact,
        "iban": piece.coordonnees_bancaires.iban,
        "noms": piece.stagiaires,
    }


def _tags_piece(piece, style) -> list[str]:
    tags = ["b2b" if piece.client.est_professionnel else "b2c"]
    if piece.organisme.exonere_tva:
        tags.append("tva-exoneree")
    if style.filigrane:
        tags.append("filigrane")
    return tags


def produire_facture(numero: int) -> dict:
    facture = generer_facture(numero, professionnel=client_professionnel(numero))
    style = style_facture(numero)
    identifiant = f"fac-{numero:04d}"
    chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"

    pages = rendre_pdf(
        style.template, chemin,
        p=facture,
        s=style,
        logo=logo_svg(facture.organisme.raison_sociale, style.couleur),
        # Date portée par le tampon à l'encre, quand le style en pose un.
        date_marque=facture.date_echeance,
    )

    tags = _tags_piece(facture, style)
    if facture.nouvelles_mentions:
        tags.append("mentions-reforme")

    return manifest.construire_entree(
        identifiant, chemin, "facture", numero, pages,
        variante=style.as_dict(),
        pii=_pii_piece(facture),
        tags=tags,
    )


def produire_devis(numero: int) -> dict:
    devis = generer_devis(numero, professionnel=client_professionnel(numero))
    style = style_devis(numero)
    identifiant = f"dev-{numero:04d}"
    chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"

    pages = rendre_pdf(
        style.template, chemin,
        p=devis,
        s=style,
        logo=logo_svg(devis.organisme.raison_sociale, style.couleur),
        # Un devis se tamponne le jour où il est accepté, donc au plus tard à l'échéance
        # de sa validité.
        date_marque=devis.date_validite,
    )

    # L'intitulé et le cartouche sont les deux marqueurs qui séparent le devis de la
    # facture. Les écrire dans le manifest permettra de mesurer leur poids réel : un
    # classifieur qui ne tiendrait que grâce au mot « devis » s'effondrera sur les
    # « proposition commerciale », et le rapport doit pouvoir le montrer.
    tags = _tags_piece(devis, style)
    tags.append(devis.libelle.split()[0].lower())
    if style.avec_acceptation:
        tags.append("cartouche-accord")
    if devis.acompte_pourcentage:
        tags.append("acompte")

    return manifest.construire_entree(
        identifiant, chemin, "devis", numero, pages,
        variante=style.as_dict(),
        pii=_pii_piece(devis),
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


def produire_carte_visite(numero: int) -> dict:
    carte = generer_carte_visite(numero)
    style = style_carte_visite(numero)
    identifiant = f"cvi-{numero:04d}"
    chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"

    pages = rendre_pdf(
        style.template, chemin,
        c=carte,
        s=style,
        logo=logo_svg(carte.organisme, style.couleur),
        qr=qr_svg(numero),
        portrait=portrait_svg(style.couleur),
    )

    tags = [style.acquisition, style.orientation]
    if style.angle_deg in (90, 270):
        tags.append("quart-de-tour")
    if style.manuscrite:
        tags.append("manuscrite")
    if style.verso != "aucun":
        tags.append(f"verso-{style.verso}")
    if not style.avec_logo:
        tags.append("sans-logo")

    # Seules les coordonnées réellement imprimées sont déclarées. Le manifest sert de
    # vérité terrain au masquage : y annoncer une donnée absente du document la ferait
    # compter plus tard comme une détection manquée, et le rappel mesuré serait faux.
    pii = {
        "nom_complet": carte.nom_complet,
        "email": carte.email,
        "telephone_mobile": carte.mobile,
        "organisme": carte.organisme,
    }
    if style.avec_fixe:
        pii["telephone_fixe"] = carte.fixe
    if style.avec_adresse:
        pii["adresse"] = carte.adresse_complete
    if style.avec_reseau:
        pii["reseau_social"] = carte.reseau

    return manifest.construire_entree(
        identifiant, chemin, "carte_visite", numero, pages,
        variante=style.as_dict(),
        pii=pii,
        tags=tags,
    )


PRODUCTEURS = {
    "facture": produire_facture,
    "devis": produire_devis,
    "rib": produire_rib,
    "carte_visite": produire_carte_visite,
}

# Types dont le document déposé est la reprise d'un objet physique. Une carte de visite
# arrive presque toujours photographiée ou scannée, pas sous forme de fichier : en
# produire autant d'exemplaires propres que de reprises en ferait le type le plus facile
# du corpus, alors que c'est le plus pauvre en signal, et fausserait du même coup la
# comparaison entre texte natif et scan sur tout le corpus.
#
# « Presque toujours » et non « toujours » : le style tire une minorité de documents en
# mode `fichier`, l'export d'un outil de mise en page déposé tel quel. Ceux-là restent à
# texte natif. Le cas est rare mais il existe, et un corpus qui n'en contiendrait aucun
# ne dirait rien de la façon dont le service le traite.
TYPES_PHYSIQUES = {"carte_visite"}

# Les types produits et les types que le manifest accepte sont déclarés dans deux
# modules, manifest ne pouvant pas importer generer sans cycle. Le contrôle échoue
# bruyamment plutôt que de laisser le validateur signaler chaque nouveau document comme
# un type inconnu.
assert set(PRODUCTEURS) == manifest.TYPES_CONNUS, (
    f"types produits {sorted(PRODUCTEURS)} et types connus du manifest "
    f"{sorted(manifest.TYPES_CONNUS)} ne concordent pas")


def _reprise_physique(propre: dict, seed: int) -> dict:
    """Numérise un document repris d'un objet physique, sans conserver l'original.

    Le rendu propre n'est ici qu'une étape de fabrication, l'équivalent du fichier de
    l'imprimeur : il est écrasé par sa reprise et n'entre jamais au manifest, qui ne doit
    décrire que ce qui existe. L'entrée reste donc autonome, sans `derive_de`, à la
    différence du jumeau scanné qui accompagne un original bien réel.

    Le mode `fichier` est rendu tel quel : c'est justement le document que personne n'a
    photographié, et il doit rester à texte natif.

    Écrire la numérisation par-dessus sa source est sans danger : la rasterisation ferme
    le fichier d'entrée avant que le nouveau PDF ne soit écrit.
    """
    acquisition = propre["variante"]["acquisition"]
    if acquisition == "fichier":
        return propre

    fichier = manifest.DOSSIER_CORPUS / propre["fichier"]
    profil = profil_acquisition(seed, acquisition)
    effets = degrader(fichier, fichier, seed, profil)

    return {
        **propre,
        "sha256": manifest.hash_fichier(fichier),
        "regime": "scanne",
        "degradations": effets,
        "tags": propre["tags"] + [profil],
    }


def _produire(type_document: str, nombre: int) -> list[dict]:
    producteur = PRODUCTEURS[type_document]
    entrees = []
    for numero in range(1, nombre + 1):
        propre = producteur(numero)

        if type_document in TYPES_PHYSIQUES:
            entrees.append(_reprise_physique(propre, numero))
            continue

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
