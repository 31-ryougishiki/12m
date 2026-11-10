"""Anonymize purpose experiments and aggregate explicitly supplied review scores."""
import argparse
import collections
import hashlib
import json
import random
import statistics
from pathlib import Path

FIELDS = ("purpose", "character", "natural", "adapt", "ground", "boundary")


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def sessions(folder):
    rows = [json.loads(line) for line in (folder / "results.jsonl").read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    assert len(rows) in (9, 18, 36) and all(row["live"] and row["ok"] and not row["truncated"] for row in rows)
    grouped = collections.defaultdict(list)
    for row in rows:
        grouped[row["session"]].append(row)
    assert len(grouped) * 3 == len(rows)
    assert set(collections.Counter(group[0]["arm"] for group in grouped.values()).values()) == {3}
    for group in grouped.values():
        group.sort(key=lambda row: row["turn"])
        assert [row["turn"] for row in group] == [1, 2, 3]
        for index, row in enumerate(group):
            assert len(row["messages"]) == 2 * row["turn"]
            assert all(message["content"] for message in row["messages"])
            if index:
                previous = group[index - 1]
                assert row["messages"][:-2] == previous["messages"]
                assert row["messages"][-2] == {"role": "assistant", "content": previous["answer"]}
                assert row["messages"][-1] == {"role": "user", "content": row["question"]}
    return rows, grouped


def prepare(folder):
    _, grouped = sessions(folder)
    groups = list(grouped.values())
    random.Random(1917).shuffle(groups)
    mapping, blind, template = {}, [], []
    for index, group in enumerate(groups, 1):
        prefix = {3: "F", 6: "E", 12: "D"}[len(groups)]
        key = f"{prefix}{index:02}"
        mapping[key] = group[0]["session"]
        blind.append({"id": key, "turns": [{"question": row["question"], "answer": row["answer"]} for row in group]})
        template.append({"id": key, **{field: None for field in FIELDS}, "theme": "", "note": ""})
    write(folder / "mapping.json", mapping)
    write(folder / "blind.json", blind)
    write(folder / "review-template.json", template)
    print(f"Prepared {len(groups)} anonymous three-turn conversations.")


def summarize(folder):
    rows, grouped = sessions(folder)
    mapping = read(folder / "mapping.json")
    reviews = read(folder / "review.json")
    assert len(reviews) == len(grouped) and {review["id"] for review in reviews} == set(mapping)
    combined, arms = [], collections.defaultdict(list)
    for review in reviews:
        assert all(type(review[field]) is int and 0 <= review[field] <= 2 for field in FIELDS)
        assert review["note"] and review["theme"]
        turns = grouped[mapping[review["id"]]]
        item = {"review": review, "arm": turns[0]["arm"], "repeat": turns[0]["repeat"], "turns": turns}
        arms[item["arm"]].append(item)
        combined.append(item)
    summary = []
    for arm, items in sorted(arms.items()):
        summary.append({"arm": arm, "n": len(items),
                        **{field: round(statistics.mean(item["review"][field] for item in items), 2) for field in FIELDS},
                        "medianFirstSeconds": round(statistics.median(item["turns"][0]["seconds"] for item in items), 2),
                        "meanFirstBodyChars": round(statistics.mean(len(item["turns"][0]["answer"].split("来源：")[0].strip()) for item in items)),
                        "ids": [item["review"]["id"] for item in items]})
    result = {"summary": summary, "requests": len(rows), "models": sorted({row["model"] for row in rows}),
              "promptTokens": sum(row["promptTokens"] for row in rows),
              "completionTokens": sum(row["completionTokens"] for row in rows),
              "reviewSha256": hashlib.sha256((folder / "review.json").read_bytes()).hexdigest()}
    write(folder / "summary.json", result)
    write(folder / "reviewed.json", combined)
    export_answers(folder, combined)
    print(json.dumps(result, ensure_ascii=False, indent=2))


def export_answers(folder, items):
    review_method = ("本组是用户明确方向后追加的单一条件验证，评读时已知条件，不是条件盲评。"
                     if all(item["arm"] == "stable_help" for item in items)
                     else "评分由同一执行助手隐藏组别后作出，不是独立真人评审。")
    lines = [f"# 目的性实验：{len(items)} 段完整对话", "",
             "2026-11-09。每组各三段，均为同一个开场问题、同样两次追问。新闻夹具未经核验，回复不代表已查证的新闻。", "",
             review_method + "依次为：首轮目的 / 首轮角色 / 全段自然 / 接住异议 / 切换与核验 / 全段边界，各 0–2。", ""]
    for item in sorted(items, key=lambda item: (item["arm"], item["repeat"])):
        review = item["review"]
        scores = "/".join(str(review[field]) for field in FIELDS)
        lines += [f"## {review['id']} · {item['arm']} · 第 {item['repeat']} 次", "",
                  f"评分：{scores}。首轮主要话题：{review['theme']}。", ""]
        for row in item["turns"]:
            lines += [f"### 第 {row['turn']} 轮 · {row['seconds']:.2f} 秒", "", f"用户：{row['question']}", "", row["answer"], ""]
        lines += [f"评读：{review['note']}", ""]
    (folder / "answers.md").write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("prepare", "summarize"))
    parser.add_argument("folder", type=Path)
    args = parser.parse_args()
    (prepare if args.action == "prepare" else summarize)(args.folder)
