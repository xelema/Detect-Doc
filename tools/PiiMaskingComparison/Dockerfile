FROM mcr.microsoft.com/dotnet/sdk:10.0.302-noble AS build
WORKDIR /src

COPY ["PiiMaskingComparison/global.json", "PiiMaskingComparison/PiiMaskingComparison.csproj", "PiiMaskingComparison/"]
RUN dotnet restore PiiMaskingComparison/PiiMaskingComparison.csproj

COPY . .
RUN dotnet publish PiiMaskingComparison/PiiMaskingComparison.csproj --configuration Release --no-restore --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0.10-noble AS final

# Les moteurs NER vivent dans un venv du conteneur : c'est la raison d'être de l'image, ne pas
# avoir à installer un runtime Python et des modèles sur le poste. Les versions sont épinglées
# et pip vérifie l'empreinte des modèles spaCy, déclarée dans le fichier de dépendances.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends python3 python3-venv \
    && rm -rf /var/lib/apt/lists/*

COPY PiiMaskingComparison/Ner/requirements.txt /tmp/requirements.txt

# torch vient de l'index CPU de PyTorch : l'index par défaut livre les roues CUDA, deux
# gigaoctets de pilotes dont un comparatif sur processeur n'a aucun usage.
RUN python3 -m venv /opt/ner \
    && /opt/ner/bin/pip install --no-cache-dir --index-url https://download.pytorch.org/whl/cpu torch==2.14.0 \
    && /opt/ner/bin/pip install --no-cache-dir --requirement /tmp/requirements.txt

# Les poids sont figés à une révision et téléchargés au build : un run ne doit dépendre ni du
# réseau, ni de ce que le dépôt distant contient le jour où il tourne.
ENV HF_HOME=/opt/modeles
ENV HF_HUB_DISABLE_XET=1
RUN /opt/ner/bin/python -c "from huggingface_hub import snapshot_download; snapshot_download('urchade/gliner_multi_pii-v1', revision='1fcf13e85f4eef5394e1fcd406cf2ca9ea82351d'); snapshot_download('fastino/gliner2.5-multi-v1', revision='aaecfe45db1d828c963717054ccb868e8ad1f1d5'); snapshot_download('microsoft/mdeberta-v3-base', revision='a0484667b22365f84929a935b5e50a51f71f159d', allow_patterns=['*.json', 'spm.model', 'tokenizer*'])" \
    && chown --recursive app:app /opt/modeles

ENV PII_PYTHON=/opt/ner/bin/python

COPY --from=build /app /app

# Le nom du dossier de run porte l'heure locale. Sans ce réglage le conteneur est en UTC et ses
# runs se rangent deux heures avant ceux lancés depuis le poste. tzdata est déjà dans l'image.
ENV TZ=Europe/Paris

# Le tool écrit ses runs sous `run/`, relatif au dossier courant. `/work/run` est le point de
# montage attendu ; le créer ici évite un échec de permission quand le montage est oublié.
WORKDIR /work
RUN mkdir -p /work/run && chown --recursive app:app /work

USER app
ENTRYPOINT ["dotnet", "/app/PiiMaskingComparison.dll"]
