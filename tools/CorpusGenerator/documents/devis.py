"""Devis, proposition chiffrée émise avant la prestation.

Voisin le plus confondable de la facture : mêmes parties, mêmes montants, même pavé
légal. Ce qui l'en distingue tient dans son intitulé, sa durée de validité, ses dates au
futur, son acompte et son cartouche d'acceptation, et rien d'autre. C'est précisément ce
jeu de marqueurs que la classification devra savoir lire. Le tronc commun aux deux types
est dans `documents/piece.py`.
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
from rendu import logo_svg, rendre_pdf

# Intitulés sous lesquels un devis circule réellement. Le premier est de loin le plus
# fréquent, d'où sa répétition : le tirage suit la réalité plutôt qu'une équiprobabilité
# qui rendrait le corpus trompeur.
LIBELLES_DEVIS = ["Devis", "Devis", "Devis", "Proposition commerciale", "Offre de prix"]

# Préfixes de numérotation d'un devis, distincts de ceux d'une facture. C'est le premier
# indice que lit un humain, et il ne doit donc pas être unique dans le corpus.
PREFIXES_DEVIS = ["DEV", "D", "DV", "PRO"]

# Durées de validité rencontrées sur des devis de formation. Trois mois est courant chez
# les organismes, un mois chez les prestataires de service.
DUREES_VALIDITE_JOURS = [15, 30, 30, 45, 60, 90]

# Part demandée à la commande. Zéro est fréquent en formation professionnelle, où le
# financement passe souvent par un OPCO : l'acompte disparaît alors du document.
ACOMPTES_POURCENT = [0, 0, 30, 30, 50]

# Le devis n'est pas tiré sur la même seed que la facture de même numéro : sans ce
# décalage, devis n° 1 et facture n° 1 sortiraient avec le même organisme, le même
# client, la même formation et les mêmes montants. Le corpus contiendrait alors des
# paires de quasi-jumeaux textuels au lieu de documents distincts, et la matrice de
# confusion facture / devis mesurerait un cas de figure qui n'existe pas en production.
DECALAGE_SEED_DEVIS = 500


@dataclass
class Devis(Piece):
    # « Devis » domine, mais « proposition commerciale » et « offre de prix » désignent
    # le même document et se rencontrent tels quels en gestion documentaire. Un
    # classifieur qui ne reconnaîtrait que le premier mot passerait à côté.
    libelle: str
    date_validite: date
    duree_validite_jours: int
    date_debut_prevue: date
    delai_realisation: str
    # 0 quand l'organisme ne demande pas d'acompte : la mention disparaît alors du
    # document, ce qui le rapproche encore d'une facture.
    acompte_pourcentage: int

    est_devis = True
    libelle_reference = "Référence"

    @property
    def montant_acompte(self) -> Decimal:
        return (self.total_ttc * self.acompte_pourcentage / 100).quantize(Decimal("0.01"))


def generer_devis(seed: int, professionnel: bool = True) -> Devis:
    seed_devis = seed + DECALAGE_SEED_DEVIS

    organisme = generer_organisme(seed_devis)
    client = generer_client(seed_devis + 1, professionnel)
    rib = generer_rib(seed_devis + 2, organisme.raison_sociale, organisme.adresse)

    # Construit après les appels ci-dessus : `faker()` reseede une instance partagée, et
    # la prendre plus tôt la ferait dériver au passage des générateurs imbriqués au lieu
    # de démarrer sur `seed_devis`, cf. le docstring de `documents.commun.faker`.
    fake = faker(seed_devis)
    intitule, duree = fake.random_element(CATALOGUE_FORMATIONS)
    stagiaires = stagiaires_piece(fake, professionnel)

    taux = Decimal("0") if organisme.exonere_tva else Decimal("20")
    lignes = lignes_formation(fake, intitule, duree, len(stagiaires), taux)

    # Un devis précède la prestation : sa date d'émission est antérieure au début prévu,
    # là où la facture suit une exécution déjà passée. C'est l'inversion la plus
    # discriminante entre les deux documents, et elle doit se lire dans les dates
    # elles-mêmes, pas seulement dans les intitulés.
    jours = fake.random_int(0, (FIN_PERIODE - DEBUT_PERIODE).days)
    date_emission = DEBUT_PERIODE + timedelta(days=jours)
    duree_validite = fake.random_element(DUREES_VALIDITE_JOURS)
    date_debut_prevue = date_emission + timedelta(days=fake.random_int(15, 90))

    # Une journée de formation compte sept heures, décompte usuel du secteur. Le calcul
    # ne s'affiche que quand la durée tombe juste : sinon "4 journées de 7 heures"
    # afficherait 28 h pour une formation facturée 30 h, une incohérence que le devis ne
    # doit jamais porter.
    nb_journees = duree // 7
    delai = (f"{nb_journees} journée{'s' if nb_journees > 1 else ''} de 7 heures"
             if nb_journees and duree % 7 == 0 else f"{duree} heures")

    return Devis(
        numero=f"{fake.random_element(PREFIXES_DEVIS)}{date_emission.year}"
               f"-{fake.numerify('####')}",
        date_emission=date_emission,
        organisme=organisme,
        client=client,
        lignes=lignes,
        reference_dossier=fake.numerify("AFF-####-###"),
        intitule_action=intitule,
        stagiaires=stagiaires,
        coordonnees_bancaires=rib,
        libelle=fake.random_element(LIBELLES_DEVIS),
        date_validite=date_emission + timedelta(days=duree_validite),
        duree_validite_jours=duree_validite,
        date_debut_prevue=date_debut_prevue,
        delai_realisation=delai,
        acompte_pourcentage=fake.random_element(ACOMPTES_POURCENT),
    )


def style_devis(seed: int) -> Style:
    """Style d'un devis.

    Le tirage passe par un flux nommé, sinon le devis n° 1 et la facture n° 1 sortiraient
    non seulement sur le même template, ce qui est voulu, mais aussi avec la même police,
    le même corps et la même couleur, ce qui réduirait la variété du corpus de moitié.
    """
    return style_piece(seed, flux("devis", seed), devis=True)


class DevisDocument(DocumentType):
    type_attendu = "devis"

    def produire(self, numero: int) -> dict:
        devis = generer_devis(numero, professionnel=client_professionnel(numero))
        style = style_devis(numero)
        identifiant = f"dev-{numero:04d}"
        chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"

        pages = rendre_pdf(
            style.template, chemin,
            p=devis,
            s=style,
            logo=logo_svg(devis.organisme.raison_sociale, style.couleur),
            # Un devis se tamponne le jour où il est accepté, donc au plus tard à
            # l'échéance de sa validité.
            date_marque=devis.date_validite,
        )

        # L'intitulé et le cartouche sont les deux marqueurs qui séparent le devis de la
        # facture. Les écrire dans le manifest permettra de mesurer leur poids réel : un
        # classifieur qui ne tiendrait que grâce au mot « devis » s'effondrera sur les
        # « proposition commerciale », et le rapport doit pouvoir le montrer.
        etiquettes = tags(devis, style)
        etiquettes.append(devis.libelle.split()[0].lower())
        if style.avec_acceptation:
            etiquettes.append("cartouche-accord")
        if devis.acompte_pourcentage:
            etiquettes.append("acompte")

        return manifest.construire_entree(
            identifiant, chemin, self.type_attendu, numero, pages,
            variante=style.as_dict(),
            pii=pii(devis, style),
            tags=etiquettes,
        )


DOCUMENT = DevisDocument()
