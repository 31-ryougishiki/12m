/**
 * dsh-elyms-bridge — loopback workbench bridge for the Elyms desktop pet.
 *
 * The desktop pet is a .NET WPF process; this host plugin gives it a small
 * authenticated HTTP API on 127.0.0.1. A request creates a real DSH session,
 * prompts it through the session-controller gateway, waits for the agent to
 * become idle, and returns the final assistant text plus a tool count.
 *
 * The plugin never opens a window and never drives the desktop. The wrapped
 * task prompt explicitly tells the DSH agent not to open GUI applications or
 * control the mouse/keyboard. The pet-side confirmation is the human gate for
 * write access.
 *
 * Handshake file: %LOCALAPPDATA%/ElymsDesktopPet/dsh-bridge.json
 *   { version, host, port, token, pid, startedAt, workspace, webUrl }
 *
 * @module dsh-elyms-bridge
 */

import { JobRegistry } from './job-registry.js'
import { randomBytes, randomUUID, timingSafeEqual } from 'node:crypto'
import { createServer } from 'node:http'
import { existsSync, mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { homedir } from 'node:os'
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path'

/** Stable Cordis plugin name. */
export const name = 'elyms-bridge'

/** Services needed: agents for creation/driving, sessions for results, commands for permission. */
export const inject = ['agents', 'sessions', 'commands']

const BRIDGE_VERSION = 1
const MAX_BODY_BYTES = 64 * 1024
const MAX_PROMPT_CHARS = 8000
const PERMISSIONS = new Set(['read-only', 'workspace-write', 'danger-full-access'])

const DEFAULTS = Object.freeze({
  enabled: true,
  host: '127.0.0.1',
  port: 0,
  workspace: process.cwd(),
  defaultPermission: 'workspace-write',
  provider: 'deepseek-official',
  model: 'deepseek-flash',
  reasoningEffort: 'high',
  maxActive: 1,
  maxJobs: 30,
  taskTimeoutMs: 30 * 60 * 1000,
  allowAnyCwd: false,
  webUrl: 'http://127.0.0.1:3080',
})

function normalizeConfig(config) {
  const input = config && typeof config === 'object' ? config : {}
  const port = Number(input.port)
  const maxActive = Number(input.maxActive)
  const maxJobs = Number(input.maxJobs)
  const timeout = Number(input.taskTimeoutMs)
  const permission = PERMISSIONS.has(input.defaultPermission) ? input.defaultPermission : DEFAULTS.defaultPermission
  return {
    enabled: input.enabled !== false && input.enabled !== 'false',
    host: typeof input.host === 'string' && input.host.trim() !== '' ? input.host.trim() : DEFAULTS.host,
    port: Number.isInteger(port) && port >= 0 && port <= 65535 ? port : DEFAULTS.port,
    workspace: resolve(typeof input.workspace === 'string' && input.workspace.trim() !== '' ? input.workspace.trim() : DEFAULTS.workspace),
    defaultPermission: permission,
    provider: typeof input.provider === 'string' && input.provider.trim() !== '' ? input.provider.trim() : DEFAULTS.provider,
    model: typeof input.model === 'string' && input.model.trim() !== '' ? input.model.trim() : DEFAULTS.model,
    reasoningEffort: typeof input.reasoningEffort === 'string' && input.reasoningEffort.trim() !== '' ? input.reasoningEffort.trim() : DEFAULTS.reasoningEffort,
    maxActive: Number.isFinite(maxActive) ? Math.max(1, Math.min(4, Math.trunc(maxActive))) : DEFAULTS.maxActive,
    maxJobs: Number.isFinite(maxJobs) ? Math.max(5, Math.min(100, Math.trunc(maxJobs))) : DEFAULTS.maxJobs,
    taskTimeoutMs: Number.isFinite(timeout) ? Math.max(30_000, Math.min(2 * 60 * 60 * 1000, Math.trunc(timeout))) : DEFAULTS.taskTimeoutMs,
    allowAnyCwd: input.allowAnyCwd === true || input.allowAnyCwd === 'true',
    webUrl: typeof input.webUrl === 'string' && input.webUrl.trim() !== '' ? input.webUrl.trim() : DEFAULTS.webUrl,
  }
}

function loggerFor(ctx) {
  try {
    if (ctx && typeof ctx.logger === 'function') return ctx.logger('elyms-bridge')
    if (ctx && ctx.logger && typeof ctx.logger.info === 'function') return ctx.logger
  } catch {}
  return console
}

function delay(ms) {
  return new Promise((resolveDelay) => setTimeout(resolveDelay, ms))
}

function handshakePath() {
  const base = process.env.LOCALAPPDATA || join(homedir(), 'AppData', 'Local')
  return join(base, 'ElymsDesktopPet', 'dsh-bridge.json')
}

function sendJson(res, status, value) {
  if (res.writableEnded) return
  const body = Buffer.from(JSON.stringify(value), 'utf8')
  res.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': body.length,
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
  })
  res.end(body)
}

