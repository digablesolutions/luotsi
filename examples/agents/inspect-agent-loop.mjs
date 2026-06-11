#!/usr/bin/env node

import { spawn, spawnSync } from 'node:child_process';
import { once } from 'node:events';
import { mkdirSync } from 'node:fs';
import { extname } from 'node:path';
import { createInterface } from 'node:readline';

const usage = `Usage:
  node examples/agents/inspect-agent-loop.mjs --device <serial> [options]
  bun examples/agents/inspect-agent-loop.mjs --device <serial> [options]

Options:
  --device <serial>             ADB serial to pass to luotsi inspect.
  --text <text>                 Visible text to wait for and tap. Defaults to "Sign in".
  --text-match <mode>           Text match mode: exact or contains. Defaults to exact.
  --content-description <text>  Optional content description selector.
  --resource-id <id>            Optional Android resource-id selector.
  --class-name <name>           Optional Android class selector.
  --region <l,t,r,b>            Optional selector region for element center.
  --allow-ambiguous             Let Luotsi pick the highest-ranked match if multiple elements match.
  --artifacts <path>            Artifact base directory for the inspect session.
  --luotsi <path>               Luotsi executable path. Defaults to "luotsi".
  --timeout-sec <seconds>       Wait timeout for visible text. Defaults to 15.
  --screenshot-label <label>    Screenshot label. Defaults to "agent-loop".
  --no-tap                      Only wait for text and capture a screenshot.
`;

class JsonLineReader {
  constructor(stream) {
    this.queue = [];
    this.waiters = [];
    this.closed = false;
    this.error = null;

    const lines = createInterface({ input: stream, crlfDelay: Infinity });
    lines.on('line', (line) => this.pushLine(line));
    lines.on('close', () => this.close());
  }

  pushLine(line) {
    try {
      const event = JSON.parse(line);
      process.stdout.write(`${JSON.stringify(event)}\n`);

      const waiter = this.waiters.shift();
      if (waiter) {
        waiter.resolve(event);
      } else {
        this.queue.push(event);
      }
    } catch (error) {
      this.fail(error);
    }
  }

  next(timeoutSec) {
    if (this.queue.length > 0) {
      return Promise.resolve(this.queue.shift());
    }
    if (this.error) {
      return Promise.reject(this.error);
    }
    if (this.closed) {
      return Promise.reject(new Error('Inspect session ended before the expected event arrived.'));
    }

    return new Promise((resolve, reject) => {
      const waiter = {
        resolve: (event) => {
          clearTimeout(timer);
          resolve(event);
        },
        reject: (error) => {
          clearTimeout(timer);
          reject(error);
        },
      };
      const timer = setTimeout(() => {
        this.waiters = this.waiters.filter((candidate) => candidate !== waiter);
        reject(new Error(`No inspect event arrived within ${timeoutSec} seconds.`));
      }, timeoutSec * 1000);

      this.waiters.push(waiter);
    });
  }

  close() {
    this.closed = true;
    this.fail(this.error ?? new Error('Inspect session ended before the expected event arrived.'));
  }

  fail(error) {
    this.error = error;
    const waiters = this.waiters;
    this.waiters = [];
    for (const waiter of waiters) {
      waiter.reject(error);
    }
  }
}

function parseArgs(argv) {
  const args = {
    text: 'Sign in',
    textMatch: 'exact',
    luotsi: 'luotsi',
    timeoutSec: 15,
    screenshotLabel: 'agent-loop',
  };

  for (let index = 0; index < argv.length; index += 1) {
    const name = argv[index];
    if (name === '--help' || name === '-h') {
      process.stdout.write(usage);
      process.exit(0);
    }
    if (name === '--no-tap') {
      args.noTap = true;
      continue;
    }
    if (name === '--allow-ambiguous') {
      args.allowAmbiguous = true;
      continue;
    }

    const value = argv[index + 1];
    if (!name.startsWith('--') || value === undefined || value.startsWith('--')) {
      throw new Error(`Missing value for ${name}.\n${usage}`);
    }

    index += 1;
    switch (name) {
      case '--device':
        args.device = value;
        break;
      case '--text':
        args.text = value;
        break;
      case '--text-match':
        args.textMatch = parseMatchMode(value, '--text-match');
        break;
      case '--content-description':
        args.contentDescription = value;
        break;
      case '--resource-id':
        args.resourceId = value;
        break;
      case '--class-name':
        args.className = value;
        break;
      case '--region':
        args.region = parseRegion(value);
        break;
      case '--artifacts':
        args.artifacts = value;
        break;
      case '--luotsi':
        args.luotsi = value;
        break;
      case '--timeout-sec':
        args.timeoutSec = Number.parseInt(value, 10);
        if (!Number.isFinite(args.timeoutSec) || args.timeoutSec <= 0) {
          throw new Error('--timeout-sec must be a positive integer.');
        }
        break;
      case '--screenshot-label':
        args.screenshotLabel = value;
        break;
      default:
        throw new Error(`Unknown option: ${name}\n${usage}`);
    }
  }

  if (!args.device) {
    throw new Error(`Missing required --device.\n${usage}`);
  }

  return args;
}

