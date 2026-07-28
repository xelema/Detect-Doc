"""Verifie que les identifiants generes portent des cles de controle justes.

A lancer apres toute modification de la generation de donnees. Un corpus dont les IBAN
ou les SIREN sont invalides est inexploitable pour mesurer le masquage des donnees
personnelles, et se repere immediatement comme factice.
"""

from stdnum import iban as iban_stdnum
from stdnum.fr import siren as siren_stdnum
from stdnum.fr import siret as siret_stdnum
from stdnum.fr import tva as tva_stdnum

from cles import cle_rib
from data import generer_facture

NB_ECHANTILLONS = 50


def verifier(graine: int) -> list[str]:
    """Retourne la liste des anomalies trouvees sur une facture, vide si tout est bon."""
    anomalies = []
    facture = generer_facture(graine, professionnel=(graine % 4 != 0))
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


def main() -> None:
    total = 0
    for graine in range(1, NB_ECHANTILLONS + 1):
        anomalies = verifier(graine)
        total += len(anomalies)
        for anomalie in anomalies:
            print(f"  graine {graine} : {anomalie}")

    print(f"\n{NB_ECHANTILLONS} factures verifiees, {total} anomalie(s).")

    exemple = generer_facture(1)
    print("\nExemple, graine 1 :")
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
