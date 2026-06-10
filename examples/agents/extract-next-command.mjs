#!/usr/bin/env node

import { readFileSync } from 'node:fs';

try {
  const envelope = readEnvelope(readFileSync(0, 'utf8'));
  const command = extractNextCommand(envelope);
  if (!command) {
    process.stderr.write('extract-next-command: no follow-up command found\n');
    process.exitCode = 1;
  } else {
    process.stdout.write(`${command}\n`);
  }
} catch (error) {
  process.stderr.write(`extract-next-command: ${error.message}\n`);
  process.exitCode = 1;
}

export function readEnvelope(text) {
  const input = String(text).trim();
  if (input.length === 0) {
    throw new Error('stdin did not contain JSON');
  }

  let parsed = null;
  try {
    parsed = JSON.parse(input);
  } catch {
    parsed = null;
  }

  if (isObject(parsed)) {
    return parsed;
  }

  if (Array.isArray(parsed)) {
    for (const item of [...parsed].reverse()) {
      if (isCommandEnvelope(item)) {
        return item;
      }
    }
  }

  const envelopes = [];
  for (const rawLine of input.split(/\r?\n/u)) {
    const line = rawLine.trim();
    if (line.length === 0) {
      continue;
    }

    try {
      const item = JSON.parse(line);
      if (isCommandEnvelope(item)) {
        envelopes.push(item);
      }
    } catch {
      // Ignore non-JSON log lines; agents often pass saved mixed logs here.
    }
  }

  if (envelopes.length > 0) {
    return envelopes.at(-1);
  }

  throw new Error('stdin did not contain a Luotsi command envelope');
}

export function extractNextCommand(envelope) {
  const data = envelope?.data;
  if (isObject(data)) {
    const direct = cleanCommand(data.recommended_next_action?.command);
    if (direct) {
      return direct;
    }

    for (const name of ['recommended_next_steps', 'next_actions', 'suggested_commands', 'commands', 'artifact_commands', 'recommended_commands']) {
      const command = firstCommand(data[name], { preferReplayOpen: ['commands', 'artifact_commands', 'recommended_commands'].includes(name) });
      if (command) {
        return command;
      }
    }
  }

  const artifactRoot = cleanCommand(envelope?.artifacts?.artifact_root);
  if (artifactRoot) {
    return `luotsi replay open --artifacts ${quoteShellArg(artifactRoot)} --dry-run`;
  }

  return null;
}

function isCommandEnvelope(value) {
  return isObject(value) && (value.schema === 'luotsi-command.v1' ||
    'ok' in value ||
    'data' in value ||
    'artifacts' in value);
}

function firstCommand(items, { preferReplayOpen }) {
  if (!Array.isArray(items)) {
    return null;
  }

  const commands = items.filter((item) => isObject(item) && cleanCommand(item.command));
  if (preferReplayOpen) {
    const replay = commands.find((item) => String(item.kind ?? '').toLowerCase() === 'replay_open' ||
      String(item.command ?? '').toLowerCase().includes('replay open'));
    if (replay) {
      return cleanCommand(replay.command);
    }
  }

  return commands.length > 0 ? cleanCommand(commands[0].command) : null;
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function cleanCommand(value) {
  if (typeof value !== 'string') {
    return null;
  }

  const stripped = value.trim();
  return stripped.length > 0 ? stripped : null;
}

function quoteShellArg(value) {
  const text = String(value);
  const safe = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_./:=-\\';
  if (text.length > 0 && [...text].every((character) => safe.includes(character))) {
    return text;
  }

  return `'${text.replaceAll("'", "'\"'\"'")}'`;
}
