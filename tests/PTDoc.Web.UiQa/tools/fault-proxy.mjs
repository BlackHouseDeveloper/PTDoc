import { randomUUID } from 'node:crypto';
import http from 'node:http';
import https from 'node:https';

const CONTROL_PATH = '/__ptdoc_live_faults/rules';
const CONTROL_HEADER = 'x-ptdoc-live-control';
const MAX_DELAY_MS = 10_000;
const MAX_OCCURRENCES = 5;

const ALLOWED_PATHS = [
  /^\/api\/v1\/dashboard\/(?:snapshot|alerts)\/?$/,
  /^\/api\/v1\/appointments\/[0-9a-f-]{36}\/appointment-type\/?$/i,
  /^\/api\/v1\/patients\/?$/,
  /^\/api\/v1\/patients\/[0-9a-f-]{36}\/insurance-policies(?:\/.*)?$/i,
  /^\/api\/v1\/intake(?:\/[0-9a-f-]{36})?(?:\/.*)?$/i,
  /^\/api\/v1\/(?:providers|provider-directory)(?:\/.*)?$/i,
  /^\/api\/v1\/admin\/providers(?:\/.*)?$/i,
  /^\/api\/v1\/(?:admin\/)?(?:note-templates|documentation-templates)(?:\/.*)?$/i
];

const HOP_BY_HOP_HEADERS = new Set([
  'connection',
  'keep-alive',
  'proxy-authenticate',
  'proxy-authorization',
  'te',
  'trailer',
  'transfer-encoding',
  'upgrade'
]);

export async function startFaultProxy({
  upstream,
  host = '127.0.0.1',
  port = 0,
  nonce = randomUUID(),
  logger = () => { }
}) {
  const upstreamUrl = new URL(upstream);
  if (!['http:', 'https:'].includes(upstreamUrl.protocol)) {
    throw new Error('Fault proxy upstream must use http or https.');
  }

  const rules = [];
  const server = http.createServer(async (request, response) => {
    try {
      const requestUrl = new URL(request.url ?? '/', `http://${request.headers.host ?? host}`);
      if (requestUrl.pathname === CONTROL_PATH) {
        await handleControlRequest(request, response, rules, nonce);
        return;
      }

      const method = (request.method ?? 'GET').toUpperCase();
      const matchingRule = rules.find(rule => rule.method === method && rule.path === requestUrl.pathname);
      if (matchingRule) {
        matchingRule.remaining -= 1;
        if (matchingRule.remaining === 0) {
          rules.splice(rules.indexOf(matchingRule), 1);
        }

        logger({ method, path: requestUrl.pathname, outcome: matchingRule.status ? 'fault' : 'delay' });
        if (matchingRule.delayMs > 0) {
          await delay(matchingRule.delayMs);
        }
        if (matchingRule.status) {
          request.resume();
          writeJson(response, matchingRule.status, {
            error: 'Controlled live verification failure.',
            code: 'live_verification_fault'
          });
          return;
        }
      }

      await forwardRequest(request, response, upstreamUrl, requestUrl);
    } catch {
      if (!response.headersSent) {
        writeJson(response, 502, {
          error: 'The live verification proxy could not reach the isolated API.',
          code: 'live_verification_proxy_error'
        });
      } else {
        response.destroy();
      }
    }
  });

  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, host, () => {
      server.off('error', reject);
      resolve();
    });
  });

  const address = server.address();
  if (!address || typeof address === 'string') {
    await closeServer(server);
    throw new Error('Fault proxy did not bind to a TCP port.');
  }

  const origin = `http://${host}:${address.port}`;
  return {
    origin,
    controlUrl: `${origin}${CONTROL_PATH}`,
    nonce,
    close: () => closeServer(server)
  };
}

