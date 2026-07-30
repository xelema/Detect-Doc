"""Vérifie que les identifiants générés portent des clés de contrôle justes.

À lancer après toute modification de la génération de données. Un corpus dont les IBAN
ou les SIREN sont invalides est inexploitable pour mesurer le masquage des données
personnelles, et se repère immédiatement comme factice.
"""

import subprocess
import tempfile
from decimal import Decimal
from pathlib import Path

import fitz
from stdnum import iban as iban_stdnum
from stdnum.fr import siren as siren_stdnum
from stdnum.fr import siret as siret_stdnum
from stdnum.fr import tva as tva_stdnum

from cles import cle_rib
from documents.carte_visite import (ACCROCHES, CERTIFICATIONS, FONCTIONS,
                                    POLICES_CARTE, POLICES_CARTE_RONDES,
                                    POLICES_CARTE_SERIF, generer_carte_visite)
from documents.commun import (MOTIFS_CLIENT, MOTIFS_ORGANISME, VILLES,
                              POLICES_CONDENSEES, POLICES_MANUSCRITES, POLICES_MONO,
                              POLICES_SANS, POLICES_SERIF, client_professionnel)
from documents.devis import generer_devis, style_devis
from documents.facture import generer_facture, style_facture
from documents.feuille_emargement import (ANNOTATIONS_EMARGEMENT, FAMILLES_EMARGEMENT,
                                          FONCTIONS_STAGIAIRES, NATURES_PRESENCE,
                                          generer_feuille_emargement,
                                          style_feuille_emargement)
from manifest import hash_fichier
from rendu import logo_svg, montant_en_lettres, rendre_pdf

NB_ECHANTILLONS = 50

# Toutes les familles que le générateur peut demander, quel que soit le type de document.
# Assemblée ici plutôt que dans documents/, qui n'a pas besoin d'un registre agrégé :
# seul ce vérificateur contrôle qu'elles sont installées et couvrent le français.
POLICES_UTILISEES = sorted(set(
    POLICES_SANS + POLICES_SERIF + POLICES_MONO + POLICES_CONDENSEES
    + POLICES_CARTE + POLICES_CARTE_RONDES + POLICES_CARTE_SERIF + POLICES_MANUSCRITES
))

# Valeurs choisies pour couvrir les pièges du français : la plage 17 à 19 qui n'a pas de
# nom de dizaine, la conjonction du "et un", les vingtaines au-delà de soixante, et le
# pluriel de "quatre-vingts" et de "cents" quand aucun nombre ne les suit.
MONTANTS_ATTENDUS = {
    0: "zero euro",
    1: "un euro",
    17: "dix-sept euros",
    21: "vingt et un euros",
    71: "soixante et onze euros",
    75: "soixante-quinze euros",
    80: "quatre-vingts euros",
    81: "quatre-vingt-un euros",
    99: "quatre-vingt-dix-neuf euros",
    100: "cent euros",
    200: "deux cents euros",
    203: "deux cent trois euros",
    1000: "mille euros",
    2772: "deux mille sept cent soixante-douze euros",
    91480: "quatre-vingt-onze mille quatre cent quatre-vingts euros",
}


def verifier_piece(piece) -> list[str]:
    """Anomalies d'identifiants d'une facture ou d'un devis, vide si tout est bon."""
    anomalies = []
    org = piece.organisme
    rib = piece.coordonnees_bancaires

    if not siren_stdnum.is_valid(org.siren):
        anomalies.append(f"SIREN organisme invalide : {org.siren}")
    if not siret_stdnum.is_valid(org.siret):
        anomalies.append(f"SIRET organisme invalide : {org.siret}")
    if not tva_stdnum.is_valid(org.tva):
        anomalies.append(f"TVA organisme invalide : {org.tva}")
    if not org.siret.startswith(org.siren):
        anomalies.append("Le SIRET ne commence pas par le SIREN")

    if piece.client.est_professionnel:
        if not siren_stdnum.is_valid(piece.client.siren):
            anomalies.append(f"SIREN client invalide : {piece.client.siren}")
        if not tva_stdnum.is_valid(piece.client.tva):
            anomalies.append(f"TVA client invalide : {piece.client.tva}")

    if not iban_stdnum.is_valid(rib.iban):
        anomalies.append(f"IBAN invalide : {rib.iban}")
    if cle_rib(rib.code_banque, rib.code_guichet, rib.numero_compte) != rib.cle:
        anomalies.append(f"Clé RIB invalide : {rib.cle}")
    # Le pavé RIB imprimé et l'IBAN doivent porter les mêmes codes, sinon le document
    # se contredit lui-même et un lecteur attentif le voit.
    if rib.iban[4:] != f"{rib.code_banque}{rib.code_guichet}{rib.numero_compte}{rib.cle}":
        anomalies.append("L'IBAN ne correspond pas aux codes du pavé RIB")

    return anomalies


