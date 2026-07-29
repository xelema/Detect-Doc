"""Verifie que les identifiants generes portent des cles de controle justes.

A lancer apres toute modification de la generation de donnees. Un corpus dont les IBAN
ou les SIREN sont invalides est inexploitable pour mesurer le masquage des donnees
personnelles, et se repere immediatement comme factice.
"""

from decimal import Decimal

from stdnum import iban as iban_stdnum
from stdnum.fr import siren as siren_stdnum
from stdnum.fr import siret as siret_stdnum
from stdnum.fr import tva as tva_stdnum

from cles import cle_rib
from data import generer_facture
from rendu import montant_en_lettres

NB_ECHANTILLONS = 50

# Valeurs choisies pour couvrir les pieges du francais : la plage 17 a 19 qui n'a pas de
# nom de dizaine, la conjonction du "et un", les vingtaines au-dela de soixante, et le
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


def verifier(seed: int) -> list[str]:
    """Retourne la liste des anomalies trouvees sur une facture, vide si tout est bon."""
    anomalies = []
    facture = generer_facture(seed, professionnel=(seed % 4 != 0))
    org = facture.organisme
    rib = facture.coordonnees_bancaires

    if not siren_stdnum.is_valid(org.siren):
        anomalies.append(f"SIREN organisme invalide : {org.siren}")
    if not siret_stdnum.is_valid(org.siret):
        anomalies.append(f"SIRET organisme invalide : {org.siret}")
    if not tva_stdnum.is_valid(org.tva):
        anomalies.append(f"TVA organisme invalide : {org.tva}")
    if not org.siret.startswith(org.siren):
        anomalies.append("Le SIRET ne commence pas par le SIREN")

    if facture.client.est_professionnel:
        if not siren_stdnum.is_valid(facture.client.siren):
            anomalies.append(f"SIREN client invalide : {facture.client.siren}")
        if not tva_stdnum.is_valid(facture.client.tva):
            anomalies.append(f"TVA client invalide : {facture.client.tva}")

    if not iban_stdnum.is_valid(rib.iban):
        anomalies.append(f"IBAN invalide : {rib.iban}")
    if cle_rib(rib.code_banque, rib.code_guichet, rib.numero_compte) != rib.cle:
        anomalies.append(f"Cle RIB invalide : {rib.cle}")
    # Le pave RIB imprime et l'IBAN doivent porter les memes codes, sinon le document
    # se contredit lui-meme et un lecteur attentif le voit.
    if rib.iban[4:] != f"{rib.code_banque}{rib.code_guichet}{rib.numero_compte}{rib.cle}":
        anomalies.append("L'IBAN ne correspond pas aux codes du pave RIB")

    return anomalies


def verifier_montants_en_lettres() -> list[str]:
    """Controle la conversion des montants en toutes lettres.

    Elle n'apparait que sur une facture sur quatre, donc un montant mal converti peut
    rester invisible longtemps si personne ne le controle explicitement.
    """
    anomalies = []
    for valeur, attendu in MONTANTS_ATTENDUS.items():
        obtenu = montant_en_lettres(Decimal(valeur))
        if obtenu != attendu:
            anomalies.append(f"{valeur} : « {obtenu} » au lieu de « {attendu} »")

    # Aucun montant plausible ne doit faire echouer la conversion.
    for valeur in range(0, 100000, 37):
        try:
            montant_en_lettres(Decimal(valeur))
        except Exception as erreur:
            anomalies.append(f"{valeur} : {type(erreur).__name__} {erreur}")

    return anomalies


def main() -> None:
    total = 0
    for seed in range(1, NB_ECHANTILLONS + 1):
        anomalies = verifier(seed)
        total += len(anomalies)
        for anomalie in anomalies:
            print(f"  seed {seed} : {anomalie}")

    anomalies_lettres = verifier_montants_en_lettres()
    total += len(anomalies_lettres)
    for anomalie in anomalies_lettres:
        print(f"  montant en lettres, {anomalie}")

    print(f"\n{NB_ECHANTILLONS} factures verifiees, {total} anomalie(s).")

    exemple = generer_facture(1)
    print("\nExemple, seed 1 :")
    print(f"  Emetteur   : {exemple.organisme.raison_sociale} "
          f"({exemple.organisme.forme_juridique})")
    print(f"  SIRET      : {exemple.organisme.siret}")
    print(f"  TVA        : {exemple.organisme.tva}")
    print(f"  NDA        : {exemple.organisme.numero_declaration_activite}")
    print(f"  Exonere    : {exemple.organisme.exonere_tva}")
    print(f"  Client     : {exemple.client.designation} "
          f"({'pro' if exemple.client.est_professionnel else 'particulier'})")
    print(f"  Facture    : {exemple.numero} du {exemple.date_emission}")
    print(f"  Action     : {exemple.intitule_action}")
    print(f"  Total HT   : {exemple.total_ht} EUR")
    print(f"  Total TTC  : {exemple.total_ttc} EUR")
    print(f"  IBAN       : {exemple.coordonnees_bancaires.iban_formate}")
    print(f"  BIC        : {exemple.coordonnees_bancaires.bic}")
    print(f"  Nouvelles mentions : {exemple.nouvelles_mentions}")


if __name__ == "__main__":
    main()
