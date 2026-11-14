import test from 'node:test'
import assert from 'node:assert/strict'
import { request } from 'node:http'
import { mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'
import { JobRegistry } from '../lib/job-registry.js'
import { apply, wrapTask } from '../lib/index.js'
import { knowledgeManifest } from '../lib/knowledge.js'

test('read-only research wrapper asks for code evidence without write instructions', () => {
  const prompt = wrapTask('阅读源码回答字段用途', 'D:/code/12m', null, 'read-only')
  assert.ok(prompt.includes('当前任务只读') && prompt.includes('实际读取的文件位置'))
  assert.ok(!prompt.includes('实际修改、创建') && !prompt.includes('改了什么'))
  assert.ok(wrapTask('修改代码', 'D:/code/12m', null, 'workspace-write').includes('实际修改、创建'))
})

test('knowledge handoff preserves absolute paths and separates drafts from task instructions', () => {
  const root = join(tmpdir(), '知识库 with spaces')
  const manifest = knowledgeManifest({ archive: '当前存档', root, files: [
    { label: '流程', path: join(root, 'workflows.md') },
    { label: '知识图谱', path: join(root, 'graph.json') },
  ] })
  const prompt = wrapTask('只读核对流程', root, manifest)
  assert.ok(prompt.includes(JSON.stringify(manifest, null, 2)))
  assert.ok(prompt.includes('草稿'))
  assert.ok(prompt.includes('GRAPH_EDIT.md') && prompt.includes('graph-edit-result.json'))
  assert.ok(prompt.includes('不要直接修改这些自动派生文件'))
  assert.throws(() => knowledgeManifest({ ...manifest, files: [{ label: 'x', path: join(root, '..', 'settings.json') }] }))
  assert.throws(() => knowledgeManifest({ ...manifest, files: [{ label: 'x', path: join(root, 'dsh-bridge.json') }] }))
  assert.throws(() => knowledgeManifest({ ...manifest, files: [manifest.files[0], manifest.files[0]] }))
  assert.equal(knowledgeManifest(null), null)
})

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
    get(name) { return name === 'connection' ? { authenticatedUrl: url => url + '?token=test-browser-only' } : undefined },
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
    assert.equal((await http(port, 'invalid', '/open')).status, 401)
    assert.equal((await http(port, token, '/open')).body.url, 'http://127.0.0.1:3080/?token=test-browser-only')
    assert.ok(!(JSON.stringify((await http(port, token, '/health')).body)).includes('test-browser-only'))
    assert.equal((await http(port, token, '/task', 'POST', { prompt: 'x', knowledge: { root: 'relative' } })).status, 400)
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

test('web agents mount standard preset before receiving knowledge references', async () => {
  const root = mkdtempSync(join(tmpdir(), 'elyms-preset-test-'))
  const original = process.env.LOCALAPPDATA
  process.env.LOCALAPPDATA = root
  let dispose, mounted = false, delivered = ''
  const scope = {}
  const presets = { async resolve(id) { assert.equal(id, 'standard'); return { id } }, async mount(ctx, id) { assert.equal(ctx, scope); assert.equal(id, 'standard'); mounted = true } }
  const agent = { status: 'running', cancel() {}, followup(message) { assert.ok(mounted); delivered = message.content[0].text }, whenIdle() { return Promise.resolve() } }
  const ctx = {
    get(name) { return name === 'agentPresets' ? presets : undefined },
    logger: () => ({ info() {}, warn() {} }),
    agents: { async create(options) { assert.equal(options.meta.agentPreset, 'standard'); await options.setup(scope); return { agent } }, get() { return agent } },
    commands: { execute() { return { kind: 'success' } } },
    sessions: { get() { return { deriveMessages() { return [{ role: 'assistant', content: [{ type: 'text', text: 'verified' }] }] } } } },
    effect(callback) { dispose = callback() },
  }
  try {
    apply(ctx, { workspace: root })
    const h = await until(() => { try { return JSON.parse(readFileSync(join(root, 'ElymsDesktopPet', 'dsh-bridge.json'), 'utf8')) } catch { return null } })
    const manifest = { archive: '测试', root, files: [{ label: '图谱', path: join(root, 'graph.json') }] }
    const result = await http(h.port, h.token, '/task', 'POST', { prompt: '检查', knowledge: manifest })
    assert.equal(result.status, 202)
    await until(() => delivered)
    assert.ok(delivered.includes(JSON.stringify(manifest, null, 2)))
    await until(async () => (await http(h.port, h.token, '/health')).body.active === 0)
  } finally {
    dispose?.()
    if (original === undefined) delete process.env.LOCALAPPDATA
    else process.env.LOCALAPPDATA = original
    rmSync(root, { recursive: true, force: true })
  }
})

