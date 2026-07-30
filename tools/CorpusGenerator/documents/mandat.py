"""Tronc commun du mandat de prélèvement SEPA et de sa variante fusionnée avec le RIB.

Un créancier, ici l'organisme de formation, y autorise le prélèvement du compte d'un
débiteur. `SEPA` et `RIB_SEPA` sont un seul et même cartouche, RUM et ICS compris : ce qui
les distingue est la présence ou l'absence du pavé de codes bancaires du RIB au-dessus,
exactement comme la facture et le devis partagent leur tronc commun dans
`documents/piece.py` et ne varient que sur quelques blocs. `documents/sepa.py` et
`documents/rib_sepa.py` n'ajoutent donc chacun que leur `type_attendu` et l'appel à
`produire_mandat` avec le bon booléen.
"""

import random
from dataclasses import asdict, dataclass
from datetime import date, timedelta
from pathlib import Path

import manifest
from cles import identifiant_creancier_sepa
from documents.commun import (COULEURS, DEBUT_PERIODE, FIN_PERIODE, Organisme,
                              POLICES_SANS, faker, generer_organisme, sans_accent)
from documents.rib import Rib, generer_rib
from rendu import logo_svg, rendre_pdf

# Décalages appliqués à la seed du document pour dériver le débiteur et le créancier.
# Sans eux, le mandat n°1 réutiliserait exactement le même RIB et le même organisme que
# la facture ou le devis n°1 : le corpus contiendrait des quasi-jumeaux textuels au lieu
# de documents distincts, comme `documents/devis.py` décale déjà sa seed de 500 vis-à-vis
# de la facture pour la même raison.
DECALAGE_SEED_DEBITEUR = 1000
DECALAGE_SEED_CREANCIER = 1500

# Code d'activité que porte la grande majorité des créanciers hors secteur bancaire, une
# convention réelle de l'ICS plutôt qu'une valeur inventée pour l'occasion.
CODE_ACTIVITE_ICS = "ZZZ"


@dataclass
class MandatSepa:
    """Cartouche de mandat de prélèvement SEPA, adossé au débiteur d'un `Rib`."""

    rum: str
    ics: str
    type_paiement: str  # "recurrent" ou "ponctuel"
    creancier: Organisme
    lieu_signature: str
    date_signature: date

    @property
    def libelle_type_paiement(self) -> str:
        return ("Paiement récurrent / répétitif" if self.type_paiement == "recurrent"
                else "Paiement ponctuel")


@dataclass
class StyleMandat:
    """Présentation d'un mandat SEPA, sur le même axe que le RIB : peu variable, cf. le
    docstring de `documents.rib`."""

    template: str
    police: str
    police_mono: str
    corps_pt: float
    couleur: str
    avec_logo: bool
    # Vrai pour RIB_SEPA, faux pour un mandat isolé : seul ce champ distingue les deux
    # types au rendu, cf. le docstring du module.
    avec_pave_rib: bool

    def as_dict(self) -> dict:
        return asdict(self)


def generer_mandat_sepa(seed: int, rib: Rib) -> MandatSepa:
    fake = faker(seed + DECALAGE_SEED_CREANCIER)
    tirage = random.Random(f"mandat-{seed}")

    creancier = generer_organisme(seed + DECALAGE_SEED_CREANCIER)

    identifiant_national = fake.numerify("######")
    ics = identifiant_creancier_sepa(CODE_ACTIVITE_ICS, identifiant_national)

    # La référence unique de mandat n'a pas de forme imposée, seulement une longueur
    # maximale de 35 caractères : les initiales du créancier suivies d'un numéro de
    # dossier sont la convention la plus répandue.
    initiales = "".join(mot[0] for mot in sans_accent(creancier.raison_sociale).upper().split())
    rum = f"{initiales[:4]}{fake.numerify('########')}"

    jours = fake.random_int(0, (FIN_PERIODE - DEBUT_PERIODE).days)
    date_signature = DEBUT_PERIODE + timedelta(days=jours)

    return MandatSepa(
        rum=rum,
        ics=ics,
        # Un prélèvement de frais de formation échelonné est la norme, le paiement
        # ponctuel en solde reste minoritaire.
        type_paiement="ponctuel" if tirage.random() < 0.25 else "recurrent",
        creancier=creancier,
        lieu_signature=rib.adresse_titulaire.ville,
        date_signature=date_signature,
    )


def style_mandat(seed: int, avec_pave_rib: bool) -> StyleMandat:
    tirage = random.Random(f"mandat-style-{seed}")
    return StyleMandat(
        template="mandat_sepa_01.html",
        police=tirage.choice(POLICES_SANS),
        police_mono="DejaVu Sans Mono",
        corps_pt=tirage.choice([9.0, 9.5, 10.0]),
        couleur=tirage.choice(COULEURS),
        avec_logo=True,
        avec_pave_rib=avec_pave_rib,
    )


def produire_mandat(numero: int, type_attendu: str, prefixe: str,
                    avec_pave_rib: bool) -> dict:
    """Produit un mandat SEPA ou sa variante RIB_SEPA. Partagé par les deux classes
    `DocumentType`, qui ne diffèrent que par ce booléen, cf. le docstring du module."""
    rib = generer_rib(numero + DECALAGE_SEED_DEBITEUR)
    mandat = generer_mandat_sepa(numero, rib)
    style = style_mandat(numero, avec_pave_rib)
    identifiant = f"{prefixe}-{numero:04d}"
    chemin: Path = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"

    pages = rendre_pdf(
        style.template, chemin,
        r=rib,
        m=mandat,
        s=style,
        logo=logo_svg(mandat.creancier.raison_sociale, style.couleur),
    )

    return manifest.construire_entree(
        identifiant, chemin, type_attendu, numero, pages,
        variante=style.as_dict(),
        pii={
            "titulaire": rib.titulaire,
            "iban": rib.iban,
            "bic": rib.bic,
            "adresse_titulaire": f"{rib.adresse_titulaire.ligne1}, "
                                 f"{rib.adresse_titulaire.code_postal} "
                                 f"{rib.adresse_titulaire.ville}",
            "rum": mandat.rum,
        },
        tags=["titulaire-personne" if rib.titulaire_est_personne
              else "titulaire-entreprise", mandat.type_paiement],
    )