function readJson(req, limit = MAX_BODY_BYTES) {
  return new Promise((resolveBody, rejectBody) => {
    let total = 0
    const chunks = []
    req.on('data', (chunk) => {
      total += chunk.length
      if (total > limit) {
        rejectBody(Object.assign(new Error('body too large'), { status: 413 }))
        req.destroy()
        return
      }
      chunks.push(chunk)
    })
    req.on('end', () => {
      if (total === 0) {
        resolveBody({})
        return
      }
      try {
        resolveBody(JSON.parse(Buffer.concat(chunks).toString('utf8')))
      } catch {
        rejectBody(Object.assign(new Error('invalid json'), { status: 400 }))
      }
    })
    req.on('error', rejectBody)
  })
}

function authorized(req, token) {
  const header = typeof req.headers.authorization === 'string' ? req.headers.authorization : ''
  const presented = header.startsWith('Bearer ') ? header.slice(7).trim() : ''
  if (presented.length !== token.length) return false
  try {
    return timingSafeEqual(Buffer.from(presented), Buffer.from(token))
  } catch {
    return false
  }
}

function insideWorkspace(workspace, cwd) {
  if (resolve(cwd) === resolve(workspace)) return true
  const rel = relative(workspace, cwd)
  return rel !== '' && !rel.startsWith('..') && !isAbsolute(rel) && !rel.startsWith(`..${sep}`)
}

function jobView(job) {
  return {
    id: job.id,
    title: job.title,
    status: job.status,
    task: job.task,
    cwd: job.cwd,
    permission: job.permission,
    sessionId: job.sessionId,
    text: job.text,
    progress: job.progress,
    error: job.error,
    note: job.note,
    tools: job.tools,
    seconds: job.seconds,
    createdAt: job.createdAt,
    startedAt: job.startedAt,
    endedAt: job.endedAt,
  }
}

function wrapTask(task, workspace) {
  return `你是由桌面宠物「曳尔沐」转交任务的 DSH 工作台执行者。
请直接完成下面的工作，不要反问、不要等待用户确认；缺少信息时按最合理的假设完成，并在结论里说明你的假设。
可以使用你的文件、命令、搜索等工具实际修改、创建、运行验证；当前工作区是 ${workspace}。
重要限制：
1. 不要操作桌面 GUI，不要打开可见窗口，不要控制鼠标或键盘，不要截图，不要使用浏览器工具打开页面。
2. 不要读取、输出或传播 API Key、密码、令牌等秘密。
3. 不要运行会破坏系统或删除工作区之外文件的操作。
完成后用简洁中文总结：改了什么、如何验证、还有什么没做或需要用户决定。

任务：
${task}`
}

function assistantText(messages) {
  for (let i = messages.length - 1; i >= 0; i -= 1) {
    const message = messages[i]
    if (message === null || typeof message !== 'object' || message.role !== 'assistant') continue
    const content = Array.isArray(message.content) ? message.content : []
    const text = content
      .filter((block) => block && block.type === 'text' && typeof block.text === 'string')
      .map((block) => block.text)
      .join('\n')
      .trim()
    if (text !== '') return text
  }
  return ''
}

