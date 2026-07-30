"""Relevé d'identité bancaire, document à part entière et pavé embarqué dans une pièce.

Volontairement peu variable dans sa présentation : dans la réalité, les RIB se
ressemblent presque tous, leur forme étant quasi normalisée. Inventer plusieurs
présentations rendrait le corpus moins représentatif, pas plus.
"""

import random
from dataclasses import dataclass
from datetime import date, timedelta

from stdnum import iban as iban_stdnum

import manifest
from cles import cle_rib, iban_francais
from documents.base import DocumentType
from documents.commun import (COULEURS, DEBUT_PERIODE, FIN_PERIODE, MOTIFS_CLIENT,
                              POLICES_SANS, Coordonnees, Style, coordonnees, faker,
                              marque)
from rendu import logo_svg, rendre_pdf

# Noms de banques inventés. Aucune banque réelle n'est nommée : le corpus doit être
# réaliste dans sa forme, pas usurper une marque existante.
BANQUES = [
    ("Banque du Cerisier", "BCERFRPP"),
    ("Crédit Méridien", "CMERFRP1"),
    ("Banque Lafayette et Associés", "BLAAFRPX"),
    ("Caisse de l'Ouvrage", "COUVFRP2"),
    ("Banque Atlantique du Ponant", "BAPOFRPP"),
]


@dataclass
class Rib:
    """Coordonnées bancaires, utilisées dans le pavé de bas de facture et comme
    document à part entière."""

    titulaire: str
    # Un RIB déposé en gestion documentaire appartient le plus souvent à une personne
    # physique. L'information est portée ici plutôt que devinée plus tard sur la forme du
    # nom : ajouter une civilité ou un motif de raison sociale ferait alors mentir le
    # manifest, sans que rien ne le signale.
    titulaire_est_personne: bool
    adresse_titulaire: Coordonnees
    banque: str
    bic: str
    agence: str
    adresse_agence: Coordonnees
    code_banque: str
    code_guichet: str
    numero_compte: str
    cle: str
    iban: str
    date_edition: date

    @property
    def iban_formate(self) -> str:
        """L'IBAN s'imprime par groupes de 4 caractères, comme sur un vrai relevé."""
        return iban_stdnum.format(self.iban)


def _titulaire_par_defaut(fake) -> tuple[str, bool]:
    """Titulaire d'un RIB isolé, et s'il s'agit d'une personne physique.

    Un RIB déposé en gestion documentaire est majoritairement celui d'un stagiaire ou
    d'un salarié, plus rarement celui d'une entreprise.
    """
    if fake.boolean(chance_of_getting_true=70):
        # Le prénom est tiré selon la civilité, sinon on produit des "M. Audrey".
        if fake.boolean():
            return f"M. {fake.first_name_male()} {fake.last_name()}", True
        return f"Mme {fake.first_name_female()} {fake.last_name()}", True
    return fake.random_element(MOTIFS_CLIENT).format(nom=fake.last_name()), False


def generer_rib(seed: int, titulaire: str | None = None,
                adresse: Coordonnees | None = None) -> Rib:
    fake = faker(seed)

    code_banque = fake.numerify("#####")
    code_guichet = fake.numerify("#####")
    numero_compte = fake.numerify("###########")
    cle = cle_rib(code_banque, code_guichet, numero_compte)

    banque, bic = fake.random_element(BANQUES)
    adresse_agence = coordonnees(fake)

    # Un titulaire imposé vient toujours de l'organisme émetteur d'une facture, donc
    # d'une entreprise.
    nom_titulaire, est_personne = (
        (titulaire, False) if titulaire else _titulaire_par_defaut(fake))

    return Rib(
        titulaire=nom_titulaire,
        titulaire_est_personne=est_personne,
        adresse_titulaire=adresse or coordonnees(fake),
        banque=banque,
        bic=bic,
        agence=f"Agence {adresse_agence.ville}",
        adresse_agence=adresse_agence,
        code_banque=code_banque,
        code_guichet=code_guichet,
        numero_compte=numero_compte,
        cle=cle,
        iban=iban_francais(code_banque, code_guichet, numero_compte, cle),
        date_edition=DEBUT_PERIODE + timedelta(
            days=fake.random_int(0, (FIN_PERIODE - DEBUT_PERIODE).days)),
    )


def style_rib(seed: int) -> Style:
    """Style d'un RIB, sur le même axe que la facture et le devis (cf. `documents.piece`).

    Volontairement peu variable : cf. le docstring du module.
    """
    tirage = random.Random(seed)
    return Style(
        template="rib_01.html",
        police=tirage.choice(POLICES_SANS),
        police_mono="DejaVu Sans Mono",
        corps_pt=tirage.choice([9.0, 9.5, 10.0]),
        couleur=tirage.choice(COULEURS),
        tableau="filets",
        avec_logo=True,
        avec_rib=True,
        avec_stagiaires=False,
        avec_reference=False,
        avec_acceptation=False,
        # Un relevé d'identité bancaire n'est ni tamponné ni filigrané.
        **marque(tirage, 0, []),
    )


class RibDocument(DocumentType):
    type_attendu = "rib"

    def produire(self, numero: int) -> dict:
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
            identifiant, chemin, self.type_attendu, numero, pages,
            variante=style.as_dict(),
            pii={
                "titulaire": rib.titulaire,
                "iban": rib.iban,
                "bic": rib.bic,
                "adresse_titulaire": rib.adresse_titulaire.formatee,
            },
            tags=["titulaire-personne" if rib.titulaire_est_personne
                  else "titulaire-entreprise"],
        )


DOCUMENT = RibDocument()
