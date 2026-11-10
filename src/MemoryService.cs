using System;

namespace ElymsPet {
 public sealed class MemoryMutation {
  public int Added, Updated, Rejected;
  public bool Changed { get { return Added > 0 || Updated > 0; } }
 }
 /// <summary>桌宠这一侧的记忆：读盘、写入、检索和面板操作。</summary>
 public sealed class MemoryService {
  readonly Func<bool> enabled;
  readonly IRepository<MemoryBook> repository;
  readonly IClock clock;
  MemoryBook book;
  public int Revision { get; private set; }
  public string LastError = "";
  public event Action Changed;
  public MemoryService(Func<bool> enabled, IRepository<MemoryBook> repository, IClock clock) {
   this.enabled = enabled; this.repository = repository; this.clock = clock;
   book = MemoryStore.Prepare(repository.Load());
   LastError = repository.LastError;
  }
  public MemoryBook Book { get { return book; } }
  public MemoryItem[] Items { get { return MemoryStore.Sorted(book); } }
  public int Count { get { return book.Items == null ? 0 : book.Items.Length; } }
  /// <summary>发给模型的记忆上下文；以「记忆可用」开头时，提示词里才会附上记忆规则。</summary>
  public string Recall(string query) {
   return MemoryStore.Recall(book, query, enabled(), clock.UtcNow);
  }
  public int Remember(MemoryItem[] items) {
   if (!enabled() || items == null || items.Length == 0) return 0;
   MemoryMutation change = MemoryStore.Apply(book, items, clock.UtcNow);
   if (change.Changed) { Save(); Touch(); }
   return change.Added;
  }
  public MemoryItem[] Observe(ref string reply) {
   MemoryItem[] items = MemoryMarkers.Extract(ref reply);
   if (items.Length > 0) Remember(items);
   return items;
  }
  public bool Confirm(string id) {
   if (!MemoryStore.Confirm(book, id)) return false;
   Save(); Touch(); return true;
  }
  public bool Forget(string id) {
   if (!MemoryStore.Forget(book, id)) return false;
   Save(); Touch(); return true;
  }
  public void Clear() {
   MemoryStore.Clear(book);
   Save(); Touch();
  }
  public void Save() {
   book.Updated=clock.UtcNow;
   repository.Save(book);
   LastError = repository.LastError;
  }
  void Touch() { Revision++; if (Changed != null) Changed(); }
 }
}
