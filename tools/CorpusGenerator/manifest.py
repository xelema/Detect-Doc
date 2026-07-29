"""Lecture, ecriture et validation du manifest du corpus.

Chaque document du corpus y porte sa source, sa licence, sa tracabilite et son statut de
sensibilite. Le manifest est versionne dans Git ; les documents eux-memes ne le sont pas,
puisque la seed et le template suffisent a les refabriquer a l'identique. Le hash
SHA-256 est ce qui rend cette promesse verifiable plutot que declarative.

Format : JSON Lines, une ligne par document, trie par identifiant. Ce format se diffe
proprement en revue de pull request, contrairement a un JSON unique reindente a chaque
regeneration.
"""

import hashlib
import json
import random
from collections import Counter
from pathlib import Path

RACINE_POC = Path(__file__).resolve().parents[2]
DOSSIER_CORPUS = RACINE_POC / "corpus"
FICHIER_MANIFEST = DOSSIER_CORPUS / "manifest.jsonl"
DOSSIER_DOCUMENTS = DOSSIER_CORPUS / "raw"

TYPES_CONNUS = {"facture", "rib"}
SENSIBILITES = {"public", "interne_assaini", "production_valide"}

# Toutes les cles ecrites par `construire_entree`. La liste doit rester alignee avec
# elle : un champ oublie ici est un champ que le validateur laisserait disparaitre du
# manifest sans rien dire, `pii_injectees` en tete.
CHAMPS_OBLIGATOIRES = [
    "id", "fichier", "sha256", "type_attendu", "source", "licence", "sensibilite",
    "langue", "pages", "regime", "seed", "variante", "degradations", "derive_de",
    "tags", "pii_injectees", "split", "valide_par", "date_validation",
]

REGIMES = {"texte-natif", "scanne"}


def hash_fichier(chemin: Path) -> str:
    return hashlib.sha256(chemin.read_bytes()).hexdigest()


def _split(seed: int) -> str:
    """Repartition dev / test, scellee des le premier document.

    Un quart du corpus va dans `dev`, qui sert au reglage du prompt et de l'OCR ; le jeu
    `test` ne s'ouvre qu'aux jalons de mesure.

    Le tirage passe par un flux nomme plutot que par un `seed % 4` : d'autres facettes du
    document, comme le choix du template, tournent elles aussi sur la seed, et un modulo
    partage les ferait toutes s'aligner. Le jeu de reglage se retrouverait alors reduit a
    une seule mise en page, ce qui viderait de son sens tout reglage fait dessus.

    Le flux depend du seul numero du document : ajouter des documents ne redistribue
    jamais ceux qui existent deja.
    """
    return "dev" if random.Random(f"split-{seed}").random() < 0.25 else "test"


def _chemin_relatif(chemin: Path) -> str:
    return str(chemin.relative_to(DOSSIER_CORPUS)).replace("\\", "/")


def construire_entree(identifiant: str, chemin: Path, type_attendu: str, seed: int,
                      pages: int, variante: dict, pii: dict, tags: list[str]) -> dict:
    """Assemble la ligne de manifest d'un document qui vient d'etre produit."""
    return {
        "id": identifiant,
        "fichier": _chemin_relatif(chemin),
        "sha256": hash_fichier(chemin),
        "type_attendu": type_attendu,
        # Documents fabriques de toutes pieces : aucune donnee personnelle reelle,
        # aucune licence a respecter, sensibilite publique.
        "source": "genere",
        "licence": "interne",
        "sensibilite": "public",
        "langue": "fr",
        "pages": pages,
        "regime": "texte-natif",
        "seed": seed,
        "variante": variante,
        "degradations": [],
        "derive_de": None,
        "tags": tags,
        # Verite terrain des donnees personnelles presentes dans le document. Le
        # generateur connait les valeurs qu'il a injectees, ce qui permettra plus tard
        # d'evaluer un masquage automatique sans annoter le corpus a la main.
        "pii_injectees": pii,
        "split": _split(seed),
        # Renseignes le jour ou le perimetre du corpus est valide par un responsable.
        # Tant qu'ils sont nuls, aucune validation n'a eu lieu : ne jamais les
        # pre-remplir, ils font foi.
        "valide_par": None,
        "date_validation": None,
    }


