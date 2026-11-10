using System;
using System.Runtime.Serialization;

namespace ElymsPet {
 /// <summary>一条长期记忆：她记住的关于对方的一件事。</summary>
 /// <remarks>记忆只存在本机 data 文件夹，格式是普通 JSON，可以直接打开查看或备份。</remarks>
 [DataContract] public sealed class MemoryItem {
  [DataMember] public string Id = "";
  [DataMember] public string Kind = "事实";
  [DataMember] public string Text = "";
  [DataMember] public string Entity = "";
  [DataMember] public string Emotion = "";
  [DataMember] public string Source = "明说";
  [DataMember] public double Importance = 0.5;
  [DataMember] public bool Core = false;
  [DataMember] public bool Pending = false;
  [DataMember] public int Hits = 1;
  [DataMember] public DateTime Created = DateTime.MinValue;
  [DataMember] public DateTime LastSeen = DateTime.MinValue;
  [DataMember] public DateTime? Due = null;
 }

 /// <summary>全部记忆、已忘记的指纹和被排除的内容。</summary>
 [DataContract] public sealed class MemoryBook {
  [DataMember] public int Schema = 1;
  [DataMember] public MemoryItem[] Items = new MemoryItem[0];
  [DataMember] public string[] Blocked = new string[0];
  [DataMember] public DateTime Updated = DateTime.MinValue;
 }

}