function toolCount(messages) {
  let count = 0
  for (const message of messages) {
    if (message && typeof message === 'object' && message.role === 'tool') count += 1
  }
  return count
}

function readJobProgress(ctx, job) {
  try {
    const session = ctx.sessions.get(job.sessionId)
    if (session === undefined) return
    const messages = session.deriveMessages()
    const text = assistantText(messages)
    if (text !== '') job.progress = text.length > 3000 ? text.slice(-3000) : text
    job.tools = toolCount(messages)
  } catch {}
}

async function setPermission(ctx, agent, permission) {
  if (!permission) return
  const outcome = await ctx.commands.execute(agent, '/permission ' + permission, [], AbortSignal.timeout(30_000))
  const result = outcome && typeof outcome === 'object' && outcome.result !== undefined ? outcome.result : outcome
  if (result && typeof result === 'object' && typeof result.kind === 'string' && result.kind !== 'success') {
    throw new Error('permission command failed: ' + (result.text || result.kind))
  }
}

/**
 * Run one job. Kept outside the HTTP handler so the response can return at
 * once and the pet can poll progress.
 */
async function runJob(ctx, logger, job, settings) {
  const started = Date.now()
  job.startedAt = new Date().toISOString()
  if (job.cancelled) {
    job.status = 'cancelled'
    job.error = '已取消'
    job.endedAt = job.startedAt
    return
  }
  job.status = 'running'
  let agent
  try {
    const sessionId = randomUUID()
    job.sessionId = sessionId
    const handle = await ctx.agents.create({
      sessionId,
      meta: { cwd: job.cwd },
      agentOptions: {
        provider: settings.provider,
        model: settings.model,
        ...(settings.reasoningEffort === '' ? {} : { reasoningEffort: settings.reasoningEffort }),
      },
    })
    job.handle = handle
    agent = handle.agent
    // Creation/permission calls may finish after a cancellation or plugin unload.
    if (job.cancelled) { agent.cancel({ kind: 'user' }); job.status = 'cancelled'; return }
    await setPermission(ctx, agent, job.permission)
    if (job.cancelled) { agent.cancel({ kind: 'user' }); job.status = 'cancelled'; return }
    readJobProgress(ctx, job)
    agent.followup({
      id: randomUUID(),
      role: 'user',
      content: [{ type: 'text', text: wrapTask(job.task, job.cwd) }],
      source: { kind: 'user' },
    })

    // followup wakes the driver; give it a moment so whenIdle() does not
    // observe the pre-prompt idle state.
    const wakeDeadline = Date.now() + 6000
    let woke = false
    while (Date.now() < wakeDeadline) {
      if (agent.status === 'running') {
        woke = true
        break
      }
      readJobProgress(ctx, job)
      if (job.progress !== '') {
        woke = true
        break
      }
      await delay(150)
    }
    if (!woke) {
      // Some sessions accept the prompt without flipping status before the
      // first step; one short grace period avoids a premature idle read.
      await delay(1500)
      readJobProgress(ctx, job)
    }

    let idle = false
    let idleError
    agent.whenIdle().then(
      () => {
        idle = true
      },
      (error) => {
        idleError = error
        idle = true
      },
    )

    while (!idle) {
      if (job.cancelled) break
      if (Date.now() - started > settings.taskTimeoutMs) {
        try {
          agent.cancel({ kind: 'timeout' })
        } catch {}
        throw new Error('任务超时，已请求停止')
      }
      await delay(1000)
      readJobProgress(ctx, job)
    }
    if (idleError !== undefined) throw idleError
    if (job.cancelled) {
      try {
        agent.cancel({ kind: 'user' })
      } catch {}
      job.status = 'cancelled'
      job.error = '已取消'
      return
    }

    const session = ctx.sessions.get(job.sessionId)
    if (session !== undefined) {
      readJobProgress(ctx, job)
      const messages = session.deriveMessages()
      job.text = assistantText(messages)
      job.tools = toolCount(messages)
    }
    if (job.text === '') {
      job.note = job.tools > 0 ? '任务结束，但没有返回文字结论；请到 DSH 会话查看工具输出。' : 'DSH 结束了，但没有返回文字结论。'
    }
    job.status = 'done'
  } catch (error) {
    job.status = job.cancelled ? 'cancelled' : 'error'
    job.error = error instanceof Error ? error.message : String(error)
    if (agent !== undefined && job.status === 'error') {
      try {
        agent.cancel({ kind: 'error' })
      } catch {}
    }
    logger.warn?.(`[elyms-bridge] job ${job.id} failed: ${job.error}`)
  } finally {
    job.endedAt = new Date().toISOString()
    job.seconds = Math.round(((Date.now() - started) / 1000) * 10) / 10
  }
}

