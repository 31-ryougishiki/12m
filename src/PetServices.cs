using System;
using System.IO;

namespace ElymsPet {
 public static class AutonomyPolicy {
  public static bool CanExecute(PetState state, bool chatting, bool browsing, bool working, bool disposed) {
   return !disposed && state.AutoLife && !state.Sleeping && state.FocusEnd == DateTime.MinValue
    && state.PausedSeconds <= 0 && !chatting && !browsing && !working;
  }
 }

 /// <summary>只负责组装和释放依赖。业务服务不持有此对象，不把它当作服务定位器。</summary>
 public sealed class PetServices : IDisposable {
  public readonly DshWorkbench Dsh;
  public readonly BiliService Bili;
  public readonly MemoryService Memory;
  public readonly ArchiveService Archive;
  public readonly ChatService Conversation;
  public readonly AutonomyService Autonomy;
  public PetServices(PetState state, IWorkScheduler scheduler, PetFeedback feedback,
   Func<string, bool> confirmDsh, string folder, bool preview, IClock clock,Func<AutonomyRequest,AutonomyPlan> planner=null) {
   state.Plugins = new PluginManager(folder,AppDomain.CurrentDomain.BaseDirectory,preview);
   Memory = new MemoryService(delegate { return state.MemoryEnabled; },
    Repository<MemoryBook>(folder,"memories.json",preview,MemoryStore.Demo),clock);
   Archive = new ArchiveService(state,scheduler,feedback,
    Repository<ArchiveBook>(folder,Path.Combine("archives","archives.json"),preview,ArchiveStore.Demo),preview);
   Func<string> digest = delegate { return PluginAccess.Enabled(state,"archive")?Archive.Digest:""; };
   Dsh = new DshWorkbench(state,scheduler,preview,Archive.PrepareDshKnowledge);
   Bili = new BiliService(state,scheduler,digest,preview);
   Conversation = new ChatService(state,scheduler,Dsh,Bili,Memory,
    new ChatSession(Repository<ChatStore>(folder,"chat-history.json",preview,delegate {return new ChatStore();})),
    digest,feedback,confirmDsh,clock,preview,delegate(string query){return PluginAccess.Enabled(state,"archive")?Archive.Recall(query):"";});
   Conversation.Graphs=PluginAccess.Enabled(state,"graph")?Archive:null;
   Autonomy = new AutonomyService(state,scheduler,Bili,Dsh,Conversation,digest,
    Repository<AutonomyJournal>(folder,"autonomy-journal.json",preview,delegate {return new AutonomyJournal();}),
    clock,new Random(),preview,planner);
  }
  static IRepository<T> Repository<T>(string folder,string name,bool preview,Func<T> demo) where T:class {
   if(preview)return new TransientRepository<T>(demo());
   return new JsonRepository<T>(Path.Combine(folder,name));
  }
  public void Dispose() {
   Autonomy.Dispose(); Conversation.Dispose(); Archive.Dispose(); Bili.Dispose(); Dsh.Dispose();
  }
 }
}
