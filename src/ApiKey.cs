using System;
using System.IO;

namespace ElymsPet {
 /// <summary>API Key 的清洗、校验、掩码与来源解析；界面只通过这里判断密钥状态。</summary>
 public static class ApiKey {
  /// <summary>一次解析的结果：密钥本体、来源说明（用于界面显示）、是否可用。</summary>
  public struct State {
   public string Key;
   public string Origin;
   public bool Configured {get {return Key.Length>0;}}
  }
  public static string Clean(string key) {
   string value=(key??"").Trim();
   if(value.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)) value=value.Substring(7).Trim();
   return value.Trim('"').Trim('\'');
  }
  public static bool Valid(string key) {
   string value=Clean(key);
   return value.Length>=8&&value.IndexOf('\r')<0&&value.IndexOf('\n')<0;
  }
  public static string Mask(string key) {
   string value=Clean(key);
   if(value.Length==0) return "未配置";
   if(value.Length<12) return "已配置";
   return value.Substring(0,3)+"…"+value.Substring(value.Length-4);
  }
  /// <summary>按「设置文件 → 环境变量 → 数据文件夹 api-key.txt → EXE 同目录 api-key.txt」查找。</summary>
  public static State Resolve(PetState state) {
   var result=new State();
   string value=state==null?"":state.ApiKey;
   if(Valid(value)) {result.Key=Clean(value);result.Origin="设置文件";return result;}
   value=Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
   if(Valid(value)) {result.Key=Clean(value);result.Origin="环境变量 DEEPSEEK_API_KEY";return result;}
   string[] folders=new string[]{Storage.Folder,AppDomain.CurrentDomain.BaseDirectory};
   for(int i=0;i<folders.Length;i++) {
    value=ReadFile(Path.Combine(folders[i],"api-key.txt"));
    if(Valid(value)) {result.Key=Clean(value);result.Origin="本地 api-key.txt";return result;}
   }
   result.Key="";result.Origin="";
   return result;
  }
  static string ReadFile(string path) {
   try {
    if(!File.Exists(path)) return "";
    string[] lines=File.ReadAllLines(path);
    for(int i=0;i<lines.Length;i++) {
     string line=(lines[i]??"").Trim();
     if(line.Length==0||line.StartsWith("#")) continue;
     int equals=line.IndexOf('=');
     if(equals>=0) line=line.Substring(equals+1);
     line=Clean(line);
     if(Valid(line)) return line;
    }
   } catch {}
   return "";
  }
 }
}
