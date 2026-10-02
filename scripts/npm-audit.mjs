#!/usr/bin/env node
// `npm audit` with a reviewed, expiring allow-list.
//
// npm has no way to accept a single advisory, so one advisory without a patched release (e.g. in build
// tooling) would otherwise turn CI red for every change. This runs `npm audit --json` in the current
// directory, drops advisories listed in ./audit-allowlist.json and fails on anything else at or above the
// given level. Every allow-list entry needs a reason and an expiry date; an expired entry fails the run so
// the exception is reviewed instead of becoming permanent.
//
// Usage: node ../../scripts/npm-audit.mjs --audit-level=high
import { spawnSync } from 'node:child_process'
import { existsSync, readFileSync } from 'node:fs'

const levels = ['info', 'low', 'moderate', 'high', 'critical']
const levelArg = process.argv.find(arg => arg.startsWith('--audit-level='))?.split('=')[1] ?? 'high'
const threshold = levels.indexOf(levelArg)
if (threshold < 0) {
  console.error(`Unknown audit level "${levelArg}".`)
  process.exit(2)
}

const allowlist = existsSync('audit-allowlist.json') ? JSON.parse(readFileSync('audit-allowlist.json', 'utf8')) : []
const today = new Date().toISOString().slice(0, 10)
let failed = false
const allowed = new Map()
for (const entry of allowlist) {
  if (!entry.id || !entry.reason || !entry.expires) {
    console.error(`Allow-list entry ${JSON.stringify(entry)} needs id, reason and expires.`)
    failed = true
  } else if (entry.expires < today) {
    console.error(`Allow-list entry ${entry.id} expired on ${entry.expires}; review it and fix or renew.`)
    failed = true
  } else {
    allowed.set(entry.id, entry)
  }
}

const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm'
const result = spawnSync(npm, ['audit', '--json'], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 })
let report
try {
  report = JSON.parse(result.stdout)
} catch {
  console.error('npm audit did not return JSON:', result.stderr || result.stdout)
  process.exit(2)
}

// Every vulnerable package traces back to at least one advisory object in some `via` list.
const advisories = new Map()
for (const vulnerability of Object.values(report.vulnerabilities ?? {})) {
  for (const via of vulnerability.via ?? []) {
    if (typeof via === 'object' && via.url) advisories.set(via.url, via)
  }
}

const blocking = []
for (const advisory of advisories.values()) {
  const id = advisory.url.split('/').pop()
  if (levels.indexOf(advisory.severity) < threshold) continue
  if (allowed.has(id)) {
    console.log(`Allowed ${id} (${advisory.severity}, ${advisory.name}) until ${allowed.get(id).expires}: ${allowed.get(id).reason}`)
    continue
  }
  blocking.push(advisory)
}

for (const advisory of blocking)
  console.error(`${advisory.severity.toUpperCase()} ${advisory.name} ${advisory.range}: ${advisory.title} (${advisory.url})`)
if (blocking.length > 0) {
  console.error(`${blocking.length} advisory(ies) at or above "${levelArg}". Fix them or, if no fix exists, add a reviewed entry to audit-allowlist.json.`)
  failed = true
}
if (failed) process.exit(1)
console.log(`No unallowed advisories at or above "${levelArg}".`)
