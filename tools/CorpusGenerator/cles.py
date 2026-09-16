"""Calcul des clés de contrôle des identifiants français.

Les documents du corpus doivent porter des identifiants dont les clés sont justes.
Un IBAN ou un SIREN mal formés rendraient le corpus repérable comme factice, et
surtout ils fausseraient plus tard la mesure du masquage des données personnelles :
un détecteur d'IBAN qui valide la clé ne se comporte pas pareil sur un IBAN invalide.
"""

from stdnum import iban as iban_stdnum


def cle_rib(code_banque: str, code_guichet: str, numero_compte: str) -> str:
    """Clé RIB sur 2 chiffres, calculée modulo 97.

    Le numéro de compte est supposé entièrement numérique. Les comptes contenant des
    lettres imposent une table de conversion supplémentaire, inutile ici puisque le
    générateur ne produit que des comptes numériques.
    """
    reste = (89 * int(code_banque) + 15 * int(code_guichet) + 3 * int(numero_compte)) % 97
    return f"{97 - reste:02d}"


def iban_francais(code_banque: str, code_guichet: str, numero_compte: str, cle: str) -> str:
    """Assemble un IBAN français de 27 caractères et calcule ses 2 chiffres de contrôle.

    L'IBAN contient le BBAN, c'est-à-dire exactement les codes imprimés dans le pavé RIB.
    Les calculer ensemble est ce qui garantit qu'un document ne se contredit pas lui-même.

    Le calcul des chiffres de contrôle est confié à stdnum, la même bibliothèque qui
    sert ensuite à valider le résultat : produire avec du code maison ce qu'on contrôle
    avec une bibliothèque ne vérifie rien d'autre que la cohérence du code maison.
    """
    bban = f"{code_banque}{code_guichet}{numero_compte}{cle}"
    return f"FR{iban_stdnum.calc_check_digits(f'FR00{bban}')}{bban}"


def cle_tva_intracommunautaire(siren: str) -> str:
    """Clé sur 2 chiffres du numéro de TVA intracommunautaire français."""
    return f"{(12 + 3 * (int(siren) % 97)) % 97:02d}"


def numero_tva_intracommunautaire(siren: str) -> str:
    return f"FR{cle_tva_intracommunautaire(siren)}{siren}"


def identifiant_creancier_sepa(code_activite: str, identifiant_national: str) -> str:
    """Assemble un Identifiant Créancier SEPA (ICS) français et calcule ses 2 chiffres de
    contrôle.

    La spécification EPC fait porter les chiffres de contrôle sur le code activité suivi
    de l'identifiant national, avec le même algorithme ISO 7064 MOD 97-10 que l'IBAN :
    calculer les deux à part aurait rendu deux implémentations à tenir cohérentes pour un
    seul et même calcul.
    """
    corps = f"{code_activite}{identifiant_national}"
    return f"FR{iban_stdnum.calc_check_digits(f'FR00{corps}')}{corps}"
