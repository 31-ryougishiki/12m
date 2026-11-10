using System;
using System.IO;
using System.Text;

namespace ElymsPet {
 /// <summary>所有角色生成入口共用的人设；独立资源便于剧情校订，不混入动态能力状态。</summary>
 public static class CharacterPersona {
  public const string Version = "2026-11-10.4";
  public static readonly string Prompt = Load();
  static string Load() {
   using(var stream=typeof(CharacterPersona).Assembly.GetManifestResourceStream("elyms-persona.txt")) {
    if(stream==null)throw new InvalidOperationException("Missing embedded character persona: elyms-persona.txt");
    using(var reader=new StreamReader(stream,Encoding.UTF8)) {
     string text=reader.ReadToEnd().Trim();
     if(text.Length==0)throw new InvalidOperationException("Character persona is empty.");
     return text+"\n";
    }
   }
  }
 }
}
