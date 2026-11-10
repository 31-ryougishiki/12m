using System;
using System.IO;
using System.Threading;
using System.Windows.Threading;

namespace ElymsPet {
 public interface IClock {
  DateTime Now { get; }
  DateTime UtcNow { get; }
 }
 public sealed class SystemClock : IClock {
  public DateTime Now { get { return DateTime.Now; } }
  public DateTime UtcNow { get { return DateTime.UtcNow; } }
 }

 public interface IRepository<T> where T : class {
  T Load();
  bool Save(T value);
  string LastError { get; }
 }
 public sealed class JsonRepository<T> : IRepository<T> where T : class {
  readonly string path;
  string error = "";
  public JsonRepository(string path) { this.path = Path.GetFullPath(path); }
  public string LastError { get { return error; } }
  public T Load() { return JsonFile.Load<T>(path, out error); }
  public bool Save(T value) { return JsonFile.Save(path, value, Path.GetDirectoryName(path), out error); }
 }
 public sealed class TransientRepository<T> : IRepository<T> where T : class {
  T value;
  public TransientRepository(T initial) { value = initial; }
  public string LastError { get { return ""; } }
  public T Load() { return value; }
  public bool Save(T updated) { value = updated; return true; }
 }

 public interface IWorkScheduler {
  void Queue(Action work);
  void Post(Action completion);
 }
 public sealed class DispatcherWorkScheduler : IWorkScheduler {
  readonly Dispatcher dispatcher;
  public DispatcherWorkScheduler(Dispatcher dispatcher) { this.dispatcher = dispatcher; }
  public void Queue(Action work) { ThreadPool.QueueUserWorkItem(delegate { work(); }); }
  public void Post(Action completion) {
   if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
    dispatcher.BeginInvoke(completion);
  }
 }

 /// <summary>每个服务或面板拥有自己的任务作用域。取消使排队工作和迟到回调失效；已开始的 HTTP 请求由原超时结束。</summary>
 public sealed class WorkScope : IDisposable {
  readonly IWorkScheduler scheduler;
  readonly Action<string> failed;
  readonly bool enabled;
  int generation;
  volatile bool disposed;
  public WorkScope(IWorkScheduler scheduler, Action<string> failed, bool enabled) {
   this.scheduler = scheduler; this.failed = failed; this.enabled = enabled;
  }
  public bool IsDisposed { get { return disposed; } }
  public void Run<T>(Func<T> work, Action<T> done, Action<string> onFailure = null) {
   Action<string> failure = onFailure ?? failed;
   if (disposed) return;
   if (!enabled) { failure("预览模式不执行在线任务。"); return; }
   int ticket = generation;
   try {
    scheduler.Queue(delegate {
     if (!Current(ticket)) return;
     T result = default(T);
     Exception error = null;
     try { result = work(); } catch (Exception e) { error = e; }
     if (!Current(ticket)) return;
     try {
      scheduler.Post(delegate {
       if (!Current(ticket)) return;
       if (error != null) { failure(FailureMessage(error)); return; }
       try { done(result); } catch (Exception e) { failure(FailureMessage(e)); }
      });
     } catch (InvalidOperationException) { /* Dispatcher 正在关闭。 */ }
    });
   } catch (Exception e) { if (Current(ticket)) failure(FailureMessage(e)); }
  }
  bool Current(int ticket) { return !disposed && ticket == Volatile.Read(ref generation); }
  static string FailureMessage(Exception error) {
   // Exception.Message 可能包含请求地址、密钥或正文，不直接送进界面。
   return "后台任务未完成（" + error.GetType().Name + "），请稍后重试。";
  }
  public void CancelPending() { Interlocked.Increment(ref generation); }
  public void Dispose() { disposed = true; CancelPending(); }
 }

 /// <summary>服务能够请求的桌宠呈现动作；不暴露窗口、Dispatcher 或其它服务。</summary>
 public sealed class PetFeedback {
  public readonly Action<int, string, int> Act;
  public readonly Action<string, int> Say;
  public readonly Action Wake;
  public PetFeedback(Action<int, string, int> act, Action<string, int> say, Action wake) {
   Act = act; Say = say; Wake = wake;
  }
 }
}
