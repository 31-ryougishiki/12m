import test from 'node:test'
import assert from 'node:assert/strict'
import { request } from 'node:http'
import { mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'
import { JobRegistry } from '../lib/job-registry.js'
import { apply } from '../lib/index.js'

test('job slots are released once and retained history never evicts active jobs', () => {
  const registry = new JobRegistry(2, 2)
  assert.equal(registry.tryAdd({ id: 'a' }), true)
  assert.equal(registry.tryAdd({ id: 'b' }), true)
  assert.equal(registry.tryAdd({ id: 'c' }), false)
  registry.finish('a')
  registry.finish('a')
  assert.equal(registry.active, 1)
  assert.equal(registry.tryAdd({ id: 'c' }), true)
  assert.deepEqual([...registry.jobs.keys()], ['b', 'c'])
  registry.close()
  registry.finish('b')
  assert.equal(registry.tryAdd({ id: 'd' }), false)
})

async function until(read) {
  const deadline = Date.now() + 4000
  while (Date.now() < deadline) {
    const value = await read()
    if (value) return value
    await delay(10)
  }
  throw new Error('Timed out waiting for local fake host')
}

function http(port, token, path, method = 'GET', body) {
  return new Promise((resolve, reject) => {
    const req = request({ host: '127.0.0.1', port, path, method, headers: { authorization: `Bearer ${token}` } }, res => {
      let data = ''
      res.on('data', chunk => { data += chunk })
      res.on('end', () => resolve({ status: res.statusCode, body: JSON.parse(data) }))
    })
    req.on('error', reject)
    req.end(body === undefined ? undefined : JSON.stringify(body))
  })
}

function partialPost(port, token) {
  let req
  const response = new Promise((resolve, reject) => {
    req = request({ host: '127.0.0.1', port, path: '/task', method: 'POST', headers: { authorization: `Bearer ${token}` } }, res => {
      let data = ''
      res.on('data', chunk => { data += chunk })
      res.on('end', () => resolve({ status: res.statusCode, body: JSON.parse(data) }))
    })
    req.on('error', reject)
    req.write('{"prompt":')
  })
  return { response, finish: value => req.end(JSON.stringify(value) + '}') }
}

test('HTTP admission is atomic after body parsing; cancellation during creation cannot send a task', async () => {
  const root = mkdtempSync(join(tmpdir(), 'elyms-bridge-test-'))
  const original = process.env.LOCALAPPDATA
  process.env.LOCALAPPDATA = root
  let dispose, resolveCreation, creations = 0, followups = 0, cancellations = 0
  const creation = new Promise(resolve => { resolveCreation = resolve })
  const agent = { cancel() { cancellations++ }, followup() { followups++ }, whenIdle() { return Promise.resolve() } }
  const ctx = {
    logger: () => ({ info() {}, warn() {} }),
    agents: { create() { creations++; return creation }, get() { return undefined } },
    commands: { execute() { return { kind: 'success' } } },
    sessions: { get() { return undefined } },
    effect(callback) { dispose = callback() },
  }
  try {
    apply(ctx, { workspace: root, port: 0, maxActive: 1 })
    const handshake = await until(() => {
      try { return JSON.parse(readFileSync(join(root, 'ElymsDesktopPet', 'dsh-bridge.json'), 'utf8')) } catch { return null }
    })
    const { port, token } = handshake
    assert.equal((await http(port, 'invalid', '/health')).status, 401)
    assert.equal((await http(port, token, '/task', 'POST', { prompt: '' })).status, 400)
    assert.equal((await http(port, token, '/health')).body.active, 0)

    // Both handlers are suspended in readJson before either body is complete.
    const a = partialPost(port, token)
    const b = partialPost(port, token)
    await delay(40)
    a.finish('first')
    b.finish('second')
    const responses = await Promise.all([a.response, b.response])
    assert.deepEqual(responses.map(value => value.status).sort(), [202, 409])
    assert.equal(creations, 1)
    assert.equal((await http(port, token, '/health')).body.active, 1)
    const id = responses.find(value => value.status === 202).body.jobId
    assert.equal((await http(port, token, `/task/${id}/cancel`, 'POST', {})).body.cancelled, true)
    resolveCreation({ agent })
    await until(async () => (await http(port, token, '/health')).body.active === 0)
    assert.equal((await http(port, token, `/task/${id}`)).body.status, 'cancelled')
    assert.equal(followups, 0)
    assert.equal(cancellations, 1)

    // A request whose body finishes after plugin unload must not create work.
    const late = partialPost(port, token)
    await delay(30)
    dispose()
    late.finish('too late')
    assert.equal((await late.response).status, 503)
    assert.equal(creations, 1)
  } finally {
    resolveCreation({ agent })
    dispose?.()
    if (original === undefined) delete process.env.LOCALAPPDATA
    else process.env.LOCALAPPDATA = original
    rmSync(root, { recursive: true, force: true })
  }
})
