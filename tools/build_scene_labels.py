"""从已整理的剧情快照生成仅含已探索标题的轻量节点索引。"""
import argparse
import json
from collections import defaultdict
from pathlib import Path


def build(source: Path, output: Path) -> None:
    archive = json.loads(source.read_text(encoding="utf-8"))
    candidates = defaultdict(set)
    for node in archive["nodes"]:
        title = node.get("title", "")
        label = node.get("exploredTitle", "").strip()
        if not label or node.get("future") or node.get("placeholder"):
            continue
        if node.get("group") == "System" or title.startswith("VariableDeclare"):
            continue
        keys = set(node.get("visitKeys") or [])
        subtitle = node.get("subtitle", "")
        if subtitle:
            keys.add(title + "." + subtitle)
        # 基础名只有在所有变体的标题一致时才提供，避免把一个分支标成另一个。
        keys.add(title)
        for key in keys:
            if key:
                candidates[key].add(label)
    labels = {key: next(iter(values)) for key, values in sorted(candidates.items()) if len(values) == 1}
    result = {
        "schemaVersion": 1,
        "source": "colsalley《幼神》授权脚本与幼神资料终端的已探索场景标题，CC BY-NC-SA 4.0",
        "nodes": labels,
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Generated {len(labels)} unambiguous scene labels: {output}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "data/scene-labels.json")
    args = parser.parse_args()
    build(args.source, args.output)
