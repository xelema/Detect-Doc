FROM python:3.12-slim-bookworm

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1
# Le conteneur tourne sous un compte sans repertoire personnel, voir `compose.yaml`.
# Fontconfig et Matplotlib cherchent alors leurs caches a la racine, ou ils ne peuvent
# pas ecrire, et noient la sortie sous leurs avertissements. Leur designer un dossier
# inscriptible vaut mieux que de creer un compte et son home pour deux fichiers de cache.
ENV HOME=/tmp

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        fonts-crosextra-caladea \
        fonts-crosextra-carlito \
        fonts-croscore \
        fonts-dejavu-core \
        fonts-liberation \
          fonts-noto-core \
          fonts-urw-base35 \
          libgl1 \
          libpango-1.0-0 \
          libpangoft2-1.0-0 \
          libxcb1 \
      && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

# Augraphy telecharge ses textures de papier dans ce dossier, cree au premier appel dans
# le repertoire courant. Il est exclu de l'image par `.dockerignore`, et /app appartient
# a root : sans ce dossier ouvert en ecriture, le conteneur lance sous un compte non
# privilegie echouerait des la premiere degradation.
RUN mkdir -p /app/augraphy_cache && chmod a+rwx /app/augraphy_cache

ENTRYPOINT ["python", "generer.py"]