def verifier_devis(seed: int) -> list[str]:
    """Contrôles propres au devis, en plus de ceux communs aux deux types.

    Ils portent tous sur la chronologie. C'est elle qui distingue un devis d'une facture
    autrement que par un mot : un devis dont la prestation serait déjà passée, ou dont la
    validité serait expirée le jour de l'émission, enseignerait exactement le contraire
    de ce que le corpus doit enseigner, et rien dans le rendu ne le signalerait.
    """
    anomalies = []
    devis = generer_devis(seed, professionnel=client_professionnel(seed))

    if devis.date_validite <= devis.date_emission:
        anomalies.append(
            f"validité {devis.date_validite} antérieure à l'émission {devis.date_emission}")
    if devis.date_debut_prevue <= devis.date_emission:
        anomalies.append(
            f"début prévu {devis.date_debut_prevue} antérieur à l'émission "
            f"{devis.date_emission}")

    attendu = (devis.total_ttc * devis.acompte_pourcentage / 100).quantize(Decimal("0.01"))
    if devis.montant_acompte != attendu:
        anomalies.append(
            f"acompte {devis.montant_acompte} au lieu de {attendu}")

    return anomalies + verifier_piece(devis)


def _feuille_et_style(seed: int):
    style = style_feuille_emargement(seed)
    feuille = generer_feuille_emargement(
        seed,
        nombre_participants=style.lignes_par_page * style.nombre_pages,
        nombre_creneaux=style.nombre_creneaux,
        mensuel=style.mensuel,
    )
    return feuille, style


def verifier_feuille_emargement(seed: int) -> list[str]:
    """Contrôle la cohérence entre session, grille et participants."""
    feuille, style = _feuille_et_style(seed)
    anomalies = []

    if feuille.date_fin < feuille.date_debut:
        anomalies.append("la session se termine avant son début")
    if len(feuille.creneaux) != style.nombre_creneaux:
        anomalies.append(
            f"{len(feuille.creneaux)} créneaux au lieu de {style.nombre_creneaux}")
    if len(feuille.participants) != style.lignes_par_page * style.nombre_pages:
        anomalies.append("le nombre de participants ne remplit pas les pages prévues")
    if len(feuille.formateur.cases) != len(feuille.creneaux):
        anomalies.append("le bloc formateur ne couvre pas tous les créneaux")
    if not 0 <= feuille.taux_remplissage <= 1:
        anomalies.append(f"taux de remplissage invalide : {feuille.taux_remplissage}")

    if style.famille == "officiel-sectoriel" and not style.avec_bloc_formateur:
        anomalies.append("le formulaire sectoriel a perdu son intervenant")

    natures_connues = {
        "signature", "vide", "barre",
        *(annotation.lower() for annotation in ANNOTATIONS_EMARGEMENT),
    }
    for participant in feuille.participants:
        if len(participant.cases) != len(feuille.creneaux):
            anomalies.append(
                f"{participant.nom_complet} : nombre de cases incohérent")
        for case in participant.cases:
            if case.nature not in natures_connues:
                anomalies.append(
                    f"{participant.nom_complet} : nature inconnue {case.nature}")

        # Une feuille d'émargement sert à facturer : le total d'heures doit correspondre
        # aux seuls créneaux réellement suivis. Compter une case barrée ou un ABSENT
        # produirait une feuille qu'aucun financeur n'accepterait, et une vérité terrain
        # fausse pour la comparaison des extractions.
        presences = sum(case.nature in NATURES_PRESENCE
                        for case in participant.cases)
        if participant.heures_suivies != presences * 3.5:
            anomalies.append(
                f"{participant.nom_complet} : {participant.heures_suivies} h suivies "
                f"pour {presences} créneau(x) de présence")
        if participant.heures_suivies > participant.heures_souscrites:
            anomalies.append(
                f"{participant.nom_complet} : plus d'heures suivies que souscrites")

    return anomalies


