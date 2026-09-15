import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.0.2/index.js';

const errorRate = new Rate('errors');

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
        data: open(`${CORPUS_DIR}/${doc.fichier}`, 'b'),
    }));

if (documents.length === 0) {
    throw new Error(`Aucun document dev trouvé dans ${CORPUS_DIR}/manifest.jsonl`);
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

    const success = check(response, {
        'status is 200': (r) => r.status === 200,
        'response is json': (r) => {
            try {
                return JSON.parse(r.body) !== null;
            } catch (e) {
                return false;
            }
        },
    });
    errorRate.add(!success);

    if (!success) {
        console.log(`Echec : status=${response.status} doc=${doc.nom} duree=${response.timings.duration}ms`);
    }

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
    return {
        stdout: textSummary(data, { indent: ' ', enableColors: true }),
        [`${RESULTS_DIR}/k6-${RUN_ID}.summary.txt`]: textSummary(data, { indent: ' ', enableColors: false }),
    };
}
