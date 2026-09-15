FROM mcr.microsoft.com/dotnet/sdk:10.0.302-noble AS build
WORKDIR /src

COPY ["global.json", "."]
COPY ["DetectDoc.slnx", "."]
COPY ["src/DetectDoc.Api/DetectDoc.Api.csproj", "src/DetectDoc.Api/"]
COPY ["src/DetectDoc.Core/DetectDoc.Core.csproj", "src/DetectDoc.Core/"]
COPY ["src/DetectDoc.Infrastructure/DetectDoc.Infrastructure.csproj", "src/DetectDoc.Infrastructure/"]
COPY ["tests/DetectDoc.Tests/DetectDoc.Tests.csproj", "tests/DetectDoc.Tests/"]
RUN dotnet restore DetectDoc.slnx

COPY . .
RUN dotnet test DetectDoc.slnx --no-restore --filter "Category!=Corpus&Category!=Python"
RUN dotnet publish src/DetectDoc.Api/DetectDoc.Api.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM build AS models
RUN sed --in-place 's|http://archive.ubuntu.com|https://archive.ubuntu.com|g; s|http://security.ubuntu.com|https://security.ubuntu.com|g' /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get update \
    && apt-get install --yes --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /models/v6 \
    && curl --fail --location --silent --show-error https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv6/det/PP-OCRv6_det_tiny.onnx --output /models/v6/PP-OCRv6_det_tiny.onnx \
    && echo "F42C0FBD294D95EAC1A550E131B277DAC97462C8025FA4B6C3CEC1B7894BD3D5  /models/v6/PP-OCRv6_det_tiny.onnx" | sha256sum --check \
    && curl --fail --location --silent --show-error https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv6/rec/PP-OCRv6_rec_tiny.onnx --output /models/v6/PP-OCRv6_rec_tiny.onnx \
    && echo "E16E242DE5937AD92609223F19BC2AFF3727EE40B095F996907C24749BAD251B  /models/v6/PP-OCRv6_rec_tiny.onnx" | sha256sum --check \
    && curl --fail --location --silent --show-error https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/paddle/PP-OCRv6/rec/PP-OCRv6_rec_tiny/ppocrv6_tiny_dict.txt --output /models/v6/ppocrv6_tiny_dict.txt \
    && echo "C5CBE34EF40C29C4DF07ED012BF96569CB69A2D2A01A07027E9F13CB832BD9CD  /models/v6/ppocrv6_tiny_dict.txt" | sha256sum --check

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10-noble AS final
WORKDIR /app

RUN sed --in-place 's|http://archive.ubuntu.com|https://archive.ubuntu.com|g; s|http://security.ubuntu.com|https://security.ubuntu.com|g' /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get update \
    && apt-get install --yes --no-install-recommends python3 python3-venv \
    && rm -rf /var/lib/apt/lists/* \
    && python3 -m venv /opt/pii

COPY src/DetectDoc.Infrastructure/Pii/Ner/requirements.txt /tmp/pii-requirements.txt
RUN /opt/pii/bin/pip install --no-cache-dir --requirement /tmp/pii-requirements.txt \
    && rm /tmp/pii-requirements.txt

COPY --from=build --chown=app:app /app/publish .
COPY --from=models --chown=app:app /models ./models

USER app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENV PII_PYTHON=/opt/pii/bin/python
ENTRYPOINT ["dotnet", "DetectDoc.Api.dll"]
