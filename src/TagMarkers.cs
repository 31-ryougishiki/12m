using System;

namespace ElymsPet {
 /// <summary>回复里工具标记的容错读取。</summary>
 /// <remarks>模型偶尔会把收尾标记少写一个方括号（例如 <c>[/WEB_SEARCH]]</c>），
 /// 或者把中括号写成全角；这里统一按「/标签名」找断点，避免整段标记漏进聊天或查询词。</remarks>
 internal static class TagMarkers {
  /// <summary>定位收尾标记；返回正文结束位置和整块之后的起点。</summary>
  internal static bool FindClose(string text,int bodyStart,string tag,out int valueEnd,out int after) {
   valueEnd=-1;after=-1;
   if(string.IsNullOrEmpty(text)||bodyStart<0||bodyStart>text.Length) return false;
   string exact="[[/"+tag+"]]";
   int hit=text.IndexOf(exact,bodyStart,StringComparison.OrdinalIgnoreCase);
   if(hit>=0) {valueEnd=hit;after=hit+exact.Length;return true;}
   int loose=text.IndexOf("/"+tag,bodyStart,StringComparison.OrdinalIgnoreCase);
   if(loose<0) return false;
   int end=loose;
   while(end>bodyStart&&(text[end-1]=='['||text[end-1]=='［')) end--;
   valueEnd=end;
   int tail=loose+1+tag.Length;
   while(tail<text.Length&&(text[tail]==']'||text[tail]=='['||text[tail]=='］'||text[tail]=='［')) tail++;
   after=tail;
   return true;
  }
 }
}
