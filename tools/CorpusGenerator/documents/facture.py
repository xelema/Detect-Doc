"""Facture de formation professionnelle.

Son voisin le plus confondable est le devis : mêmes parties, mêmes montants, même pavé
légal. Le tronc commun aux deux types, et la façon dont leur présentation varie, sont
dans `documents/piece.py`.
"""

from dataclasses import dataclass
from datetime import date, timedelta
from decimal import Decimal

import manifest
from configuration import flux
from documents.base import DocumentType
from documents.commun import (CATALOGUE_FORMATIONS, DEBUT_PERIODE, FIN_PERIODE,
                              client_professionnel, faker, generer_client,
                              generer_organisme)
from documents.piece import (Piece, Style, lignes_formation, pii, stagiaires_piece,
                             style_piece, tags)
from documents.rib import generer_rib
from rendu import logo_svg, rendre_pdf, texte_natif


@dataclass
class Facture(Piece):
    date_execution: date
    date_echeance: date
    # Les quatre mentions ajoutées par la réforme de la facturation électronique entrent
    # en vigueur par paliers, septembre 2026 puis septembre 2027. La production contient
    # donc un mélange des deux formes, le corpus doit le refléter.
    nouvelles_mentions: bool
    categorie_operation: str

    libelle = "Facture"


def generer_facture(seed: int, professionnel: bool = True) -> Facture:
    organisme = generer_organisme(seed)
    client = generer_client(seed + 1, professionnel)
    rib = generer_rib(seed + 2, organisme.raison_sociale, organisme.adresse)

    # Construit après les appels ci-dessus : `faker()` reseede une instance partagée, la
    # prendre plus tôt la ferait dériver au passage des générateurs imbriqués au lieu de
    # démarrer sur `seed`, cf. le docstring de `documents.commun.faker`.
    fake = faker(seed)
    intitule, duree = fake.random_element(CATALOGUE_FORMATIONS)
    stagiaires = stagiaires_piece(fake, professionnel)

    # Un organisme exonéré facture sans TVA, sinon le taux normal s'applique.
    taux = Decimal("0") if organisme.exonere_tva else Decimal("20")
    lignes = lignes_formation(fake, intitule, duree, len(stagiaires), taux)

    jours = fake.random_int(0, (FIN_PERIODE - DEBUT_PERIODE).days)
    date_execution = DEBUT_PERIODE + timedelta(days=jours)
    date_emission = date_execution + timedelta(days=fake.random_int(1, 10))

    return Facture(
        numero=f"FA{date_emission.year}-{fake.numerify('####')}",
        date_emission=date_emission,
        organisme=organisme,
        client=client,
        lignes=lignes,
        reference_dossier=fake.numerify("CONV-####-###"),
        intitule_action=intitule,
        stagiaires=stagiaires,
        coordonnees_bancaires=rib,
        date_execution=date_execution,
        date_echeance=date_emission + timedelta(days=30),
        nouvelles_mentions=fake.boolean(chance_of_getting_true=50),
        categorie_operation="Prestations de services",
    )


def style_facture(seed: int) -> Style:
    return style_piece(seed, flux("facture", seed), devis=False)


class FactureDocument(DocumentType):
    type_attendu = "facture"

    def produire(self, numero: int) -> dict:
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

        etiquettes = tags(facture, style)
        if facture.nouvelles_mentions:
            etiquettes.append("mentions-reforme")

        return manifest.construire_entree(
            identifiant, chemin, self.type_attendu, numero, pages,
            variante=style.as_dict(),
            pii=pii(facture, style),
            tags=etiquettes,
            texte_reference=texte_natif(chemin),
        )


DOCUMENT = FactureDocument()