function parseMatchMode(value, optionName) {
  const normalized = value.toLowerCase();
  if (normalized === 'exact' || normalized === 'contains') {
    return normalized;
  }

  throw new Error(`${optionName} must be "exact" or "contains".`);
}

function parseRegion(value) {
  const numbers = value.split(',').map((part) => Number.parseInt(part.trim(), 10));
  if (numbers.length !== 4 || numbers.some((number) => !Number.isFinite(number))) {
    throw new Error('--region must be four integers: left,top,right,bottom.');
  }

  const [left, top, right, bottom] = numbers;
  return { left, top, right, bottom };
}

function startInspect(args) {
  if (args.artifacts) {
    mkdirSync(args.artifacts, { recursive: true });
  }

  const inspectArgs = ['inspect', '--device', args.device];
  if (args.artifacts) {
    inspectArgs.push('--artifacts', args.artifacts);
  }
  const spawnTarget = buildSpawnTarget(args.luotsi, inspectArgs);

  const child = spawn(spawnTarget.command, spawnTarget.args, {
    stdio: ['pipe', 'pipe', 'pipe'],
    windowsHide: true,
  });

  const reader = new JsonLineReader(child.stdout);
  child.on('error', (error) => reader.fail(error));
  child.stdin.on('error', (error) => {
    if (error.code !== 'EPIPE') {
      reader.fail(error);
    }
  });
  child.stderr.on('data', (chunk) => process.stderr.write(chunk));

  return { child, reader };
}

function buildSpawnTarget(command, args) {
  if (process.platform !== 'win32') {
    return { command, args };
  }

  const resolvedCommand = resolveWindowsCommand(command);
  if (!isCommandShim(resolvedCommand)) {
    return { command: resolvedCommand, args };
  }

  return {
    command: process.env.ComSpec ?? 'cmd.exe',
    args: ['/d', '/v:off', '/c', quoteCmdLine([resolvedCommand, ...args])],
  };
}

function resolveWindowsCommand(command) {
  const result = spawnSync('where.exe', [command], {
    encoding: 'utf8',
    windowsHide: true,
  });
  if (result.status !== 0 || !result.stdout) {
    return command;
  }

  return result.stdout
    .split(/\r?\n/u)
    .map((line) => line.trim())
    .find(Boolean) ?? command;
}

function isCommandShim(command) {
  return ['.cmd', '.bat'].includes(extname(command).toLowerCase());
}

function quoteCmdLine(parts) {
  return parts.map(quoteCmdArg).join(' ');
}

function quoteCmdArg(value) {
  return `"${String(value).replaceAll('%', '%%').replaceAll('"', '""')}"`;
}

function sendCommand(session, id, command, payload = {}) {
  if (
    session.child.exitCode !== null ||
    session.child.signalCode !== null ||
    session.child.stdin.destroyed ||
    !session.child.stdin.writable
  ) {
    throw new Error('Inspect stdin is closed.');
  }

  session.child.stdin.write(`${JSON.stringify({ id, command, ...payload })}\n`);
}

function selectorPayload(args) {
  const payload = {
    text: args.text,
    text_match: args.textMatch,
  };
  if (args.contentDescription) {
    payload.content_description = args.contentDescription;
  }
  if (args.resourceId) {
    payload.resource_id = args.resourceId;
  }
  if (args.className) {
    payload.class_name = args.className;
  }
  if (args.region) {
    payload.region = args.region;
  }
  if (args.allowAmbiguous) {
    payload.allow_ambiguous = true;
  }

  return payload;
}

