FROM python:3.12-slim-bookworm

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

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

ENTRYPOINT ["python", "generer.py"]
