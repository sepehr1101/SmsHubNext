import http from 'k6/http';
import { check, sleep } from 'k6';
import exec from 'k6/execution';
import { Counter } from 'k6/metrics';

const baseUrl = __ENV.BASE_URL || 'http://app:8080';
const apiKey = __ENV.API_KEY || 'smshub-smsir-sandbox-customer-key-2026';
const senderLine = __ENV.SENDER_LINE;
const recipient = __ENV.RECIPIENT;
const runId = __ENV.RUN_ID;
const messageTypeId = Number(__ENV.MESSAGE_TYPE_ID || 250);
const messageCount = Number(__ENV.MESSAGE_COUNT || 100000);
const batchSize = Number(__ENV.BATCH_SIZE || 100);
const requestIntervalMs = Number(__ENV.REQUEST_INTERVAL_MS || 1000);
const batchCount = messageCount / batchSize;
const maximumDurationSeconds = Math.ceil(batchCount * requestIntervalMs / 1000) + 600;
const unexpectedResponses = new Counter('unexpected_responses');
const acceptedMessages = new Counter('accepted_messages');

if (!senderLine || !/^\d+$/.test(senderLine)) {
  throw new Error('SENDER_LINE must be a numeric sms.ir sandbox sender line.');
}
if (!recipient || !/^09\d{9}$/.test(recipient)) {
  throw new Error('RECIPIENT must be one Iranian mobile number in 09xxxxxxxxx form.');
}
if (!runId || !/^[a-zA-Z0-9-]+$/.test(runId)) {
  throw new Error('RUN_ID is required and may contain only letters, digits, and hyphens.');
}
if (!Number.isInteger(messageCount) || messageCount < 1 || messageCount > 100000) {
  throw new Error('MESSAGE_COUNT must be an integer from 1 through 100000.');
}
if (!Number.isInteger(batchSize) || batchSize < 1 || batchSize > 100) {
  throw new Error('BATCH_SIZE must be an integer from 1 through the sms.ir limit of 100.');
}
if (messageCount % batchSize !== 0) {
  throw new Error('MESSAGE_COUNT must be divisible by BATCH_SIZE.');
}
if (!Number.isInteger(requestIntervalMs) || requestIntervalMs < 100 || requestIntervalMs > 60000) {
  throw new Error('REQUEST_INTERVAL_MS must be an integer from 100 through 60000.');
}

export const options = {
  setupTimeout: '120s',
  scenarios: {
    smsir_sandbox: {
      executor: 'per-vu-iterations',
      vus: 1,
      iterations: batchCount,
      maxDuration: `${maximumDurationSeconds}s`,
      tags: { workload: 'smsir-sandbox' },
    },
  },
  thresholds: {
    checks: ['rate==1'],
    http_req_failed: ['rate==0'],
    http_req_duration: ['p(95)<5000'],
    unexpected_responses: ['count==0'],
    accepted_messages: [`count==${messageCount}`],
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
  throw new Error('SmsHubNext did not become live before the sms.ir sandbox timeout.');
}

export default function () {
  const batchIndex = exec.scenario.iterationInTest;
  const clientBatchId = `smsir-sandbox-${runId}-${batchIndex}`;
  const messages = [];

  for (let index = 0; index < batchSize; index += 1) {
    const messageIndex = batchIndex * batchSize + index;
    messages.push({
      recipient,
      text: `SmsHubNext sms.ir sandbox ${runId} message ${messageIndex}`,
      clientCorrelatedId: `${runId}-${messageIndex}`,
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
      tags: { endpoint: 'send_messages', provider: 'smsir-sandbox' },
    },
  );

  const accepted = check(response, {
    'sandbox send returns 202': (result) => result.status === 202,
    'sandbox send reports success': (result) => result.json('success') === true,
    'sandbox send accepts the complete batch': (result) => result.json('data.acceptedCount') === batchSize,
    'sandbox send returns a batch id': (result) => Number(result.json('data.batchId')) > 0,
  });

  if (accepted) {
    acceptedMessages.add(batchSize);
  } else {
    unexpectedResponses.add(1);
    console.error(`Unexpected sms.ir sandbox response: status=${response.status}, body=${response.body}`);
  }

  sleep(requestIntervalMs / 1000);
}
