/** Owns admission and retention. tryAdd must remain synchronous: no await between checking and reserving. */
export class JobRegistry {
  constructor(maxActive, maxJobs) {
    this.maxActive = maxActive
    this.maxJobs = maxJobs
    this.jobs = new Map()
    this.running = new Set()
    this.closed = false
  }

  get active() { return this.running.size }
  get busyJob() { return this.jobs.get(this.running.values().next().value) }

  tryAdd(job) {
    if (this.closed || this.active >= this.maxActive) return false
    this.jobs.set(job.id, job)
    this.running.add(job.id)
    this.prune()
    return true
  }

  finish(id) {
    this.running.delete(id)
    this.prune()
  }

  close() { this.closed = true }

  prune() {
    for (const id of this.jobs.keys()) {
      if (this.jobs.size <= this.maxJobs) break
      if (!this.running.has(id)) this.jobs.delete(id)
    }
  }
}
