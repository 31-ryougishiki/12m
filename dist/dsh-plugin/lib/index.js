/**
 * dsh-elyms-pet — host-side DSH plugin that launches the Elyms desktop pet.
 *
 * It is deliberately small and dependency-free: a DSH profile loads this
 * module in the host process, and after a short delay it starts
 * `dist/ElymsPet.exe`. The pet is a normal Windows GUI process; this plugin
 * does not render anything into the Web GUI and does not touch the DSH source
 * tree.
 *
 * Launching a second copy is the existing pet's supported "recall" path:
 * ElymsPet.exe holds a single-instance mutex, signals
 * `Local\\ElymsDesktopPet.Recall`, and exits, which shows the existing pet,
 * brings it back on screen, and clears mouse passthrough. So every DSH start
 * also recovers a hidden pet.
 *
 * @module dsh-elyms-pet
 */

import { spawn } from 'node:child_process'
import { existsSync } from 'node:fs'
import { dirname, isAbsolute, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

/** Stable Cordis plugin name; matches the id in cordis.patch.yml. */
export const name = 'elyms-pet'

/** No DSH services are required; this plugin only owns a child process. */
export const inject = []

const PACKAGE_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const DEFAULT_EXE = resolve(PACKAGE_ROOT, '..', 'dist', 'ElymsPet.exe')

const DEFAULTS = Object.freeze({
  enabled: true,
  exePath: '',
  args: [],
  startDelayMs: 1200,
  leaveRunning: true,
})

/**
 * Coerce a loader configuration object into the plugin's normalized settings.
 * @param {unknown} config
 */
function normalizeConfig(config) {
  const input = config && typeof config === 'object' ? config : {}
  const delay = Number(input.startDelayMs)
  return {
    enabled: input.enabled !== false && input.enabled !== 'false',
    exePath: typeof input.exePath === 'string' ? input.exePath.trim() : '',
    args: Array.isArray(input.args) ? input.args.map(String) : [],
    startDelayMs: Number.isFinite(delay) ? Math.max(0, Math.min(60000, delay)) : DEFAULTS.startDelayMs,
    leaveRunning: input.leaveRunning !== false && input.leaveRunning !== 'false',
  }
}

/**
 * Find the pet executable. Explicit config wins, then ELYMS_PET_EXE, then the
 * checkout layout (plugin/lib -> plugin -> workspace/dist).
 * @param {unknown} config
 * @returns {string} an existing absolute path, or ''
 */
export function resolveExe(config = {}) {
  const settings = normalizeConfig(config)
  const candidates = []
  if (settings.exePath) {
    candidates.push(isAbsolute(settings.exePath) ? settings.exePath : resolve(PACKAGE_ROOT, settings.exePath))
  }
  if (process.env.ELYMS_PET_EXE) candidates.push(process.env.ELYMS_PET_EXE)
  candidates.push(DEFAULT_EXE)
  candidates.push(resolve(PACKAGE_ROOT, '..', '..', 'dist', 'ElymsPet.exe'))
  for (const candidate of candidates) {
    if (typeof candidate === 'string' && candidate.length > 0 && existsSync(candidate)) return candidate
  }
  return ''
}

function loggerFor(ctx) {
  try {
    if (ctx && typeof ctx.logger === 'function') return ctx.logger('elyms-pet')
    if (ctx && ctx.logger && typeof ctx.logger.info === 'function') return ctx.logger
  } catch {}
  return console
}

/**
 * Start the pet once. Exported so the launcher can be tested without a DSH
 * host; `apply` is the normal entry point.
 * @param {unknown} config
 * @param {{ info?: Function, warn?: Function }} logger
 */
export function launchPet(config = {}, logger = console) {
  const settings = normalizeConfig(config)
  if (!settings.enabled) return null
  const exePath = resolveExe(settings)
  if (!exePath) {
    try {
      logger.warn?.(`[elyms-pet] ElymsPet.exe not found; set exePath or ELYMS_PET_EXE. Checked ${DEFAULT_EXE}`)
    } catch {}
    return null
  }

  try {
    const child = spawn(exePath, settings.args, {
      cwd: dirname(exePath),
      detached: true,
      stdio: 'ignore',
      windowsHide: false,
    })
    child.once('error', (error) => {
      try {
        logger.warn?.(`[elyms-pet] failed to start ${exePath}: ${error.message}`)
      } catch {}
    })
    child.once('exit', (code, signal) => {
      if (code === 0) {
        try {
          logger.info?.('[elyms-pet] existing pet instance recalled')
        } catch {}
      } else if (code !== null && code !== undefined) {
        try {
          logger.warn?.(`[elyms-pet] pet exited with code ${code}${signal ? ` (${signal})` : ''}`)
        } catch {}
      }
    })
    child.unref()
    try {
      logger.info?.(`[elyms-pet] launched ${exePath}`)
    } catch {}
    return child
  } catch (error) {
    try {
      logger.warn?.(`[elyms-pet] spawn failed: ${error.message}`)
    } catch {}
    return null
  }
}

/**
 * Cordis plugin entry. Registers one effect that starts the pet after
 * `startDelayMs` and cleans up the timer (and optionally the pet process) when
 * the profile unloads.
 * @param {any} ctx
 * @param {unknown} config
 */
export function apply(ctx, config = {}) {
  const settings = normalizeConfig(config)
  if (!settings.enabled) return
  const logger = loggerFor(ctx)
  let child = null
  let timer = null
  let disposed = false

  const start = () => {
    timer = null
    if (disposed) return
    child = launchPet(settings, logger)
  }

  const dispose = () => {
    disposed = true
    if (timer) {
      clearTimeout(timer)
      timer = null
    }
    if (!child) return
    if (!settings.leaveRunning && child.pid) {
      try {
        process.kill(child.pid)
      } catch {}
    }
    child = null
  }

  if (ctx && typeof ctx.effect === 'function') {
    ctx.effect(() => {
      if (settings.startDelayMs > 0) {
        timer = setTimeout(start, settings.startDelayMs)
        if (typeof timer.unref === 'function') timer.unref()
      } else {
        start()
      }
      return dispose
    }, 'elyms-pet: launch')
  } else {
    // Standalone test / non-Cordis host: start now and expose a disposer.
    start()
    return dispose
  }
}