def entree_derivee(original: dict, identifiant: str, chemin: Path, regime: str,
                   degradations: list[str], tags: list[str]) -> dict:
    """Ligne de manifest d'un document produit a partir d'un autre.

    L'entree est une copie de celle de l'original, dont seul ce qui a reellement change
    est remplace. Copier plutot que reconstruire garantit que les deux documents portent
    le meme type attendu, la meme verite terrain et surtout **le meme jeu** : separer un
    document de son derive entre le jeu de reglage et le jeu de mesure ferait fuiter le
    contenu de l'un dans l'autre.

    C'est aussi ce qui evite d'avoir a propager ici chaque champ ajoute au manifest.
    """
    return {
        **original,
        "id": identifiant,
        "fichier": _chemin_relatif(chemin),
        "sha256": hash_fichier(chemin),
        "regime": regime,
        "degradations": degradations,
        "derive_de": original["id"],
        "tags": tags,
    }


def lire() -> dict[str, dict]:
    if not FICHIER_MANIFEST.exists():
        return {}
    lignes = FICHIER_MANIFEST.read_text(encoding="utf-8").splitlines()
    entrees = [json.loads(ligne) for ligne in lignes if ligne.strip()]
    return {entree["id"]: entree for entree in entrees}


def enregistrer(nouvelles: list[dict]) -> None:
    """Fusionne les entrees fournies avec le manifest existant, puis reecrit tout.

    Regenerer un seul type ne doit pas effacer les autres, et regenerer deux fois le
    meme document doit produire exactement le meme fichier.
    """
    entrees = lire()
    for entree in nouvelles:
        entrees[entree["id"]] = entree

    FICHIER_MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    contenu = "\n".join(
        json.dumps(entrees[identifiant], ensure_ascii=False, sort_keys=True)
        for identifiant in sorted(entrees)
    )
    FICHIER_MANIFEST.write_text(contenu + "\n", encoding="utf-8")


def valider() -> list[str]:
    """Controle le manifest. Retourne la liste des anomalies, vide si tout est bon."""
    anomalies = []

    if not FICHIER_MANIFEST.exists():
        return [f"Manifest absent : {FICHIER_MANIFEST}"]

    entrees = lire()
    if not entrees:
        return ["Manifest vide"]

    for identifiant, entree in sorted(entrees.items()):
        for champ in CHAMPS_OBLIGATOIRES:
            if champ not in entree:
                anomalies.append(f"{identifiant} : champ manquant '{champ}'")

        if entree.get("type_attendu") not in TYPES_CONNUS:
            anomalies.append(f"{identifiant} : type inconnu '{entree.get('type_attendu')}'")
        if entree.get("sensibilite") not in SENSIBILITES:
            anomalies.append(
                f"{identifiant} : sensibilite inconnue '{entree.get('sensibilite')}'")
        if entree.get("split") not in {"dev", "test"}:
            anomalies.append(f"{identifiant} : split invalide '{entree.get('split')}'")
        if entree.get("regime") not in REGIMES:
            anomalies.append(f"{identifiant} : regime inconnu '{entree.get('regime')}'")

        origine = entree.get("derive_de")
        if origine is not None:
            if origine not in entrees:
                anomalies.append(f"{identifiant} : derive d'un document absent '{origine}'")
            # Un document et son jumeau scanne portent le meme contenu. Les separer
            # entre le jeu de reglage et le jeu de mesure ferait fuiter l'un dans
            # l'autre et rendrait toute mesure trop optimiste.
            elif entrees[origine]["split"] != entree["split"]:
                anomalies.append(f"{identifiant} : split different de son original")

        chemin = DOSSIER_CORPUS / entree.get("fichier", "")
        if not chemin.exists():
            anomalies.append(f"{identifiant} : fichier absent ({entree.get('fichier')})")
        elif hash_fichier(chemin) != entree.get("sha256"):
            anomalies.append(f"{identifiant} : hash differente du manifest")

    return anomalies


def resume() -> str:
    entrees = lire()
    documents = entrees.values()
    compteurs = {
        "type": Counter(e["type_attendu"] for e in documents),
        "jeu": Counter(e["split"] for e in documents),
        "regime": Counter(e["regime"] for e in documents),
        "template": Counter(e["variante"].get("template", "?") for e in documents),
    }

    lignes = [f"{len(entrees)} document(s) au manifest"]
    for axe, valeurs in compteurs.items():
        detail = ", ".join(f"{cle} {n}" for cle, n in sorted(valeurs.items()))
        lignes.append(f"  par {axe:<9}: {detail}")
    return "\n".join(lignes)


def main() -> None:
    anomalies = valider()
    for anomalie in anomalies:
        print(f"  {anomalie}")
    print(resume())
    print(f"\n{len(anomalies)} anomalie(s).")


if __name__ == "__main__":
    main()
