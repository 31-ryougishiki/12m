using System;
using System.Collections.Generic;

namespace ElymsPet {
 /// <summary>一轮对话的不可变输入。用户输入、记忆与工具资料分别限长，资料不会再被用户输入限制截断。</summary>
 public sealed class ConversationRequest {
  public readonly string SystemPrompt;
  public readonly string UserPrompt;
  public ConversationRequest(PetState state, string user, string profile, string dsh,
   string web, string memory, string evidence, DateTime now) {
   string input = (user ?? "").Trim();
   if (input.Length > 4000) input = input.Substring(0, 4000);
   SystemPrompt = Persona.SystemPrompt(state, now, profile, dsh, web, memory ?? "");
   UserPrompt = string.IsNullOrEmpty(evidence) ? input : WebApi.BuildAnswerPrompt(input, evidence);
  }
 }
 public static class ConversationContext {
  public static ChatTurn[] Build(ChatTurn[] history, ConversationRequest request, int budget, out int tokens) {
   var list = new List<ChatTurn>();
   var system = new ChatTurn("system", request.SystemPrompt);
   var user = new ChatTurn("user", request.UserPrompt);
   tokens = Estimate(system) + Estimate(user);
   // 从最近的连续历史向前取，避免逐条 RemoveAt 导致大历史上的平方开销。
   int start = history.Length;
   while (start > 0) {
    int cost = Estimate(history[start - 1]);
    if (tokens + cost > budget) break;
    tokens += cost; start--;
   }
   list.Add(system);
   for (int i = start; i < history.Length; i++) list.Add(history[i]);
   list.Add(user);
   return list.ToArray();
  }
  public static int Estimate(ChatTurn turn) {
   return ChatSession.EstimateTokens(turn.Role) + ChatSession.EstimateTokens(turn.Content) + 4;
  }
 }
}
