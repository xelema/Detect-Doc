"""Calcul des cles de controle des identifiants francais.

Les documents du corpus doivent porter des identifiants dont les cles sont justes.
Un IBAN ou un SIREN mal formes rendraient le corpus reperable comme factice, et
surtout ils fausseraient plus tard la mesure du masquage des donnees personnelles :
un detecteur d'IBAN qui valide la cle ne se comporte pas pareil sur un IBAN invalide.
"""

from stdnum import iban as iban_stdnum


def cle_rib(code_banque: str, code_guichet: str, numero_compte: str) -> str:
    """Cle RIB sur 2 chiffres, calculee modulo 97.

    Le numero de compte est suppose entierement numerique. Les comptes contenant des
    lettres imposent une table de conversion supplementaire, inutile ici puisque le
    generateur ne produit que des comptes numeriques.
    """
    reste = (89 * int(code_banque) + 15 * int(code_guichet) + 3 * int(numero_compte)) % 97
    return f"{97 - reste:02d}"


def iban_francais(code_banque: str, code_guichet: str, numero_compte: str, cle: str) -> str:
    """Assemble un IBAN francais de 27 caracteres et calcule ses 2 chiffres de controle.

    L'IBAN contient le BBAN, c'est-a-dire exactement les codes imprimes dans le pave RIB.
    Les calculer ensemble est ce qui garantit qu'un document ne se contredit pas lui-meme.

    Le calcul des chiffres de controle est confie a `stdnum`, la meme bibliotheque qui
    sert ensuite a valider le resultat : produire avec du code maison ce qu'on controle
    avec une bibliotheque ne verifie rien d'autre que la coherence du code maison.
    """
    bban = f"{code_banque}{code_guichet}{numero_compte}{cle}"
    return f"FR{iban_stdnum.calc_check_digits(f'FR00{bban}')}{bban}"


def cle_tva_intracommunautaire(siren: str) -> str:
    """Cle sur 2 chiffres du numero de TVA intracommunautaire francais."""
    return f"{(12 + 3 * (int(siren) % 97)) % 97:02d}"


def numero_tva_intracommunautaire(siren: str) -> str:
    return f"FR{cle_tva_intracommunautaire(siren)}{siren}"
