FROM mcr.microsoft.com/dotnet/sdk:10.0.302-noble AS build
WORKDIR /src

COPY ["PiiMaskingComparison/global.json", "PiiMaskingComparison/PiiMaskingComparison.csproj", "PiiMaskingComparison/"]
RUN dotnet restore PiiMaskingComparison/PiiMaskingComparison.csproj

COPY . .
RUN dotnet publish PiiMaskingComparison/PiiMaskingComparison.csproj --configuration Release --no-restore --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0.10-noble AS final

# Presidio et son modèle spaCy français vivent dans un venv du conteneur
RUN apt-get update \
    && apt-get install --yes --no-install-recommends python3 python3-venv \
    && rm -rf /var/lib/apt/lists/*

COPY PiiMaskingComparison/Presidio/requirements.txt /tmp/requirements.txt
RUN python3 -m venv /opt/presidio \
    && /opt/presidio/bin/pip install --no-cache-dir --requirement /tmp/requirements.txt
ENV PII_PYTHON=/opt/presidio/bin/python

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
