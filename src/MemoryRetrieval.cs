using System;
using System.Collections.Generic;

namespace ElymsPet {
 public static class MemoryRetrieval {
  const double K1 = 1.2, B = 0.75;
  /// <summary>按相关度、激活度和重要性挑出这次的记忆。</summary>
  public static MemoryItem[] Search(MemoryBook book, string query, DateTime now, int top) {
   if (book == null || book.Items == null || top <= 0) return new MemoryItem[0];
   var items = new List<MemoryItem>();
   var maps = new List<Dictionary<string, int>>();
   var lengths = new List<int>();
   var df = new Dictionary<string, int>();
   for (int i = 0; i < book.Items.Length; i++) {
    MemoryItem item = book.Items[i];
    if (item == null || item.Pending) continue;
    var tokens = new List<string>();
    MemoryStore.Tokens((item.Text ?? "") + " " + (item.Entity ?? ""), tokens);
    var map = new Dictionary<string, int>();
    for (int k = 0; k < tokens.Count; k++) {
     int seen; map.TryGetValue(tokens[k], out seen); map[tokens[k]] = seen + 1;
    }
    foreach (KeyValuePair<string, int> pair in map) {
     int count; df.TryGetValue(pair.Key, out count); df[pair.Key] = count + 1;
    }
    items.Add(item); maps.Add(map); lengths.Add(tokens.Count);
   }
   int total = items.Count;
   if (total == 0) return new MemoryItem[0];
   double sum = 0;
   for (int i = 0; i < lengths.Count; i++) sum += lengths[i];
   double average = sum / total;
   if (average <= 0) average = 1;
   var queryMap = new Dictionary<string, int>();
   var queryTokens = new List<string>();
   MemoryStore.Tokens(query, queryTokens);
   for (int i = 0; i < queryTokens.Count; i++) {
    int seen; queryMap.TryGetValue(queryTokens[i], out seen); queryMap[queryTokens[i]] = seen + 1;
   }
   var scores = new double[total];
   var rels = new double[total];
   double best = 0;
   for (int i = 0; i < total; i++) {
    double relevance = 0;
    if (queryMap.Count > 0) {
     double raw = 0;
     foreach (KeyValuePair<string, int> pair in queryMap) {
      int tf;
      if (!maps[i].TryGetValue(pair.Key, out tf) || tf == 0) continue;
      int docs; df.TryGetValue(pair.Key, out docs); if (docs <= 0) docs = 1;
      double idf = Math.Log(1.0 + (total - docs + 0.5) / (docs + 0.5));
      raw += idf * (tf * (K1 + 1)) / (tf + K1 * (1 - B + B * lengths[i] / average));
     }
     relevance = 1.0 - Math.Exp(-raw / 2.2);
     if (relevance > best) best = relevance;
    }
    rels[i] = relevance;
    scores[i] = 0.58 * relevance + 0.27 * MemoryStore.Activation(items[i], now) + 0.15 * items[i].Importance;
   }
   bool filtered = queryMap.Count > 0;
   if (filtered && best < 0.08) return new MemoryItem[0];
   if (top > total) top = total;
   var picked = new List<MemoryItem>();
   var used = new bool[total];
   for (int k = 0; k < top; k++) {
    int bestIndex = -1; double bestScore = double.MinValue;
    for (int i = 0; i < total; i++) {
     if (used[i]) continue;
     if (filtered && rels[i] < 0.06) continue;
     if (scores[i] > bestScore) { bestScore = scores[i]; bestIndex = i; }
    }
    if (bestIndex < 0) break;
    used[bestIndex] = true;
    picked.Add(items[bestIndex]);
   }
   return picked.ToArray();
  }

 }
}
