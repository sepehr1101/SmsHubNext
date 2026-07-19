import http from 'k6/http';
import { check, sleep } from 'k6';
import exec from 'k6/execution';
import { Counter } from 'k6/metrics';

const baseUrl = __ENV.BASE_URL || 'http://app:8080';
const apiKey = __ENV.API_KEY || 'smshub-load-test-key-2026';
const unexpectedResponses = new Counter('unexpected_responses');

export const options = {
  setupTimeout: '120s',
  scenarios: {
    steady_acceptance: {
      executor: 'constant-arrival-rate',
      exec: 'sendUniqueBatch',
      rate: 2,
      timeUnit: '1s',
      duration: '20s',
      preAllocatedVUs: 20,
      maxVUs: 40,
      tags: { workload: 'steady' },
    },
    idempotency_storm: {
      executor: 'per-vu-iterations',
      exec: 'retrySameBatch',
      vus: 50,
      iterations: 1,
      startTime: '22s',
      maxDuration: '30s',
      tags: { workload: 'idempotency' },
    },
  },
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

  throw new Error('SmsHubNext did not become live before the load-test timeout.');
}

export function sendUniqueBatch() {
  const clientBatchId = `load-${exec.scenario.iterationInTest}-${exec.vu.idInTest}-${Date.now()}`;
  send(clientBatchId, messagesFor(clientBatchId));
}

export function retrySameBatch() {
  const clientBatchId = 'load-idempotency-shared';
  send(clientBatchId, messagesFor(clientBatchId));
}

function messagesFor(seed) {
  const messages = [];
  for (let index = 0; index < 10; index += 1) {
    messages.push({
      recipient: `98912${String(index).padStart(7, '0')}`,
      text: `Load test ${seed} item ${index}`,
      clientCorrelatedId: `${seed}-${index}`,
    });
  }
  return messages;
}

function send(clientBatchId, messages) {
  const response = http.post(
    `${baseUrl}/messages`,
    JSON.stringify({
      senderLine: '300099999999',
      messageTypeId: 250,
      clientBatchId,
      messages,
    }),
    {
      headers: {
        'Content-Type': 'application/json',
        'X-Api-Key': apiKey,
      },
      timeout: '15s',
      tags: { endpoint: 'send_messages' },
    },
  );

  const accepted = check(response, {
    'send returns 202': (result) => result.status === 202,
    'send response is successful': (result) => result.json('success') === true,
    'all ten messages are accepted': (result) => result.json('data.acceptedCount') === 10,
    'server batch id is returned': (result) => Number(result.json('data.batchId')) > 0,
  });

  if (!accepted) {
    unexpectedResponses.add(1);
    console.error(`Unexpected send response: status=${response.status}, body=${response.body}`);
  }
}
