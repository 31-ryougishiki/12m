using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace ElymsPet {
 /// <summary>本机 JSON 存档的原子读写：先写 .tmp，再替换主文件，旧内容留作 .bak。</summary>
 /// <remarks>设置文件与存档单元共用同一套写入/回退规则，避免两处各写一遍。</remarks>
 internal static class JsonFile {
  /// <summary>按「主文件 → .bak」顺序读取；都失败时返回 null，并给出最后一个错误。</summary>
  public static T Load<T>(string path,out string error) where T:class {
   error="";
   string[] paths=new string[]{path,path+".bak"};
   for(int i=0;i<paths.Length;i++) {
    if(!File.Exists(paths[i])) continue;
    try {
     using(FileStream stream=File.OpenRead(paths[i])) {
      var value=(T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
      if(value!=null) {error="";return value;}
     }
    } catch(Exception e) {error=e.Message;}
   }
   return null;
  }
  /// <summary>原子写入；失败时保留原文件，返回 false 并给出错误。</summary>
  public static bool Save<T>(string path,T value,string folder,out string error) {
   error="";
   try {
    Directory.CreateDirectory(folder);
    string temp=path+".tmp";
    using(FileStream stream=File.Create(temp)) new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);
    if(File.Exists(path)) File.Replace(temp,path,path+".bak"); else File.Move(temp,path);
    return true;
   } catch(Exception e) {error=e.Message;return false;}
  }
 }
}
