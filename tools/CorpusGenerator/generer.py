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
import degradation
from configuration import (DOCUMENTS, ECRIRE_COMPOSITION, FICHIER, SEED, SHA256,
                           part_numerisee, reglages_explicites)

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


def _jumeau_scanne(type_document: str, propre: dict, seed: int) -> dict:
    """Version scannée d'un document, dont l'original reste au corpus."""
    profil = degradation.profil(type_document, seed)
    identifiant = f"{propre['id']}-d"
    chemin = manifest.DOSSIER_DOCUMENTS / f"{identifiant}.pdf"
    effets = degradation.degrader(
        manifest.DOSSIER_CORPUS / propre["fichier"], chemin, seed, profil)

    return manifest.entree_derivee(
        propre, identifiant, chemin,
        regime="scanne", degradations=effets, tags=propre["tags"] + [profil])


def _reprise_physique(type_document: str, propre: dict, seed: int) -> dict:
    """Numérise un document repris d'un objet physique, sans conserver l'original.

    Le rendu propre n'est ici qu'une étape de fabrication, l'équivalent du fichier de
    l'imprimeur : il est écrasé par sa reprise et n'entre jamais au manifest, qui ne doit
    décrire que ce qui existe. L'entrée reste donc autonome, sans `derive_de`, à la
    différence du jumeau scanné qui accompagne un original bien réel.

    Le mode `fichier` est rendu tel quel : c'est justement le document que personne n'a
    photographié, et il doit rester à texte natif.

    Le mode et le profil sont redemandés à `degradation` plutôt que transportés depuis le
    style : la réponse ne dépend que du type et du numéro, et la faire voyager dans le
    manifest obligerait chaque style de type physique à porter un champ de plus.

    Écrire la numérisation par-dessus sa source est sans danger : la rasterisation ferme
    le fichier d'entrée avant que le nouveau PDF ne soit écrit.
    """
    _, profil = degradation.acquisition(type_document, seed)
    if profil is None:
        return propre

    fichier = manifest.DOSSIER_CORPUS / propre["fichier"]
    rotation_page_deg = propre["variante"].get("rotation_page_deg", 0)
    effets = degradation.degrader(
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
    le devis et les trois types bancaires donnent une ou deux entrées par unité, selon la
    part de documents numérisés demandée pour le type. La boucle produit donc autant
    d'unités qu'il en faut pour tomber sur le compte demandé, plutôt qu'un nombre d'unités
    fixé d'avance qui laisserait l'effectif réel dépendre de cette part.

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
            entrees.append(_reprise_physique(type_document, propre, numero))
            continue

        entrees.append(propre)
        if len(entrees) == nombre:
            break
        if degradation.jumeau(type_document, numero):
            entrees.append(_jumeau_scanne(type_document, propre, numero))
    return entrees


def _verifier_configuration() -> None:
    """Refuse un réglage que le type qui le porte ne saurait pas appliquer.

    `configuration.py` contrôle la forme du fichier, mais il ignore de quelle nature est
    chaque type : seul ce module connaît `physique` et `autonome`. Un réglage sans effet
    passerait sinon inaperçu, et le corpus produit ne serait pas celui qu'on croit lire.
    """
    types_inconnus = set(DOCUMENTS) - set(TYPES)
    if types_inconnus:
        raise ValueError(
            f"type(s) inconnu(s) dans [types] : {', '.join(sorted(types_inconnus))}")

    # Un type enregistré ici mais absent du TOML ne serait tout simplement pas produit, et
    # rien ne le dirait. Le mettre à `count = 0` est la façon explicite de l'écarter.
    types_sans_bloc = set(TYPES) - set(DOCUMENTS)
    if types_sans_bloc:
        raise ValueError(
            f"type(s) sans bloc [types.<nom>] : {', '.join(sorted(types_sans_bloc))}")

    for type_document, document in TYPES.items():
        explicites = reglages_explicites(type_document)
        part = part_numerisee(type_document)

        if not (document.physique or document.autonome) and part > 50:
            raise ValueError(
                f"[types.{type_document}] scanned_share = {part} : ce type conserve son "
                "original et ne produit qu'un jumeau par unité, 50 est donc le maximum "
                "atteignable")
        if "rotated_share" in explicites and not document.physique:
            raise ValueError(
                f"[types.{type_document}] rotated_share n'a de sens que pour un type "
                "repris d'un objet physique, seul à pouvoir être posé de travers")
        if "profiles" in explicites and document.autonome:
            raise ValueError(
                f"[types.{type_document}] profiles n'a de sens que pour un type numérisé "
                "à partir d'un papier ; ce type tranche lui-même son régime")


def main() -> None:
    _verifier_configuration()

    entrees = []
    for type_document, nombre in sorted(DOCUMENTS.items()):
        entrees.extend(_produire(type_document, nombre))

    manifest.enregistrer(entrees)
    if ECRIRE_COMPOSITION:
        manifest.ecrire_composition()

    for entree in entrees:
        print(f"  {entree['id']:<12} {entree['split']:<4} {entree['regime']:<12} "
              f"{', '.join(entree['tags'])}")
    print(f"\nConfiguration : {FICHIER}")
    print(f"Empreinte     : {SHA256}")
    print(f"Seed globale  : {SEED}")
    print(f"Documentation : {manifest.FICHIER_DOC_CORPUS if ECRIRE_COMPOSITION else 'non réécrite'}")
    print(f"{len(entrees)} document(s) écrits, manifest mis à jour.")


if __name__ == "__main__":
    main()
