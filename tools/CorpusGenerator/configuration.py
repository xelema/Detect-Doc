"""Configuration globale lue une fois au démarrage."""

from hashlib import sha256
from pathlib import Path
import tomllib


FICHIER = Path(__file__).with_name("corpus-generator.toml")
SOURCE = FICHIER.read_bytes()
TOML = tomllib.loads(SOURCE.decode("utf-8"))

chemin_corpus = Path(TOML["corpus"]["output_dir"])
DOSSIER_CORPUS = (
    chemin_corpus if chemin_corpus.is_absolute() else FICHIER.parent / chemin_corpus
).resolve()

DOCUMENTS = TOML["documents"]
UNITE_SUR_N_EN_DEV = int(TOML["split"]["dev_every"])

DEGRADATION = TOML["degradation"]
UNITE_SUR_N_DEDOUBLEE = int(DEGRADATION["twin_every"])
RESOLUTION_DPI = DEGRADATION["dpi"]
QUALITE_JPEG = DEGRADATION["container_jpeg_quality"]
POIDS_PROFILS = DEGRADATION["weights"]

SHA256 = sha256(SOURCE).hexdigest()
