"""Pont NER pour le comparatif : un document par ligne sur stdin, ses plages sur stdout.

Les modèles mettent plusieurs secondes à charger, d'où un processus unique tenu ouvert pour tout
le run plutôt qu'un appel par document. La poignée de main envoyée au démarrage sort ce
chargement de la fenêtre chronométrée par le benchmark.
"""

import contextlib
import json
import os
import sys
import time

LANGUE = "fr"

CATEGORIES_PRESIDIO = {
    "PERSON": "Nom",
    "ORGANIZATION": "Organisme",
    "LOCATION": "Adresse",
    "EMAIL_ADDRESS": "Email",
    "PHONE_NUMBER": "Telephone",
    "IBAN_CODE": "Iban",
}

CATEGORIES_GLINER = {
    "person": "Nom",
    "organization": "Organisme",
    "address": "Adresse",
    "email": "Email",
    "phone number": "Telephone",
    "iban": "Iban",
}


def charger_presidio():
    import spacy
    from presidio_analyzer import AnalyzerEngine, RecognizerRegistry
    from presidio_analyzer.nlp_engine import NlpEngineProvider, SpacyNlpEngine
    from presidio_analyzer.predefined_recognizers import PhoneRecognizer

    modele = os.environ.get("PII_PRESIDIO_MODELE", "fr_core_news_sm")
    demandees = os.environ.get("PII_PRESIDIO_ENTITES")
    entites = ([e.strip() for e in demandees.split(",") if e.strip() in CATEGORIES_PRESIDIO]
               if demandees else list(CATEGORIES_PRESIDIO))

    if modele == "aucun":
        # Pipeline spaCy vide : il tokenise et rien d'autre. Les neuf reconnaisseurs de motifs
        # fonctionnent, le dixième, celui qui produit les noms, n'a plus de modèle à interroger.
        # Mesure le plancher de Presidio, sans le gigaoctet du modèle.
        class MoteurSansModele(SpacyNlpEngine):
            def __init__(self):
                super().__init__(models=[{"lang_code": LANGUE, "model_name": modele}])
                self.nlp = {LANGUE: spacy.blank(LANGUE)}

        moteur_nlp = MoteurSansModele()
    else:
        moteur_nlp = NlpEngineProvider(nlp_configuration={
            "nlp_engine_name": "spacy",
            "models": [{"lang_code": LANGUE, "model_name": modele}],
        }).create_engine()

    registre = RecognizerRegistry(supported_languages=[LANGUE])
    registre.load_predefined_recognizers(languages=[LANGUE])
    # Les régions par défaut du reconnaisseur de téléphones n'incluent pas la France.
    registre.add_recognizer(PhoneRecognizer(supported_language=LANGUE, supported_regions=["FR"]))

    analyzer = AnalyzerEngine(
        nlp_engine=moteur_nlp,
        registry=registre,
        supported_languages=[LANGUE],
    )

    def detecter(texte):
        for resultat in analyzer.analyze(text=texte, language=LANGUE, entities=entites):
            yield CATEGORIES_PRESIDIO[resultat.entity_type], resultat.entity_type, resultat.start, resultat.end

    return detecter, {"modele": modele, "entites": ", ".join(entites)}


def charger_gliner():
    from gliner import GLiNER

    modele = os.environ.get("PII_GLINER_MODELE", "urchade/gliner_multi_pii-v1")
    demandes = os.environ.get("PII_GLINER_LABELS")
    labels = ([l.strip() for l in demandes.split(",") if l.strip() in CATEGORIES_GLINER]
              if demandes else list(CATEGORIES_GLINER))
    seuil = float(os.environ.get("PII_GLINER_SEUIL", "0.5"))
    budget = int(os.environ.get("PII_GLINER_CARACTERES", "1200"))

    model = GLiNER.from_pretrained(modele)

    def detecter(texte):
        for debut_bloc, bloc in decouper(texte, budget):
            for entite in model.predict_entities(bloc, labels, threshold=seuil):
                categorie = CATEGORIES_GLINER.get(entite["label"])
                if categorie:
                    yield categorie, entite["label"], debut_bloc + entite["start"], debut_bloc + entite["end"]

    return detecter, {
        "modele": modele,
        "entites": ", ".join(labels),
        "seuil": seuil,
        "caracteres_par_bloc": budget,
    }


def charger_gliner2():
    from gliner2 import AutoExtractor

    modele = os.environ.get("PII_GLINER2_MODELE", "fastino/gliner2.5-multi-v1")
    demandes = os.environ.get("PII_GLINER2_LABELS")
    labels = ([l.strip() for l in demandes.split(",") if l.strip() in CATEGORIES_GLINER]
              if demandes else list(CATEGORIES_GLINER))
    seuil = float(os.environ.get("PII_GLINER2_SEUIL", "0.5"))
    budget = int(os.environ.get("PII_GLINER2_CARACTERES", "1200"))

    model = AutoExtractor.from_pretrained(modele)

    def detecter(texte):
        for debut_bloc, bloc in decouper(texte, budget):
            resultat = model.extract_entities(bloc, labels, threshold=seuil, include_spans=True)
            for label, entites in resultat["entities"].items():
                categorie = CATEGORIES_GLINER.get(label)
                if not categorie:
                    continue
                for entite in entites:
                    yield categorie, label, debut_bloc + entite["start"], debut_bloc + entite["end"]

    return detecter, {
        "modele": modele,
        "entites": ", ".join(labels),
        "seuil": seuil,
        "caracteres_par_bloc": budget,
    }