async function waitForInitialState(session, timeoutSec) {
  while (true) {
    const event = await session.reader.next(timeoutSec);
    if (event.type === 'screen_snapshot') {
      return event;
    }
    if (event.type === 'session_error' || event.type === 'protocol_error') {
      throw new Error(`Inspect session failed: ${JSON.stringify(event)}`);
    }
  }
}

async function waitForCommandResult(session, id, timeoutSec, options = {}) {
  let result = null;
  while (true) {
    const event = await session.reader.next(timeoutSec);
    if (event.type === 'session_error' || event.type === 'protocol_error') {
      throw new Error(`Inspect session failed: ${JSON.stringify(event)}`);
    }
    if (event.type === 'command_result' && event.id === id) {
      result = event;
      if (!event.ok || !options.waitForSettle) {
        return event;
      }
    }
    if (options.waitForSettle && event.type === 'screen_delta' && event.id === id && result) {
      return result;
    }
  }
}

async function stopInspect(session) {
  if (!session.child.pid) {
    return;
  }

  if (session.child.exitCode !== null || session.child.signalCode !== null) {
    validateInspectExit(session.child.exitCode, session.child.signalCode);
    return;
  }

  try {
    sendCommand(session, 'exit', 'exit');
  } catch {
    // The process may already have ended after an inspect failure.
  }

  const exit = once(session.child, 'exit');
  let timeoutId;
  const timeout = new Promise((resolve) => {
    timeoutId = setTimeout(() => {
      session.child.kill();
      resolve({ timedOut: true });
    }, 5000);
  });
  const result = await Promise.race([
    exit.then(([code, signal]) => ({ code, signal, timedOut: false })),
    timeout,
  ]);
  clearTimeout(timeoutId);

  if (result.timedOut) {
    throw new Error('Inspect process did not exit within 5 seconds.');
  }

  validateInspectExit(result.code, result.signal);
}

function validateInspectExit(code, signal) {
  if (signal) {
    throw new Error(`Inspect process exited after signal ${signal}.`);
  }
  if (code !== 0) {
    throw new Error(`Inspect process exited with code ${code}.`);
  }
}

async function runLoop(args) {
  const session = startInspect(args);

  try {
    await waitForInitialState(session, args.timeoutSec);

    sendCommand(session, '1', 'wait_visible', {
      ...selectorPayload(args),
      timeout_sec: args.timeoutSec,
    });
    const visible = await waitForCommandResult(session, '1', args.timeoutSec + 5);
    if (!visible.ok) {
      sendCommand(session, '2', 'capture_artifacts', { label: 'agent-loop-wait-failed' });
      await waitForCommandResult(session, '2', args.timeoutSec);
      return 2;
    }

    if (args.noTap) {
      sendCommand(session, '2', 'screenshot', { label: args.screenshotLabel });
      const screenshot = await waitForCommandResult(session, '2', args.timeoutSec);
      if (!screenshot.ok) {
        return 4;
      }
      return 0;
    }

    sendCommand(session, '2', 'tap_text', {
      ...selectorPayload(args),
      timeout_sec: 5,
    });
    const tapped = await waitForCommandResult(session, '2', args.timeoutSec, { waitForSettle: true });
    if (!tapped.ok) {
      return 3;
    }

    sendCommand(session, '3', 'screenshot', { label: args.screenshotLabel });
    const screenshot = await waitForCommandResult(session, '3', args.timeoutSec);
    if (!screenshot.ok) {
      return 4;
    }
    return 0;
  } finally {
    await stopInspect(session);
  }
}

function writeArtifactHandoff(args) {
  if (!args.artifacts) {
    return;
  }

  process.stderr.write(`inspect-agent-loop artifacts: ${args.artifacts}\n`);
  process.stderr.write(`inspect-agent-loop next: luotsi replay packet --last --artifacts ${quoteShellArg(args.artifacts)}\n`);
}

function quoteShellArg(value) {
  const text = String(value);
  if (/^[A-Za-z0-9_./:=-]+$/u.test(text)) {
    return text;
  }

  return `'${text.replaceAll("'", "'\"'\"'")}'`;
}

let args;
try {
  args = parseArgs(process.argv.slice(2));
  const exitCode = await runLoop(args);
  writeArtifactHandoff(args);
  process.exitCode = exitCode;
} catch (error) {
  process.stderr.write(`inspect-agent-loop failed: ${error.message}\n`);
  if (args) {
    writeArtifactHandoff(args);
  }
  process.exitCode = 1;
}
