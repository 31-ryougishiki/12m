using System;
using System.Collections.Generic;

namespace ElymsPet {
 // A short, reviewable decision summary, never the model's private reasoning.
 public sealed class ArchiveAssessment {
  public string Task,CanHelp,Missing,Impact,NextStep,Mode;
  public const string Schema="\n先评估、后选题：先生成 assessment 对象，再根据评估生成 reply；不能先想一个问题再为它补理由。assessment 对象：{\"task\":\"当前要协助完成的具体任务\",\"can_help\":\"根据已有证据可以提供的具体帮助及范围，信息不足可写尚不能判断\",\"missing\":\"当前最关键的信息缺口；足够做最小试做则可为空\",\"impact\":\"该缺口会影响哪项决策或结果；可试做时写试做的验证边界\",\"next_step\":\"获得信息后能推进的具体动作，或最小试做及验证方式\",\"mode\":\"ask|try\"}。这是面向用户的简短判断摘要，每项一两句，不输出推理过程。只有缺口会阻碍用户当前明确要求的交付物时才选 ask，reply 只问解决这个缺口的一个具体问题；try 表示信息足够做有边界的最小试做，reply 说明能做什么并询问是否先试做/核对，不自动执行，不再凭空制造问题。判断属于模型建议，不是用户能力证据或已完成验证。用户明确暂停时无需 assessment。\n";
  static string Read(Dictionary<string,object> map,string key,int max) {return ArchiveStore.Clip(ArchiveStore.Clean(MiniJson.GetString(map,key)),max);}
  public static ArchiveAssessment Parse(string json) {
   object root;if(!MiniJson.TryParse(json,out root))return null;
   var map=root as Dictionary<string,object>;object value;
   if(map==null||!map.TryGetValue("assessment",out value))return null;
   var fields=value as Dictionary<string,object>;if(fields==null)return null;
   var a=new ArchiveAssessment{Task=Read(fields,"task",160),CanHelp=Read(fields,"can_help",350),Missing=Read(fields,"missing",350),Impact=Read(fields,"impact",250),NextStep=Read(fields,"next_step",350),Mode=Read(fields,"mode",16)};
   if(a.Task.Length==0||a.CanHelp.Length==0||a.Impact.Length==0||a.NextStep.Length==0)return null;
   if(a.Mode!="ask"&&a.Mode!="try")return null;
   if(a.Mode=="ask"&&a.Missing.Length==0)return null;
   return a;
  }
  public string Display() {
   return "本轮判断（模型建议，尚未经实际任务验证）\n任务："+Task+"\n目前可协助："+CanHelp+"\n关键缺口："+(Missing.Length>0?Missing:"暂未发现阻碍最小试做的信息缺口")+"\n影响 / 验证边界："+Impact+"\n下一步："+NextStep+"\n建议："+(Mode=="try"?"先做最小试做并核对结果":"先补关键缺口");
  }
 }
}