def decouper(texte, budget):
    """Blocs bornés par la limite de tokens du modèle, coupés sur des fins de ligne.

    Une entité à cheval sur deux blocs est perdue ; couper sur un retour à la ligne rend le cas
    rare sur ces documents, où chaque champ occupe sa ligne.
    """
    debut = 0
    courant = 0
    for ligne in texte.splitlines(keepends=True):
        if courant and courant + len(ligne) > budget:
            yield debut, texte[debut:debut + courant]
            debut += courant
            courant = 0
        courant += len(ligne)
    if courant:
        yield debut, texte[debut:debut + courant]


def pic_memoire_octets():
    """Pic de mémoire résidente du processus, demandé au système d'exploitation.

    C'est ce processus qui porte le modèle : les compteurs du benchmark, côté .NET, ne voient
    que son propre espace et publieraient quelques mégaoctets pour un moteur qui en consomme
    des milliers.
    """
    try:
        if sys.platform == "win32":
            import ctypes

            class CompteursMemoire(ctypes.Structure):
                _fields_ = [("cb", ctypes.c_uint32), ("PageFaultCount", ctypes.c_uint32)] + [
                    (nom, ctypes.c_size_t) for nom in (
                        "PeakWorkingSetSize", "WorkingSetSize", "QuotaPeakPagedPoolUsage",
                        "QuotaPagedPoolUsage", "QuotaPeakNonPagedPoolUsage",
                        "QuotaNonPagedPoolUsage", "PagefileUsage", "PeakPagefileUsage")]

            kernel32 = ctypes.WinDLL("kernel32")
            psapi = ctypes.WinDLL("psapi")
            # Sans types déclarés, ctypes tronque la poignée de processus à 32 bits et l'appel
            # échoue en rendant zéro, ce qui se lirait comme une mesure.
            kernel32.GetCurrentProcess.restype = ctypes.c_void_p
            psapi.GetProcessMemoryInfo.argtypes = [
                ctypes.c_void_p, ctypes.POINTER(CompteursMemoire), ctypes.c_uint32]

            compteurs = CompteursMemoire()
            compteurs.cb = ctypes.sizeof(compteurs)
            if psapi.GetProcessMemoryInfo(
                    kernel32.GetCurrentProcess(), ctypes.byref(compteurs), compteurs.cb):
                return int(compteurs.PeakWorkingSetSize)
            return None

        with open("/proc/self/status", encoding="utf-8") as statut:
            for ligne in statut:
                if ligne.startswith("VmHWM:"):
                    return int(ligne.split()[1]) * 1024
    except Exception:
        return None

    return None


def en_indices_utf16(texte, debut, fin):
    """Python indexe en points de code, .NET en unités UTF-16. La conversion évite un décalage
    des plages dès qu'un caractère hors du plan de base apparaît avant une PII."""
    depart = len(texte[:debut].encode("utf-16-le")) // 2
    longueur = len(texte[debut:fin].encode("utf-16-le")) // 2
    return depart, longueur


BACKENDS = {"presidio": charger_presidio, "gliner": charger_gliner, "gliner2": charger_gliner2}


def main():
    backend = sys.argv[1] if len(sys.argv) > 1 else "presidio"
    depart = time.perf_counter()
    try:
        # Le chargement écrit sur stdout, spaCy et transformers y impriment leurs diagnostics :
        # le détourner vers stderr garde le canal de sortie réservé au protocole.
        with contextlib.redirect_stdout(sys.stderr):
            detecter, description = BACKENDS[backend]()
    except (Exception, SystemExit) as erreur:
        print(json.dumps({"erreur": f"{type(erreur).__name__}: {erreur}"}), flush=True)
        raise SystemExit(1)

    print(json.dumps({"chargement_ms": (time.perf_counter() - depart) * 1000, **description}), flush=True)

    for ligne in sys.stdin:
        ligne = ligne.strip()
        if not ligne:
            continue

        try:
            texte = json.loads(ligne)["texte"]
            detections = []
            with contextlib.redirect_stdout(sys.stderr):
                trouvees = list(detecter(texte))
            for categorie, regle, debut, fin in trouvees:
                position, longueur = en_indices_utf16(texte, debut, fin)
                detections.append({
                    "categorie": categorie,
                    "start": position,
                    "length": longueur,
                    "regle": regle,
                })
            reponse = {"detections": detections, "pic_memoire_octets": pic_memoire_octets()}
        except Exception as erreur:  # remonte au benchmark, qui classe le document en erreur
            reponse = {"erreur": f"{type(erreur).__name__}: {erreur}"}

        print(json.dumps(reponse), flush=True)


if __name__ == "__main__":
    main()
