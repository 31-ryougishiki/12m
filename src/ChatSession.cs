using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

#pragma warning disable 0649
namespace ElymsPet {
 public sealed class ChatSession {
  public const int MaxContextTokens=1000000;
  readonly List<ChatTurn> turns = new List<ChatTurn>();
  public int Count {get {return turns.Count;}}
  public ChatTurn[] History {get {return turns.ToArray();}}
  public int ContextTokens {get;private set;}
  public int ContextLimit {get {return MaxContextTokens;}}
  readonly IRepository<ChatStore> repository;
  public string LastError { get; private set; }
  public ChatSession() : this(new JsonRepository<ChatStore>(HistoryPath)) { }
  public ChatSession(IRepository<ChatStore> repository) { this.repository = repository; LastError = ""; }
  public static string HistoryPath {get {return Path.Combine(Storage.Folder,"chat-history.json");}}
  public void Clear() {turns.Clear();ContextTokens=0;Save();}
  public ChatTurn[] Build(PetState state,string user) {return Build(state,user,"","","");}
  public ChatTurn[] Build(PetState state,string user,string profileDigest) {return Build(state,user,profileDigest,"","");}
  public ChatTurn[] Build(PetState state,string user,string profileDigest,string dshContext) {return Build(state,user,profileDigest,dshContext,"");}
  public ChatTurn[] Build(PetState state,string user,string profileDigest,string dshContext,string webContext) {
   return Build(new ConversationRequest(state,user,profileDigest,dshContext,webContext,"","",DateTime.Now));
  }
  public ChatTurn[] Build(ConversationRequest request) {
   int tokens;
   ChatTurn[] result = ConversationContext.Build(History,request,MaxContextTokens-8192,out tokens);
   ContextTokens = tokens;
   return result;
  }
  public void Remember(string user,string reply) {
   turns.Add(new ChatTurn("user",Cap(user,4000)));
   turns.Add(new ChatTurn("assistant",Cap(reply,4000)));
   TrimHistory();
   Save();
  }
  public void RememberAssistant(string text) {
   text=Cap(text,4000);
   if(text.Length==0)return;
   turns.Add(new ChatTurn("assistant",text));
   TrimHistory();
   Save();
  }
  public ChatTurn[] Recent(int count) {
   int start=Math.Max(0,turns.Count-Math.Max(0,count));
   var list=new List<ChatTurn>();
   for(int i=start;i<turns.Count;i++)list.Add(turns[i]);
   return list.ToArray();
  }
  public void Load() {
   turns.Clear();ContextTokens=0;
   ChatStore store=repository.Load();
   LastError=repository.LastError;
   if(store==null||store.turns==null)return;
   for(int i=0;i<store.turns.Length;i++) {
    ChatStoredTurn item=store.turns[i];
    if(item==null||string.IsNullOrEmpty(item.content))continue;
    string role=item.role=="assistant"?"assistant":"user";
    turns.Add(new ChatTurn(role,item.content));
   }
   TrimHistory();
  }
  public void Save() {
   var store=new ChatStore {turns=new ChatStoredTurn[turns.Count]};
   for(int i=0;i<turns.Count;i++) store.turns[i]=new ChatStoredTurn {role=turns[i].Role,content=turns[i].Content};
   repository.Save(store);
   LastError=repository.LastError;
  }
  void TrimHistory() {
   int budget=MaxContextTokens-16384;
   int total=EstimateList(turns);
   while(total>budget&&turns.Count>0) {total-=EstimateTurn(turns[0]);turns.RemoveAt(0);}
   ContextTokens=total;
  }
  static int EstimateList(List<ChatTurn> list) {
   int total=0;
   for(int i=0;i<list.Count;i++) total+=EstimateTurn(list[i]);
   return total;
  }
  static int EstimateTurn(ChatTurn turn) {
   return EstimateTokens(turn.Role)+EstimateTokens(turn.Content)+4;
  }
  public static int EstimateTokens(string text) {
   if(string.IsNullOrEmpty(text)) return 0;
   int cjk=0,other=0;
   for(int i=0;i<text.Length;i++) {
    char c=text[i];
    if(c>=0x2E80&&c<=0x9FFF) cjk++;
    else other++;
   }
   return cjk+(other+3)/4;
  }
  static string Cap(string text,int max) {
   text=(text??"").Trim();
   return text.Length<=max?text:text.Substring(0,max)+"…";
  }
 }
}