function writeHandshake(value) {
  const path = handshakePath()
  mkdirSync(dirname(path), { recursive: true })
  const temp = path + '.tmp-' + process.pid
  writeFileSync(temp, JSON.stringify(value, null, 2), { encoding: 'utf8', mode: 0o600 })
  try {
    renameSync(temp, path)
  } catch {
    try {
      rmSync(path, { force: true })
      renameSync(temp, path)
    } catch {}
  }
}

function removeHandshake(token) {
  try {
    const path = handshakePath()
    if (!existsSync(path)) return
    const current = JSON.parse(readFileSync(path, 'utf8'))
    if (current && current.token === token) rmSync(path, { force: true })
  } catch {}
}

/**
 * Mount the loopback bridge.
 * @param {any} ctx - Cordis host context with gateway, agents, sessions and commands.
 * @param {unknown} config - profile entry configuration.
 */
export function apply(ctx, config = {}) {
  const settings = normalizeConfig(config)
  if (!settings.enabled) return
  const logger = loggerFor(ctx)
  const token = randomBytes(24).toString('hex')
  const startedAt = new Date().toISOString()
  const registry = new JobRegistry(settings.maxActive, settings.maxJobs)
  const jobs = registry.jobs

  const snapshot = () => {
    const value = {
      version: BRIDGE_VERSION,
      host: settings.host,
      port: 0,
      token,
      pid: process.pid,
      startedAt,
      workspace: settings.workspace,
      webUrl: settings.webUrl,
      maxActive: settings.maxActive,
    }
    return value
  }

  const handle = async (req, res) => {
    try {
      if (!authorized(req, token)) {
        sendJson(res, 401, { error: 'unauthorized' })
        return
      }
      const url = new URL(req.url || '/', `http://${settings.host}`)
      const pathname = url.pathname

      if (req.method === 'GET' && pathname === '/health') {
        sendJson(res, 200, {
          ok: true,
          name: 'elyms-bridge',
          version: BRIDGE_VERSION,
          pid: process.pid,
          startedAt,
          workspace: settings.workspace,
          webUrl: settings.webUrl,
          active: registry.active,
          jobs: jobs.size,
        })
        return
      }

      if (req.method === 'GET' && pathname === '/tasks') {
        const views = [...jobs.values()].slice(-20).map(jobView)
        sendJson(res, 200, { tasks: views })
        return
      }

      if (req.method === 'POST' && pathname === '/task') {
        const body = await readJson(req)
        const task = typeof body.prompt === 'string' ? body.prompt.trim() : ''
        if (task === '') {
          sendJson(res, 400, { error: 'prompt is required' })
          return
        }
        if (task.length > MAX_PROMPT_CHARS) {
          sendJson(res, 400, { error: 'prompt is too long' })
          return
        }
        const permission = typeof body.permission === 'string' && PERMISSIONS.has(body.permission) ? body.permission : settings.defaultPermission
        const cwdRaw = typeof body.cwd === 'string' && body.cwd.trim() !== '' ? body.cwd.trim() : settings.workspace
        const cwd = isAbsolute(cwdRaw) ? resolve(cwdRaw) : resolve(settings.workspace, cwdRaw)
        if (!settings.allowAnyCwd && !insideWorkspace(settings.workspace, cwd)) {
          sendJson(res, 403, { error: 'cwd is outside the configured workspace' })
          return
        }
        try {
          if (!statSync(cwd).isDirectory()) throw new Error('not a directory')
        } catch {
          sendJson(res, 400, { error: 'cwd does not exist' })
          return
        }
        const title = typeof body.title === 'string' && body.title.trim() !== ''
          ? body.title.trim().slice(0, 80)
          : '曳尔沐 · ' + task.replace(/\s+/g, ' ').slice(0, 24)
        const job = {
          id: randomUUID(),
          title,
          task,
          cwd,
          permission,
          status: 'queued',
          sessionId: '',
          text: '',
          progress: '',
          error: '',
          note: '',
          tools: 0,
          seconds: 0,
          createdAt: new Date().toISOString(),
          startedAt: '',
          endedAt: '',
          cancelled: false,
        }
        // The body await above can interleave requests. Reserve only after validation,
        // in one synchronous operation, and reject bodies completing after shutdown.
        if (!registry.tryAdd(job)) {
          sendJson(res, registry.closed ? 503 : 409, {
            error: registry.closed ? 'shutting down' : 'busy',
            jobId: registry.busyJob?.id,
          })
          return
        }
        try {
          sendJson(res, 202, { jobId: job.id, sessionId: '', title, status: 'queued' })
        } catch (error) {
          job.status = 'error'
          registry.finish(job.id)
          throw error
        }
        runJob(ctx, logger, job, settings)
          .catch((error) => {
            job.status = 'error'
            job.error = error instanceof Error ? error.message : String(error)
          })
          .finally(() => registry.finish(job.id))
        return
      }

      const match = pathname.match(/^\/task\/([0-9a-fA-F-]+)(\/cancel)?$/)
      if (match !== null) {
        const job = jobs.get(match[1])
        if (job === undefined) {
          sendJson(res, 404, { error: 'job not found' })
          return
        }
        if (req.method === 'GET' && match[2] === undefined) {
          sendJson(res, 200, jobView(job))
          return
        }
        if (req.method === 'POST' && match[2] === '/cancel') {
          if (job.status === 'queued' || job.status === 'running') {
            job.cancelled = true
            const agent = job.sessionId === '' ? undefined : ctx.agents.get(job.sessionId)
            if (agent !== undefined) {
              try {
                agent.cancel({ kind: 'user' })
              } catch {}
            }
            sendJson(res, 200, { cancelled: true, jobId: job.id })
            return
          }
          sendJson(res, 200, { cancelled: false, status: job.status })
          return
        }
      }

      sendJson(res, 404, { error: 'not found' })
    } catch (error) {
      const status = error && Number.isInteger(error.status) ? error.status : 500
      sendJson(res, status, { error: error instanceof Error ? error.message : String(error) })
    }
  }

  ctx.effect(() => {
    const server = createServer((req, res) => {
      handle(req, res).catch((error) => {
        sendJson(res, 500, { error: error instanceof Error ? error.message : String(error) })
      })
    })
    server.on('error', (error) => {
      logger.warn?.(`[elyms-bridge] server error: ${error instanceof Error ? error.message : String(error)}`)
    })
    server.listen(settings.port, settings.host, () => {
      const address = server.address()
      const port = address !== null && typeof address === 'object' ? address.port : settings.port
      const value = snapshot()
      value.port = port
      writeHandshake(value)
      logger.info?.(`[elyms-bridge] listening on http://${settings.host}:${port}; workspace ${settings.workspace}`)
    })
    return () => {
      registry.close()
      try {
        server.close()
      } catch {}
      removeHandshake(token)
      for (const job of jobs.values()) {
        if (job.status === 'queued' || job.status === 'running') {
          job.cancelled = true
          if (job.sessionId !== '') {
            const agent = ctx.agents.get(job.sessionId)
            if (agent !== undefined) {
              try {
                agent.cancel({ kind: 'user' })
              } catch {}
            }
          }
          if (job.handle && typeof job.handle.dispose === 'function') {
            try {
              void job.handle.dispose().catch(() => {})
            } catch {}
          }
        }
      }
    }
  }, 'elyms-bridge: loopback API')
}

export { jobView, wrapTask, assistantText }