def verifier_montants_en_lettres() -> list[str]:
    """Contrôle la conversion des montants en toutes lettres.

    Elle n'apparaît que sur une facture sur quatre, donc un montant mal converti peut
    rester invisible longtemps si personne ne le contrôle explicitement.
    """
    anomalies = []
    for valeur, attendu in MONTANTS_ATTENDUS.items():
        obtenu = montant_en_lettres(Decimal(valeur))
        if obtenu != attendu:
            anomalies.append(f"{valeur} : « {obtenu} » au lieu de « {attendu} »")

    # Aucun montant plausible ne doit faire échouer la conversion.
    for valeur in range(0, 100000, 37):
        try:
            montant_en_lettres(Decimal(valeur))
        except Exception as erreur:
            anomalies.append(f"{valeur} : {type(erreur).__name__} {erreur}")

    return anomalies


def _caracteres_produits() -> set[str]:
    """Tous les caractères que la génération peut poser dans un document.

    Les listes de vocabulaire sont prises telles quelles, plutôt qu'échantillonnées : une
    fonction ou une accroche rare passerait sinon entre les mailles. Les valeurs tirées,
    noms et adresses en tête, sont elles échantillonnées, faute de pouvoir les énumérer.
    """
    fixes = "".join(FONCTIONS + FONCTIONS_STAGIAIRES + ANNOTATIONS_EMARGEMENT
                    + ACCROCHES + CERTIFICATIONS + MOTIFS_ORGANISME
                    + MOTIFS_CLIENT + [ville for _, ville in VILLES])

    cartes = [generer_carte_visite(seed) for seed in range(1, NB_ECHANTILLONS + 1)]
    tires_cartes = "".join(
        f"{c.civilite}{c.nom_complet}{c.organisme}{c.email}{c.mobile}{c.fixe}"
        f"{c.site}{c.reseau}{c.adresse_complete}"
        for c in cartes)
    feuilles = [_feuille_et_style(seed)[0]
                for seed in range(1, NB_ECHANTILLONS + 1)]
    tires_feuilles = "".join(
        f"{f.organisme.raison_sociale}{f.intitule_action}{f.lieu}"
        f"{f.formateur.nom_complet}"
        + "".join(
            f"{p.nom_complet}{p.entreprise}{p.fonction}"
            for p in f.participants
        )
        for f in feuilles
    )

    # Séparateurs et symboles posés par les templates eux-mêmes, absents des données.
    templates = "·%()[]«»"

    return set(fixes + tires_cartes + tires_feuilles + templates)


# Le symbole monétaire n'apparaît que sur les pièces comptables. Les polices manuscrites
# ne servent qu'à l'identité portée au stylo sur une carte de visite et aux annotations
# d'une feuille d'émargement, où aucun montant ne figure : le leur réclamer écarterait de
# bonnes polices pour un caractère qu'elles ne seront jamais appelées à dessiner.
SYMBOLE_MONETAIRE = "€"


def _police_servie(famille: str) -> tuple[str, set[int]]:
    """Famille réellement servie par fontconfig, et les codes qu'elle sait dessiner.

    `fc-match` répond toujours quelque chose : quand la police demandée est absente, il
    sert sa police de secours sans le signaler. Comparer les deux noms est le seul moyen
    de savoir si la famille demandée existe vraiment.

    La couverture est lue sur fontconfig plutôt que dans le fichier de police, parce que
    les familles URW sont livrées en Type 1 et qu'aucun lecteur de tables OpenType ne
    sait les ouvrir. fontconfig, lui, les décrit comme les autres.
    """
    def interroger(motif: str) -> str:
        return subprocess.run(["fc-match", "-f", motif, famille],
                              capture_output=True, text=True, check=True).stdout.strip()

    codes = set()
    # Le charset se lit en plages hexadécimales, « 20-7e a0 152-153 ».
    for plage in interroger("%{charset}").split():
        debut, _, fin = plage.partition("-")
        codes.update(range(int(debut, 16), int(fin or debut, 16) + 1))

    return interroger("%{family[0]}"), codes


