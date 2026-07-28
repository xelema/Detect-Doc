"""Calcul des cles de controle des identifiants francais.

Les documents du corpus doivent porter des identifiants dont les cles sont justes.
Un IBAN ou un SIREN mal formes rendraient le corpus reperable comme factice, et
surtout ils fausseraient plus tard la mesure du masquage des donnees personnelles :
un detecteur d'IBAN qui valide la cle ne se comporte pas pareil sur un IBAN invalide.
"""


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
    """
    bban = f"{code_banque}{code_guichet}{numero_compte}{cle}"

    # Norme ISO 13616 : on deplace le code pays et "00" a la fin, puis on convertit
    # chaque lettre en nombre (A vaut 10, B vaut 11, et ainsi de suite).
    provisoire = f"{bban}FR00"
    numerique = "".join(str(int(c, 36)) for c in provisoire)
    contexte = 98 - (int(numerique) % 97)

    return f"FR{contexte:02d}{bban}"


def cle_tva_intracommunautaire(siren: str) -> str:
    """Cle sur 2 chiffres du numero de TVA intracommunautaire francais."""
    return f"{(12 + 3 * (int(siren) % 97)) % 97:02d}"


def numero_tva_intracommunautaire(siren: str) -> str:
    return f"FR{cle_tva_intracommunautaire(siren)}{siren}"
