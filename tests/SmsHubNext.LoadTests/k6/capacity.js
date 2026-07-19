import http from 'k6/http';
import { check, sleep } from 'k6';
import exec from 'k6/execution';
import { Counter } from 'k6/metrics';

const baseUrl = __ENV.BASE_URL || 'http://app:8080';
const apiKey = __ENV.API_KEY || 'smshub-load-test-key-2026';
const senderLine = __ENV.SENDER_LINE || '300099999999';
const messageTypeId = Number(__ENV.MESSAGE_TYPE_ID || 250);
const mode = (__ENV.MODE || 'stress').toLowerCase();
const batchSize = Number(__ENV.BATCH_SIZE || 100);
const startRate = Number(__ENV.START_RATE || 2);
const targetRate = Number(__ENV.TARGET_RATE || 20);
const soakRate = Number(__ENV.SOAK_RATE || 5);
const soakDuration = __ENV.SOAK_DURATION || '1h';
const unexpectedResponses = new Counter('unexpected_responses');

if (!['stress', 'soak'].includes(mode)) {
  throw new Error(`MODE must be either stress or soak; received '${mode}'.`);
}
if (!Number.isInteger(batchSize) || batchSize < 1 || batchSize > 1000) {
  throw new Error(`BATCH_SIZE must be an integer from 1 through 1000; received '${batchSize}'.`);
}
if (!Number.isInteger(messageTypeId) || messageTypeId < 1 || messageTypeId > 255) {
  throw new Error(`MESSAGE_TYPE_ID must be an integer from 1 through 255; received '${messageTypeId}'.`);
}
if (![startRate, targetRate, soakRate].every((rate) => Number.isInteger(rate) && rate > 0)) {
  throw new Error('START_RATE, TARGET_RATE, and SOAK_RATE must be positive integers.');
}

const scenario = mode === 'soak'
  ? {
      executor: 'constant-arrival-rate',
      rate: soakRate,
      timeUnit: '1s',
      duration: soakDuration,
      preAllocatedVUs: Math.max(20, soakRate * 4),
      maxVUs: Math.max(40, soakRate * 8),
    }
  : {
      executor: 'ramping-arrival-rate',
      startRate,
      timeUnit: '1s',
      preAllocatedVUs: Math.max(40, targetRate * 4),
      maxVUs: Math.max(80, targetRate * 8),
      stages: [
        { target: startRate, duration: '1m' },
        { target: Math.ceil(targetRate / 2), duration: '2m' },
        { target: targetRate, duration: '2m' },
        { target: targetRate, duration: '5m' },
        { target: 0, duration: '1m' },
      ],
    };

export const options = {
  setupTimeout: '120s',
  scenarios: { capacity: scenario },
  thresholds: {
    checks: ['rate>0.999'],
    http_req_failed: ['rate<0.001'],
    http_req_duration: ['p(95)<3000'],
    dropped_iterations: ['count==0'],
    unexpected_responses: ['count==0'],
  },
};

export function setup() {
  for (let attempt = 0; attempt < 60; attempt += 1) {
    const response = http.get(`${baseUrl}/health/live`, { timeout: '2s' });
    if (response.status === 200) {
      return;
    }
    sleep(1);
  }
  throw new Error('SmsHubNext did not become live before the capacity-test timeout.');
}

export default function () {
  const clientBatchId = `capacity-${mode}-${exec.scenario.iterationInTest}-${exec.vu.idInTest}-${Date.now()}`;
  const messages = [];
  for (let index = 0; index < batchSize; index += 1) {
    messages.push({
      recipient: `98912${String(index % 10000000).padStart(7, '0')}`,
      text: `Capacity ${clientBatchId} item ${index}`,
      clientCorrelatedId: `${clientBatchId}-${index}`,
    });
  }

  const response = http.post(
    `${baseUrl}/messages`,
    JSON.stringify({
      senderLine,
      messageTypeId,
      clientBatchId,
      messages,
    }),
    {
      headers: {
        'Content-Type': 'application/json',
        'X-Api-Key': apiKey,
      },
      timeout: '30s',
      tags: { endpoint: 'send_messages', mode },
    },
  );

  const accepted = check(response, {
    'capacity send returns 202': (result) => result.status === 202,
    'capacity send accepts all messages': (result) => result.json('data.acceptedCount') === batchSize,
  });

  if (!accepted) {
    unexpectedResponses.add(1);
    console.error(`Unexpected capacity response: status=${response.status}, body=${response.body}`);
  }
}
