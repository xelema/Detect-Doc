FROM mcr.microsoft.com/dotnet/sdk:10.0.302-noble AS build
WORKDIR /src

COPY ["PiiMaskingComparison/global.json", "PiiMaskingComparison/PiiMaskingComparison.csproj", "PiiMaskingComparison/"]
RUN dotnet restore PiiMaskingComparison/PiiMaskingComparison.csproj

COPY . .
RUN dotnet publish PiiMaskingComparison/PiiMaskingComparison.csproj --configuration Release --no-restore --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0.10-noble AS final
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