def verifier_polices() -> list[str]:
    """Contrôle que chaque police déclarée est installée et couvre le français.

    Deux pannes silencieuses, et c'est ce qui les rend dangereuses. Une police absente est
    remplacée sans un mot par la police de secours du système, et le corpus sort avec une
    variété typographique bien moindre que celle que le manifest annonce. Un caractère
    non dessiné est emprunté à une autre police, si bien qu'un nom peut sortir avec un É
    d'un autre corps et d'une autre graisse au milieu d'un mot.

    Aucune erreur n'est levée dans les deux cas, et le manifest continue d'affirmer que
    le document est en telle police. Seul ce contrôle le dit.
    """
    anomalies = []
    requis = _caracteres_produits()

    for famille in POLICES_UTILISEES:
        servie, couverts = _police_servie(famille)
        if servie.casefold() != famille.casefold():
            anomalies.append(f"{famille} : absente, fontconfig sert « {servie} »")
            continue

        attendus = requis if famille in POLICES_MANUSCRITES else requis | {SYMBOLE_MONETAIRE}
        manquants = sorted(c for c in attendus if ord(c) not in couverts)
        if manquants:
            anomalies.append(f"{famille} : ne dessine pas {''.join(manquants)}")

    return anomalies


def verifier_signatures_non_extractibles() -> list[str]:
    """Contrôle qu'aucun paraphe n'est resté du texte dans le PDF rendu.

    C'est le seul contrôle capable de voir la régression : une signature écrite dans une
    police manuscrite ressemble à un tracé sur la page, et rien ne la distingue à l'œil du
    dessin attendu. Sur un PDF à texte natif, elle livrerait pourtant le nom du signataire
    au premier copier-coller, alors qu'une feuille signée à la main ne le livre jamais.

    Les paraphes de la forme « P. Dupont » et « Paul D. » servent de sonde : ces chaînes
    n'apparaissent nulle part ailleurs dans la feuille, où les identités sont imprimées en
    entier. Les trouver dans le texte extrait ne peut vouloir dire qu'une chose.
    """
    anomalies = []

    for seed in range(1, len(FAMILLES_EMARGEMENT) + 1):
        feuille, style = _feuille_et_style(seed)
        groupes = [
            feuille.participants[index:index + style.lignes_par_page]
            for index in range(0, len(feuille.participants), style.lignes_par_page)
        ]
        with tempfile.TemporaryDirectory() as dossier:
            chemin = Path(dossier) / "emargement.pdf"
            rendre_pdf(
                style.template, chemin,
                f=feuille,
                s=style,
                groupes=groupes,
                logo=logo_svg(feuille.organisme.raison_sociale, style.couleur),
            )
            with fitz.open(chemin) as document:
                texte = "".join(page.get_text() for page in document)

        signataires = [*feuille.participants, feuille.formateur]
        sondes = {f"{p.prenom[0]}. {p.nom}" for p in signataires}
        sondes |= {f"{p.prenom} {p.nom[0]}." for p in signataires}
        for sonde in sorted(sondes):
            if sonde in texte:
                anomalies.append(
                    f"émargement {style.famille} : le paraphe « {sonde} » est "
                    "extractible comme texte")

    return anomalies


def verifier_reproductibilite() -> list[str]:
    """Rend deux fois le même document et compare les fichiers obtenus.

    C'est la promesse centrale de l'outil : le corpus n'est pas versionné parce qu'il se
    refabrique à l'identique. Elle repose sur plusieurs précautions éparpillées, dont un
    horodatage de police et un identifiant de document PDF, et elle casse sans lever la
    moindre erreur. Sans ce contrôle, seule une lecture attentive du diff du manifest
    révélerait la rupture.

    Les deux types sont éprouvés : ils partagent leurs templates, mais pas les blocs qui
    s'y affichent, et le cartouche d'acceptation n'apparaît que sur le devis.
    """
    anomalies = []
    documents = [
        ("facture", generer_facture(1, professionnel=True), style_facture(1)),
        ("devis", generer_devis(1, professionnel=True), style_devis(1)),
    ]

    for intitule, piece, style in documents:
        date_marque = piece.date_validite if piece.est_devis else piece.date_echeance
        empreintes = []
        with tempfile.TemporaryDirectory() as dossier:
            for essai in range(2):
                chemin = Path(dossier) / f"essai-{essai}.pdf"
                rendre_pdf(style.template, chemin, p=piece, s=style,
                           logo=logo_svg(piece.organisme.raison_sociale, style.couleur),
                           date_marque=date_marque)
                empreintes.append(hash_fichier(chemin))

        if empreintes[0] != empreintes[1]:
            anomalies.append(f"deux rendus du même {intitule} produisent des fichiers "
                             f"différents")

    for seed in range(1, 6):
        feuille, style = _feuille_et_style(seed)
        groupes = [
            feuille.participants[index:index + style.lignes_par_page]
            for index in range(0, len(feuille.participants), style.lignes_par_page)
        ]
        empreintes = []
        nombres_pages = []
        with tempfile.TemporaryDirectory() as dossier:
            for essai in range(2):
                chemin = Path(dossier) / f"emargement-{essai}.pdf"
                nombres_pages.append(rendre_pdf(
                    style.template, chemin,
                    f=feuille,
                    s=style,
                    groupes=groupes,
                    logo=logo_svg(feuille.organisme.raison_sociale, style.couleur),
                ))
                empreintes.append(hash_fichier(chemin))
        if empreintes[0] != empreintes[1]:
            anomalies.append(
                f"deux rendus du même émargement {style.famille} "
                "produisent des fichiers différents")
        if any(pages != style.nombre_pages for pages in nombres_pages):
            anomalies.append(
                f"émargement {style.famille} : nombre de pages différent de "
                f"{style.nombre_pages}")

    return anomalies