export function validateFaultRule(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error('Fault rule must be an object.');
  }

  const method = String(value.method ?? '').trim().toUpperCase();
  const path = String(value.path ?? '').trim();
  const delayMs = Number(value.delayMs ?? 0);
  const status = value.status === undefined || value.status === null ? null : Number(value.status);
  const occurrences = Number(value.occurrences ?? 1);

  if (!['GET', 'POST', 'PUT', 'PATCH', 'DELETE'].includes(method)) {
    throw new Error('Fault rule method is not allowed.');
  }
  if (!path.startsWith('/') || path.includes('?') || !ALLOWED_PATHS.some(pattern => pattern.test(path))) {
    throw new Error('Fault rule path is not allowlisted.');
  }
  if (!Number.isInteger(delayMs) || delayMs < 0 || delayMs > MAX_DELAY_MS) {
    throw new Error(`Fault rule delayMs must be an integer from 0 to ${MAX_DELAY_MS}.`);
  }
  if (status !== null && (!Number.isInteger(status) || status < 400 || status > 599)) {
    throw new Error('Fault rule status must be null or an HTTP error status from 400 to 599.');
  }
  if (!Number.isInteger(occurrences) || occurrences < 1 || occurrences > MAX_OCCURRENCES) {
    throw new Error(`Fault rule occurrences must be an integer from 1 to ${MAX_OCCURRENCES}.`);
  }
  if (status === null && delayMs === 0) {
    throw new Error('Fault rule must specify a delay or an error status.');
  }

  return {
    id: randomUUID(),
    method,
    path,
    delayMs,
    status,
    remaining: occurrences
  };
}

async function handleControlRequest(request, response, rules, nonce) {
  if (request.headers[CONTROL_HEADER] !== nonce) {
    request.resume();
    writeJson(response, 403, { error: 'Fault proxy control access denied.' });
    return;
  }

  if (request.method === 'GET') {
    writeJson(response, 200, { rules: rules.map(sanitizeRule) });
    return;
  }
  if (request.method === 'DELETE') {
    rules.splice(0, rules.length);
    request.resume();
    response.writeHead(204).end();
    return;
  }
  if (request.method !== 'POST') {
    request.resume();
    response.setHeader('allow', 'GET, POST, DELETE');
    writeJson(response, 405, { error: 'Fault proxy control method is not allowed.' });
    return;
  }

  try {
    const body = await readJsonBody(request);
    const rule = validateFaultRule(body);
    rules.push(rule);
    writeJson(response, 201, sanitizeRule(rule));
  } catch (error) {
    writeJson(response, 400, { error: error.message });
  }
}

function sanitizeRule(rule) {
  return {
    id: rule.id,
    method: rule.method,
    path: rule.path,
    delayMs: rule.delayMs,
    status: rule.status,
    remaining: rule.remaining
  };
}

async function forwardRequest(request, response, upstreamUrl, requestUrl) {
  const transport = upstreamUrl.protocol === 'https:' ? https : http;
  const headers = {};
  for (const [name, value] of Object.entries(request.headers)) {
    if (!HOP_BY_HOP_HEADERS.has(name.toLowerCase()) && name.toLowerCase() !== 'host' && value !== undefined) {
      headers[name] = value;
    }
  }

  const targetPath = `${joinPaths(upstreamUrl.pathname, requestUrl.pathname)}${requestUrl.search}`;
  await new Promise((resolve, reject) => {
    const upstreamRequest = transport.request({
      protocol: upstreamUrl.protocol,
      hostname: upstreamUrl.hostname,
      port: upstreamUrl.port,
      method: request.method,
      path: targetPath,
      headers
    }, upstreamResponse => {
      const responseHeaders = {};
      for (const [name, value] of Object.entries(upstreamResponse.headers)) {
        if (!HOP_BY_HOP_HEADERS.has(name.toLowerCase()) && value !== undefined) {
          responseHeaders[name] = value;
        }
      }
      response.writeHead(upstreamResponse.statusCode ?? 502, responseHeaders);
      upstreamResponse.pipe(response);
      upstreamResponse.once('end', resolve);
      upstreamResponse.once('error', reject);
    });
    upstreamRequest.once('error', reject);
    request.pipe(upstreamRequest);
  });
}

function joinPaths(prefix, suffix) {
  const normalizedPrefix = prefix.endsWith('/') ? prefix.slice(0, -1) : prefix;
  return `${normalizedPrefix}${suffix}` || '/';
}

async function readJsonBody(request) {
  const chunks = [];
  let length = 0;
  for await (const chunk of request) {
    length += chunk.length;
    if (length > 16_384) {
      throw new Error('Fault proxy control payload is too large.');
    }
    chunks.push(chunk);
  }
  return JSON.parse(Buffer.concat(chunks).toString('utf8'));
}

function writeJson(response, status, body) {
  const payload = JSON.stringify(body);
  response.writeHead(status, {
    'content-type': 'application/json; charset=utf-8',
    'content-length': Buffer.byteLength(payload),
    'cache-control': 'no-store'
  });
  response.end(payload);
}

function delay(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

function closeServer(server) {
  server.closeIdleConnections?.();
  server.closeAllConnections?.();
  return new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
}
