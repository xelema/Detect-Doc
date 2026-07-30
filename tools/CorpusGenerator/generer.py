"""Point d'entrée du générateur de corpus.

Usage :
    .venv/bin/python generer.py

Les types et quantités viennent du fichier TOML global. Ce module ne fait qu'orchestrer :
la génération de données, le style et la production du manifest de chaque type vivent
dans son propre fichier sous `documents/`. Ajouter un type demande d'y créer une classe
sur le même modèle, de l'enregistrer ci-dessous dans TYPES, et d'ajouter son nom à
`manifest.TYPES_CONNUS`.
"""

import manifest
from documents.carte_visite import DOCUMENT as carte_visite
from documents.devis import DOCUMENT as devis
from documents.facture import DOCUMENT as facture
from documents.feuille_emargement import DOCUMENT as feuille_emargement
from documents.offre import DOCUMENT as offre_emploi
from documents.rib import DOCUMENT as rib
from documents.rib_sepa import DOCUMENT as rib_sepa
from documents.sepa import DOCUMENT as sepa
from configuration import DOCUMENTS, FICHIER, SHA256
from degradation import degrader, profil_acquisition, profil_degradation

TYPES = {
    document.type_attendu: document
    for document in [facture, devis, rib, sepa, rib_sepa, carte_visite,
                     feuille_emargement, offre_emploi]
}

# Les types produits et les types que le manifest accepte sont déclarés dans deux
# modules, manifest ne pouvant pas importer documents/ sans tirer les bibliothèques de
# rendu (WeasyPrint, Augraphy) dans son propre outil de validation, plus léger. Le
# contrôle échoue bruyamment plutôt que de laisser le validateur signaler chaque nouveau
# document comme un type inconnu.
assert set(TYPES) == manifest.TYPES_CONNUS, (
    f"types produits {sorted(TYPES)} et types connus du manifest "
    f"{sorted(manifest.TYPES_CONNUS)} ne concordent pas")


def _jumeau_scanne(propre: dict, seed: int) -> dict | None:
    """Version scannée d'un document, si la seed lui en attribue une."""
    profil = profil_degradation(seed)
    if profil is None:
        return None

    identifiant = f"{propre['id']}-d"
    chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"
    effets = degrader(manifest.DOSSIER_CORPUS / propre["fichier"], chemin, seed, profil)

    return manifest.entree_derivee(
        propre, identifiant, chemin,
        regime="scanne", degradations=effets, tags=propre["tags"] + [profil])


def _reprise_physique(propre: dict, seed: int) -> dict:
    """Numérise un document repris d'un objet physique, sans conserver l'original.

    Le rendu propre n'est ici qu'une étape de fabrication, l'équivalent du fichier de
    l'imprimeur : il est écrasé par sa reprise et n'entre jamais au manifest, qui ne doit
    décrire que ce qui existe. L'entrée reste donc autonome, sans `derive_de`, à la
    différence du jumeau scanné qui accompagne un original bien réel.

    Le mode `fichier` est rendu tel quel : c'est justement le document que personne n'a
    photographié, et il doit rester à texte natif.

    Écrire la numérisation par-dessus sa source est sans danger : la rasterisation ferme
    le fichier d'entrée avant que le nouveau PDF ne soit écrit.
    """
    acquisition = propre["variante"]["acquisition"]
    if acquisition == "fichier":
        return propre

    fichier = manifest.DOSSIER_CORPUS / propre["fichier"]
    profil = profil_acquisition(seed, acquisition)
    rotation_page_deg = propre["variante"].get("rotation_page_deg", 0)
    effets = degrader(
        fichier, fichier, seed, profil,
        rotation_page_deg=rotation_page_deg,
    )

    return {
        **propre,
        "sha256": manifest.hash_fichier(fichier),
        "regime": "scanne",
        "degradations": effets,
        "tags": propre["tags"] + [profil],
    }


def _produire(type_document: str, nombre: int) -> list[dict]:
    """Produit exactement `nombre` entrées de manifest pour ce type.

    La configuration compte les documents écrits, pas les unités générées : la facture,
    le devis et les trois types bancaires donnent une ou deux entrées par unité, une unité
    sur N se dédoublant en un jumeau scanné. La boucle produit donc autant d'unités qu'il
    en faut pour tomber sur le compte demandé, plutôt qu'un nombre d'unités fixé d'avance
    qui laisserait l'effectif réel dépendre de la règle de dédoublement.

    Le jumeau n'est fabriqué que s'il reste de la place : le produire pour le jeter
    laisserait sur le disque un fichier absent du manifest, que le validateur ne verrait
    pas et que la prochaine exécution ne nettoierait pas.

    Les identifiants restant attachés au numéro d'unité, augmenter un effectif prolonge
    le corpus sans jamais redistribuer ce qui existe déjà.
    """
    document = TYPES[type_document]
    entrees: list[dict] = []
    numero = 0
    while len(entrees) < nombre:
        numero += 1
        propre = document.produire(numero)

        if document.autonome:
            # produire() a déjà tranché lui-même le régime final : ni reprise forcée,
            # ni jumeau dégradé. Voir documents/base.py.
            entrees.append(propre)
            continue

        if document.physique:
            entrees.append(_reprise_physique(propre, numero))
            continue

        entrees.append(propre)
        if len(entrees) == nombre:
            break
        jumeau = _jumeau_scanne(propre, numero)
        if jumeau is not None:
            entrees.append(jumeau)
    return entrees


def main() -> None:
    types_inconnus = set(DOCUMENTS) - set(TYPES)
    if types_inconnus:
        raise ValueError(
            f"type(s) inconnu(s) dans [documents] : {', '.join(sorted(types_inconnus))}")

    entrees = []
    for type_document, nombre in sorted(DOCUMENTS.items()):
        entrees.extend(_produire(type_document, nombre))

    manifest.enregistrer(entrees)

    for entree in entrees:
        print(f"  {entree['id']:<12} {entree['split']:<4} {entree['regime']:<12} "
              f"{', '.join(entree['tags'])}")
    print(f"\nConfiguration : {FICHIER}")
    print(f"Empreinte     : {SHA256}")
    print(f"{len(entrees)} document(s) écrits, manifest mis à jour.")


if __name__ == "__main__":
    main()
