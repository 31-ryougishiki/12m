"""Prepare anonymous replies, then aggregate explicitly supplied review scores.

This utility does not call a model or infer subjective quality from keywords.
"""
import argparse
import collections
import json
import random
import statistics
from pathlib import Path


def read_jsonl(path):
    return [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]


def prepare(folder):
    rows = read_jsonl(folder / "results.jsonl")
    replies = [row for row in rows if row["kind"] == "reply"]
    assert len(replies) == 36, "Expected a completed 36-reply experiment"
    assert all(row["live"] and row["ok"] for row in rows)
    assert len({row["question"] for row in rows}) == 1
    counts = collections.Counter((row["arm"], row["thinking"]) for row in replies)
    assert len(counts) == 12 and set(counts.values()) == {3}
    random.Random(731).shuffle(replies)
    mapping, blind, template = {}, [], []
    for index, row in enumerate(replies, 1):
        key = f"R{index:02}"
        mapping[key] = row["id"]
        blind.append({"id": key, "answer": row["answer"]})
        template.append({"id": key, "facts": None, "opinion": None,
                         "natural": None, "character": None, "note": ""})
    (folder / "blind.json").write_text(json.dumps(blind, ensure_ascii=False, indent=2), encoding="utf-8")
    (folder / "mapping.json").write_text(json.dumps(mapping, indent=2), encoding="utf-8")
    (folder / "review-template.json").write_text(json.dumps(template, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Prepared {len(blind)} anonymous replies in {folder}")


def summarize(folder):
    rows = read_jsonl(folder / "results.jsonl")
    replies = {row["id"]: row for row in rows if row["kind"] == "reply"}
    mapping = json.loads((folder / "mapping.json").read_text(encoding="utf-8"))
    reviews = json.loads((folder / "review.json").read_text(encoding="utf-8"))
    assert len(reviews) == 36 and len({row["id"] for row in reviews}) == 36
    groups = collections.defaultdict(list)
    joined = []
    for review in reviews:
        for field in ("facts", "opinion", "natural", "character"):
            assert type(review[field]) is int and 0 <= review[field] <= 2
        assert review["note"]
        sample = replies[mapping[review["id"]]]
        item = {**sample, "review": review}
        groups[(sample["arm"], sample["thinking"])].append(item)
        joined.append(item)
    summary = []
    for (arm, thinking), samples in sorted(groups.items()):
        entry = {"arm": arm, "thinking": thinking, "n": len(samples)}
        for field in ("facts", "opinion", "natural", "character"):
            entry[field] = round(statistics.mean(row["review"][field] for row in samples), 2)
        entry["medianSeconds"] = round(statistics.median(row["seconds"] for row in samples), 2)
        entry["medianPipelineSeconds"] = round(statistics.median(row["pipelineSeconds"] for row in samples), 2)
        entry["meanBodyChars"] = round(statistics.mean(len(row["answer"].split("来源：")[0].strip()) for row in samples))
        entry["truncated"] = sum(row["truncated"] for row in samples)
        entry["blindIds"] = [row["review"]["id"] for row in samples]
        summary.append(entry)
    result = {"summary": summary, "requests": len(rows),
              "promptTokens": sum(row["promptTokens"] for row in rows),
              "completionTokens": sum(row["completionTokens"] for row in rows)}
    (folder / "summary.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    (folder / "reviewed.json").write_text(json.dumps(joined, ensure_ascii=False, indent=2), encoding="utf-8")
    export_answers(folder, joined)
    print(json.dumps(result, ensure_ascii=False, indent=2))


def export_answers(folder, rows):
    lines = ["# 固定问题的 36 条真实模型回复", "",
             "问题：你怎么看待最近 AI 的发展？", "",
             "新闻材料沿用固定的未核验夹具；以下原样保留模型回复，不能当成已核实新闻。"
             "评分由同一执行助手匿名评读后给出，不是用户认可或独立评审。", "",
             "评分顺序：证据与人设边界 / 回应观点 / 自然聊天感 / 角色辨识度，各 0–2。", ""]
    for sample in sorted(rows, key=lambda row: (row["arm"], row["thinking"], row["repeat"])):
        review = sample["review"]
        mode = "开" if sample["thinking"] else "关"
        scores = "/".join(str(review[field]) for field in ("facts", "opinion", "natural", "character"))
        lines.extend([
            f"## {review['id']} · {sample['arm']} · 思考{mode} · 第 {sample['repeat']} 次", "",
            f"最终生成 {sample['seconds']:.2f} 秒；含抽取 {sample['pipelineSeconds']:.2f} 秒；评分 {scores}。", "",
            sample["answer"], "", f"评读：{review['note']}", ""])
    (folder / "answers.md").write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("prepare", "summarize"))
    parser.add_argument("folder", type=Path)
    args = parser.parse_args()
    (prepare if args.action == "prepare" else summarize)(args.folder)
