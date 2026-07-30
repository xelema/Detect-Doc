"""Tronc commun de la facture et du devis.

Un organisme édite ses devis et ses factures avec le même logiciel : même papier à
en-tête, même tableau de lignes, mêmes totaux, même pavé légal. Ce qui les sépare tient
à quelques mentions, aux dates et au cartouche d'acceptation. Partager ce tronc ici
garantit que la différence reste là où elle est dans la réalité, au lieu d'être offerte
au classifieur sous forme de deux mises en page étrangères.

`documents/facture.py` et `documents/devis.py` importent d'ici leur dataclass de base,
leur style et leurs lignes chiffrées, et n'ajoutent que ce qui leur est propre.

Deux axes de présentation, qui ne se testent pas pareil et qu'il faut donc faire varier
séparément. La **structure** décide de ce que le classifieur voit : quels blocs
existent, où ils sont posés, comment les intitulés sont formulés. La **typographie**
décide de ce que l'OCR voit : famille, corps, contraste, filets du tableau.
"""

import random
from dataclasses import dataclass
from datetime import date
from decimal import Decimal

from faker import Faker

from documents.commun import (COULEURS, POLICES_CONDENSEES, POLICES_MONO, POLICES_SANS,
                              POLICES_SERIF, Client, Organisme, Style, marque)
from documents.rib import Rib


@dataclass
class Ligne:
    designation: str
    detail: str
    quantite: Decimal
    unite: str
    prix_unitaire_ht: Decimal
    taux_tva: Decimal

    @property
    def total_ht(self) -> Decimal:
        return (self.quantite * self.prix_unitaire_ht).quantize(Decimal("0.01"))

    @property
    def montant_tva(self) -> Decimal:
        return (self.total_ht * self.taux_tva / 100).quantize(Decimal("0.01"))


@dataclass
class Piece:
    """Tronc commun de la facture et du devis, cf. le docstring du module."""

    numero: str
    date_emission: date
    organisme: Organisme
    client: Client
    lignes: list[Ligne]
    # Convention de formation sur une facture, numéro d'affaire sur un devis : la
    # référence existe dans les deux cas, seul son intitulé imprimé change.
    reference_dossier: str
    intitule_action: str
    stagiaires: list[str]
    coordonnees_bancaires: Rib

    # Lus par les templates, qui servent les deux types. Des attributs de classe plutôt
    # que des champs : ces valeurs ne varient pas d'un document à l'autre.
    #
    # L'intitulé imprimé en tête, lui, est déclaré par chaque sous-classe et non ici :
    # une valeur posée sur Piece deviendrait la valeur par défaut du champ de même nom
    # du devis, qui doit rester obligatoire.
    est_devis = False
    libelle_reference = "Convention"

    @property
    def total_ht(self) -> Decimal:
        return sum((l.total_ht for l in self.lignes), Decimal("0.00"))

    @property
    def total_tva(self) -> Decimal:
        return sum((l.montant_tva for l in self.lignes), Decimal("0.00"))

    @property
    def total_ttc(self) -> Decimal:
        return self.total_ht + self.total_tva

    @property
    def taux_tva(self) -> Decimal:
        """Taux appliqué au document, pour que les templates ne l'écrivent pas en dur.

        Toutes les lignes portent le même taux : un organisme exonéré l'est pour tout
        le document. Le jour où un taux réduit apparaîtra, c'est ici qu'il faudra
        décider quoi afficher, pas dans les quatre templates.
        """
        return self.lignes[0].taux_tva


def lignes_formation(fake: Faker, intitule: str, duree: int, nb_stagiaires: int,
                     taux: Decimal) -> list[Ligne]:
    """Lignes chiffrées d'une action de formation.

    Identiques sur un devis et sur une facture : le montant proposé est celui qui sera
    facturé. Un corpus où les deux types n'auraient pas la même forme de tableau
    donnerait au classifieur un raccourci qui n'existe pas en production.
    """
    prix_heure = Decimal(fake.random_element([28, 35, 42, 55, 70]))

    lignes = [
        Ligne(
            designation=intitule,
            detail=f"Action de formation de {duree} heures, "
                   f"{nb_stagiaires} stagiaire{'s' if nb_stagiaires > 1 else ''}",
            quantite=Decimal(duree * nb_stagiaires),
            unite="heure",
            prix_unitaire_ht=prix_heure,
            taux_tva=taux,
        )
    ]
    if fake.boolean(chance_of_getting_true=40):
        lignes.append(
            Ligne(
                designation="Supports pédagogiques",
                detail=f"{nb_stagiaires} classeur{'s' if nb_stagiaires > 1 else ''} "
                       f"remis en début de session",
                quantite=Decimal(nb_stagiaires),
                unite="unite",
                prix_unitaire_ht=Decimal("45"),
                taux_tva=taux,
            )
        )
    return lignes