def main() -> None:
    total = 0
    controles_par_seed = (
        ("facture", lambda seed: verifier_piece(
            generer_facture(seed, professionnel=client_professionnel(seed)))),
        ("devis", verifier_devis),
        ("feuille émargement", verifier_feuille_emargement),
    )

    for intitule, controle in controles_par_seed:
        for seed in range(1, NB_ECHANTILLONS + 1):
            anomalies = controle(seed)
            total += len(anomalies)
            for anomalie in anomalies:
                print(f"  {intitule} seed {seed} : {anomalie}")

    for intitule, controle in (("montant en lettres", verifier_montants_en_lettres),
                               ("polices", verifier_polices),
                               ("signatures", verifier_signatures_non_extractibles),
                               ("reproductibilite", verifier_reproductibilite)):
        anomalies = controle()
        total += len(anomalies)
        for anomalie in anomalies:
            print(f"  {intitule}, {anomalie}")

    print(f"\n{NB_ECHANTILLONS} factures, {NB_ECHANTILLONS} devis et "
          f"{NB_ECHANTILLONS} feuilles d'émargement vérifiés, "
          f"{total} anomalie(s).")

    facture = generer_facture(1)
    print("\nExemple de facture, seed 1 :")
    print(f"  Émetteur   : {facture.organisme.raison_sociale} "
          f"({facture.organisme.forme_juridique})")
    print(f"  SIRET      : {facture.organisme.siret}")
    print(f"  TVA        : {facture.organisme.tva}")
    print(f"  NDA        : {facture.organisme.numero_declaration_activite}")
    print(f"  Exonéré    : {facture.organisme.exonere_tva}")
    print(f"  Client     : {facture.client.designation} "
          f"({'pro' if facture.client.est_professionnel else 'particulier'})")
    print(f"  Facture    : {facture.numero} du {facture.date_emission}")
    print(f"  Action     : {facture.intitule_action}")
    print(f"  Total HT   : {facture.total_ht} EUR")
    print(f"  Total TTC  : {facture.total_ttc} EUR")
    print(f"  IBAN       : {facture.coordonnees_bancaires.iban_formate}")
    print(f"  BIC        : {facture.coordonnees_bancaires.bic}")
    print(f"  Nouvelles mentions : {facture.nouvelles_mentions}")

    devis = generer_devis(1)
    print("\nExemple de devis, seed 1 :")
    print(f"  Émetteur   : {devis.organisme.raison_sociale}")
    print(f"  Client     : {devis.client.designation}")
    print(f"  Intitulé   : {devis.libelle} {devis.numero} du {devis.date_emission}")
    print(f"  Validité   : {devis.duree_validite_jours} jours, "
          f"jusqu'au {devis.date_validite}")
    print(f"  Début prévu: {devis.date_debut_prevue}, {devis.delai_realisation}")
    print(f"  Total TTC  : {devis.total_ttc} EUR")
    print(f"  Acompte    : {devis.acompte_pourcentage} % "
          f"({devis.montant_acompte} EUR)")


if __name__ == "__main__":
    main()
