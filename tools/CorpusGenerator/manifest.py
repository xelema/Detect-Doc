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
from pathlib import Path

RACINE_POC = Path(__file__).resolve().parents[2]
DOSSIER_CORPUS = RACINE_POC / "corpus"
FICHIER_MANIFEST = DOSSIER_CORPUS / "manifest.jsonl"
DOSSIER_DOCUMENTS = DOSSIER_CORPUS / "raw"

TYPES_CONNUS = {"facture", "rib"}
SENSIBILITES = {"public", "interne_assaini", "production_valide"}

CHAMPS_OBLIGATOIRES = [
    "id", "fichier", "sha256", "type_attendu", "source", "licence", "sensibilite",
    "langue", "pages", "regime", "seed", "variante", "degradations", "split",
]


def hash_fichier(chemin: Path) -> str:
    return hashlib.sha256(chemin.read_bytes()).hexdigest()


def _split(seed: int) -> str:
    """Repartition dev / test, scellee des le premier document.

    Une seed sur quatre va dans `dev`, soit un quart du corpus. La regle
    est arithmetique et non aleatoire : un document ne peut pas changer de camp entre
    deux executions, ce qui serait le meilleur moyen de fausser toutes les mesures.
    Le jeu `dev` sert au reglage du prompt et de l'OCR, le jeu `test` ne s'ouvre qu'aux
    jalons de mesure.
    """
    return "dev" if seed % 4 == 1 else "test"


def construire_entree(identifiant: str, chemin: Path, type_attendu: str, seed: int,
                      pages: int, variante: dict, pii: dict,
                      tags: list[str]) -> dict:
    """Assemble la ligne de manifest d'un document qui vient d'etre produit."""
    return {
        "id": identifiant,
        "fichier": str(chemin.relative_to(DOSSIER_CORPUS)).replace("\\", "/"),
        "sha256": hash_fichier(chemin),
        "type_attendu": type_attendu,
        # Documents fabriques de toutes pieces : aucune donnee personnelle reelle,
        # aucune licence a respecter, sensibilite publique.
        "source": "genere",
        "licence": "interne",
        "sensibilite": "public",
        "langue": "fr",
        "pages": pages,
        # `texte-natif` tant que le document n'est pas rasterise puis degrade.
        "regime": "texte-natif",
        "seed": seed,
        "variante": variante,
        "degradations": [],
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

        chemin = DOSSIER_CORPUS / entree.get("fichier", "")
        if not chemin.exists():
            anomalies.append(f"{identifiant} : fichier absent ({entree.get('fichier')})")
        elif hash_fichier(chemin) != entree.get("sha256"):
            anomalies.append(f"{identifiant} : hash differente du manifest")

    return anomalies


def resume() -> str:
    entrees = lire()
    par_type: dict[str, int] = {}
    par_split: dict[str, int] = {}
    par_variante: dict[str, int] = {}

    for entree in entrees.values():
        par_type[entree["type_attendu"]] = par_type.get(entree["type_attendu"], 0) + 1
        par_split[entree["split"]] = par_split.get(entree["split"], 0) + 1
        template = entree.get("variante", {}).get("template", "?")
        par_variante[template] = par_variante.get(template, 0) + 1

    lignes = [f"{len(entrees)} document(s) au manifest"]
    lignes.append("  par type      : " + ", ".join(
        f"{cle} {valeur}" for cle, valeur in sorted(par_type.items())))
    lignes.append("  par jeu       : " + ", ".join(
        f"{cle} {valeur}" for cle, valeur in sorted(par_split.items())))
    lignes.append("  par template  : " + ", ".join(
        f"{cle} {valeur}" for cle, valeur in sorted(par_variante.items())))
    return "\n".join(lignes)


def main() -> None:
    anomalies = valider()
    for anomalie in anomalies:
        print(f"  {anomalie}")
    print(resume())
    print(f"\n{len(anomalies)} anomalie(s).")


if __name__ == "__main__":
    main()