def _piece_moderne(tirage: random.Random, devis: bool) -> Style:
    """Mise en page des logiciels de facturation récents : logo, couleur, tableau dense."""
    return Style(
        template="piece_01.html",
        police=tirage.choice(POLICES_SANS),
        police_mono="DejaVu Sans Mono",
        corps_pt=tirage.choice([8.5, 9.0, 9.5]),
        couleur=tirage.choice(COULEURS),
        tableau=tirage.choice(["filets", "horizontaux", "bandes"]),
        avec_logo=tirage.random() < 0.85,
        avec_rib=tirage.random() < 0.8,
        avec_stagiaires=tirage.random() < 0.7,
        avec_reference=tirage.random() < 0.8,
        avec_acceptation=devis and tirage.random() < 0.8,
        **marque(tirage, 0.25, ["diagonal", "tampon"], devis),
    )


def _piece_classique(tirage: random.Random, devis: bool) -> Style:
    """Pièce de cabinet : serif, aucun logo, aucune couleur, montant en lettres.

    C'est la forme la plus difficile pour un classifieur, parce qu'elle ressemble à une
    lettre commerciale plus qu'à une sortie de logiciel.
    """
    return Style(
        template="piece_02.html",
        police=tirage.choice(POLICES_SERIF),
        police_mono="Nimbus Mono PS",
        corps_pt=tirage.choice([9.5, 10.0, 10.5]),
        couleur="",
        tableau="horizontaux",
        avec_logo=False,
        avec_rib=tirage.random() < 0.6,
        avec_stagiaires=tirage.random() < 0.4,
        avec_reference=tirage.random() < 0.5,
        # Un devis de cabinet part par courrier et revient signé : le cartouche y est
        # la règle plutôt que l'exception.
        avec_acceptation=devis and tirage.random() < 0.9,
        # Une pièce de cabinet est imprimée puis manipulée : elle porte un tampon à
        # l'encre plutôt qu'un filigrane, que seul un logiciel émetteur sait produire.
        **marque(tirage, 0.25, ["tampon"], devis),
    )


def _piece_listing(tirage: random.Random, devis: bool) -> Style:
    """Sortie d'un logiciel de gestion ancien : chasse fixe, majuscules, filets en tirets.

    Beaucoup d'ERP éditent encore comme ça. Le rendu est pauvre mais très régulier, ce
    qui en fait un cas intéressant : facile pour l'OCR, trompeur pour le classifieur.
    """
    return Style(
        template="piece_03.html",
        police=tirage.choice(POLICES_MONO),
        police_mono=tirage.choice(POLICES_MONO),
        corps_pt=tirage.choice([7.5, 8.0, 8.5]),
        couleur="",
        tableau="aucun",
        avec_logo=False,
        avec_rib=tirage.random() < 0.5,
        avec_stagiaires=tirage.random() < 0.5,
        # Un progiciel de gestion imprime toujours sa référence de dossier.
        avec_reference=True,
        avec_acceptation=devis and tirage.random() < 0.7,
        **marque(tirage, 0.2, ["tampon", "diagonal"], devis),
    )


def _piece_dense(tirage: random.Random, devis: bool) -> Style:
    """Pièce d'un service en ligne : bandeau coloré, condensé, petit corps, filigrane.

    Le cas le plus hostile à l'OCR du lot : corps réduit, police condensée, texte clair
    sur fond teinté, et parfois un filigrane par-dessus le contenu.
    """
    return Style(
        template="piece_04.html",
        police=tirage.choice(POLICES_CONDENSEES + ["Carlito"]),
        police_mono="Cousine",
        corps_pt=tirage.choice([7.0, 7.5, 8.0]),
        couleur=tirage.choice(COULEURS),
        tableau=tirage.choice(["aucun", "bandes"]),
        avec_logo=tirage.random() < 0.5,
        avec_rib=tirage.random() < 0.7,
        avec_stagiaires=tirage.random() < 0.5,
        avec_reference=tirage.random() < 0.6,
        # Un service en ligne fait accepter son devis d'un clic : le cartouche à signer
        # y est bien plus rare qu'ailleurs.
        avec_acceptation=devis and tirage.random() < 0.4,
        **marque(tirage, 0.4, ["diagonal", "repete", "tampon"], devis),
    )


# Les quatre mises en page servent les deux types. Un organisme édite son devis et sa
# facture avec le même logiciel, donc sur le même papier à en-tête : leur donner des
# templates distincts offrirait au classifieur un raccourci visuel qui n'existe pas en
# production, et masquerait la seule difficulté qui compte, celle du texte.
FABRIQUES_PIECE = [
    _piece_moderne,
    _piece_classique,
    _piece_listing,
    _piece_dense,
]


def style_piece(seed: int, tirage: random.Random, devis: bool) -> Style:
    """Style d'une facture ou d'un devis.

    Le template tourne sur la seed plutôt que d'être tiré au hasard : sur un corpus de
    taille modeste, un tirage aléatoire produirait des templates sous-représentés, et
    donc des mesures moins fiables là où on en a le plus besoin.
    """
    return FABRIQUES_PIECE[seed % len(FABRIQUES_PIECE)](tirage, devis)


def pii(piece: Piece) -> dict:
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


def tags(piece: Piece, style: Style) -> list[str]:
    valeurs = ["b2b" if piece.client.est_professionnel else "b2c"]
    if piece.organisme.exonere_tva:
        valeurs.append("tva-exoneree")
    if style.filigrane:
        valeurs.append("filigrane")
    return valeurs
