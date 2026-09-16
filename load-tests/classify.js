import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.0.2/index.js';

const errorRate = new Rate('errors');
const statusCounter = new Counter('http_status');
const extractionMs = new Trend('stage_extraction_ms', true);
const piiMs = new Trend('stage_pii_ms', true);
const classificationMs = new Trend('stage_classification_ms', true);
const durationByEngine = new Trend('duration_by_engine_ms', true);

// Pas de prefixe K6_ : K6_VUS/K6_ITERATIONS sont natives et ecrasent le scenario.
const VUS = Number(__ENV.LOAD_VUS || '5');
const ITERATIONS = Number(__ENV.LOAD_ITERATIONS || String(VUS * 5));

export const options = {
    insecureSkipTLSVerify: true,
    // Un run par palier de concurrence : le ratio VUs/RAM se lit run par run.
    scenarios: {
        classify: {
            executor: 'shared-iterations',
            vus: VUS,
            iterations: ITERATIONS,
            maxDuration: __ENV.LOAD_MAX_DURATION || '15m',
        },
    },
    thresholds: {
        'http_req_failed': ['rate<0.1'],
        'errors': ['rate<0.1'],
    },
};

const BASE_URL = (__ENV.DETECTDOC_BASE_URL || 'http://localhost:8080').replace(/\/+$/, '');
const CORPUS_DIR = (__ENV.K6_CORPUS_DIR || '.').replace(/\/+$/, '');
const TYPES_PATH = __ENV.K6_TYPES_PATH || 'descriptions-types.json';

// Liste JSON envoyée telle quelle dans le champ `types` de la route.
const TYPES = open(TYPES_PATH);

// Le manifest porte le split : seuls les documents `dev` servent à la charge,
// le split `test` reste scellé pour les mesures de qualité.
const documents = open(`${CORPUS_DIR}/manifest.jsonl`)
    .split('\n')
    .filter((line) => line.trim() !== '')
    .map((line) => JSON.parse(line))
    .filter((doc) => doc.split === 'dev')
    .map((doc) => ({
        nom: doc.fichier.split('/').pop(),
        regime: doc.regime || '',
        data: open(`${CORPUS_DIR}/${doc.fichier}`, 'b'),
    }));

if (documents.length === 0) {
    throw new Error(`Aucun document dev trouvé dans ${CORPUS_DIR}/manifest.jsonl`);
}

// Une ligne REQ|... par requête sur la console : chaque VU a son propre contexte
// JS, un tableau partagé n'arriverait pas jusqu'à handleSummary. L'orchestrateur
// capture la sortie et la transforme en CSV.
function emitRecord(fields) {
    console.log('REQ|' + fields.map((v) => String(v ?? '').replace(/[|\n]/g, ' ')).join('|'));
}

export default function () {
    const doc = documents[Math.floor(Math.random() * documents.length)];

    const response = http.post(
        `${BASE_URL}/api/documents/classify`,
        {
            file: http.file(doc.data, doc.nom, 'application/pdf'),
            types: TYPES,
            engine: 'auto',
        },
        { timeout: '120s' });

    let body = null;
    try {
        body = JSON.parse(response.body);
    } catch (e) { /* corps non JSON : traité comme échec par le check */ }

    const success = check(response, {
        'status is 200': (r) => r.status === 200,
        'response is json': () => body !== null,
    });
    errorRate.add(!success);
    statusCounter.add(1, { status: String(response.status) });

    const record = {
        vu: __VU,
        iter: __ITER,
        doc: doc.nom,
        regime: doc.regime,
        status: response.status,
        duration_ms: Math.round(response.timings.duration),
        error_title: '',
    };

    if (response.status === 200 && body) {
        record.moteur = body.moteur || '';
        record.type_predit = body.type || '';
        record.confiance = body.confiance ?? '';
        record.pages = body.pagesTraitees ?? '';
        record.extraction_ms = body.extractionMs ?? '';
        record.pii_ms = body.piiMaskingMs ?? '';
        record.classification_ms = body.classificationMs ?? '';
        record.total_ms = body.totalMs ?? '';
        record.execution_id = body.executionId || '';
        extractionMs.add(body.extractionMs || 0);
        piiMs.add(body.piiMaskingMs || 0);
        classificationMs.add(body.classificationMs || 0);
        if (body.moteur) {
            durationByEngine.add(response.timings.duration, { moteur: body.moteur });
        }
    } else {
        // Le ProblemDetails nomme l'étape fautive dans `title`.
        record.error_title = (body && body.title) || (response.body || '').slice(0, 120);
        record.execution_id = (body && body.executionId) || '';
    }

    emitRecord(['req', record.vu, record.iter, record.doc, record.regime, record.status,
        record.duration_ms, record.moteur, record.type_predit, record.confiance, record.pages,
        record.extraction_ms, record.pii_ms, record.classification_ms, record.total_ms,
        record.execution_id, record.error_title]);
    sleep(1);
}

export function setup() {
    console.log(`Cible : ${BASE_URL}/api/documents/classify`);
    console.log(`Corpus : ${documents.length} documents dev, engine=auto`);
    console.log(`VUs : ${VUS}, iterations : ${ITERATIONS}`);
}

const RUN_ID = (__ENV.K6_RUN_ID || `vus${VUS}`).replace(/[:\\/\s]+/g, '-');
const RESULTS_DIR = (__ENV.K6_RESULTS_DIR || '.').replace(/\/+$/, '');

export function handleSummary(data) {
    const base = `${RESULTS_DIR}/k6-${RUN_ID}`;

    return {
        stdout: textSummary(data, { indent: ' ', enableColors: true }),
        [`${base}.summary.txt`]: textSummary(data, { indent: ' ', enableColors: false }),
    };
}
