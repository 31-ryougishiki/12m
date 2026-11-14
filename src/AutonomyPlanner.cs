using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Threading;

#pragma warning disable 0649
namespace ElymsPet {
 /// <summary>自主时间的规划器：让模型自己选这次想做什么；失败时用本地轮换。</summary>
 public static class AutonomyPlanner {
  static readonly string[] FallbackTopics={"独立游戏 开发","冷门纪录片","天文 科普","城市散步","手作木工","旧电脑修复"};
  public static AutonomyPlan Plan(AutonomyRequest request) {
   bool hasKey=CanUseAi(request.State,request.Key);
   return Plan(request.Key,hasKey,request.State.ApiBase,request.State.ApiModel,request.State,
    request.Profile,request.Recent,request.Last,request.DshAllowed,request.DshContext,request.WebContext,request.Now);
  }
  /// <summary>在线 AI 关闭时，自主规划和 B站 都不能偷偷发起模型请求。</summary>
  public static bool CanUseAi(PetState state,string key) {
   return state!=null&&state.AiChat&&ApiKey.Valid(key);
  }
  public static AutonomyPlan Plan(string key,bool hasKey,string baseUrl,string model,PetState state,string digest,string recent,string last,bool dshAllowed,string dshContext,string webContext,DateTime? now=null) {
   if(hasKey&&(state==null||state.AiChat)) {
    try {
     var user=new StringBuilder();
     user.Append("现在是你的自主时间，").Append((now??DateTime.Now).ToString("yyyy年M月d日 HH:mm")).Append("。\n");
     user.Append("最近对话：\n").Append(string.IsNullOrEmpty(recent)?"（还没有）":recent).Append("\n");
     user.Append("上次自主活动：").Append(string.IsNullOrEmpty(last)?"（还没有）":last).Append("\n");
     user.Append("对方的存档画像：").Append(string.IsNullOrEmpty(digest)?"（还没有）":Clip(digest,500)).Append("\n");
     user.Append("你可以自己决定这段时间做什么。可选：\n");
     user.Append("chat：主动找对方说一句 20—80 字的话；\n");
     if(state==null||PluginAccess.Enabled(state,"bili"))user.Append("bili：自己去 B站 逛逛，topic 写 2—20 字搜索词（可以为空）；\n");
     user.Append("note：写一段 50—200 字的自己的观察 / 计划；\n");
     if(dshAllowed) user.Append("dsh：让 DSH 在 autonomy/ 目录做一个小实验，task 写清楚目标；\n");
     user.Append("rest：这次什么都不做，发一会儿呆。\n");
     user.Append("不要每次都选同一种；不要打扰对方睡觉或专注。只输出一个 JSON 对象：{\"type\":\"chat|bili|note|dsh|rest\",\"topic\":\"\",\"message\":\"\",\"task\":\"\",\"note\":\"\",\"reason\":\"\"}。不要输出 Markdown。");
     var turns=new ChatTurn[] {
      new ChatTurn("system",Persona.SystemPrompt(state,now??DateTime.Now,digest,dshContext,webContext)),
      new ChatTurn("user",user.ToString())
     };
     ChatOutcome outcome=DeepSeek.Complete(key,baseUrl,model,true,turns,45,700,0.8);
     if(outcome.Ok) {
      AutonomyPlan parsed=Parse(outcome.Text);
      if(parsed!=null) return Normalize(parsed,dshAllowed);
     }
    } catch {}
   }
   return LocalPlan(dshAllowed);
  }
  public static AutonomyPlan Parse(string text) {
   if(string.IsNullOrEmpty(text))return null;
   int start=text.IndexOf('{'),end=text.LastIndexOf('}');
   if(start<0||end<=start)return null;
   try {
    using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(text.Substring(start,end-start+1)))) {
     AutonomyPlanData data=(AutonomyPlanData)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AutonomyPlanData)).ReadObject(stream);
     if(data==null)return null;
     return new AutonomyPlan {Type=data.type??"rest",Topic=data.topic??"",Message=data.message??"",Task=data.task??"",Note=data.note??"",Reason=data.reason??""};
    }
   } catch {return null;}
  }
  static AutonomyPlan Normalize(AutonomyPlan plan,bool dshAllowed) {
   string type=(plan.Type??"rest").Trim().ToLowerInvariant();
   if(type!="chat"&&type!="bili"&&type!="note"&&type!="dsh"&&type!="rest")type="rest";
   if(type=="dsh"&&!dshAllowed)type="note";
   plan.Type=type;
   plan.Topic=Clip(plan.Topic,30);
   plan.Message=Clip(plan.Message,500);
   plan.Task=Clip(plan.Task,1200);
   plan.Note=Clip(plan.Note,1000);
   return plan;
  }
  static AutonomyPlan LocalPlan(bool dshAllowed) {
   var random=new Random();
   int roll=random.Next(100);
   if(roll<40) return new AutonomyPlan {Type="chat",Message="在忙吗？我刚刚在这边发了一会儿呆，突然想过来打个招呼。",Reason="本地轮换：聊天"};
   if(roll<70) return new AutonomyPlan {Type="bili",Topic=FallbackTopics[random.Next(FallbackTopics.Length)],Reason="本地轮换：逛 B站"};
   if(dshAllowed&&roll<78) return new AutonomyPlan {Type="dsh",Task="在 autonomy/ 目录下做一个小实验，先写一个 README 记录想法。",Reason="本地轮换：DSH 小实验"};
   return new AutonomyPlan {Type="note",Note="今天也在桌面上。把一点小事记下来，等下次再回头看。",Reason="本地轮换：笔记"};
  }
  static string Clip(string text,int max) {
   text=(text??"").Trim();return text.Length<=max?text:text.Substring(0,max)+"…";
  }
 }
}
