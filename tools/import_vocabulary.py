"""固定来源，生成离线词库与遮词审计；不执行上游 JavaScript。"""
from pathlib import Path
import argparse
import hashlib
import json
import re
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
COMMIT = "5cef573933663c4673c6e0093f1df04e68018b1a"
URL = f"https://raw.githubusercontent.com/hefengxian/my-ielts/{COMMIT}/src/pages/vocabulary/vocabulary.js"
SHA256 = "e21e604c16c30f1310dc025ccf6e58b93f2f525b231735f0b27e093d5e0eb1ce"
BLANK = "______"
IRREGULAR = {
    "arise": "arose arisen", "awake": "awoke awoken", "be": "am is are was were been being",
    "bear": "bore born borne", "beat": "beaten", "become": "became", "begin": "began begun",
    "bend": "bent", "bind": "bound", "bite": "bit bitten", "bleed": "bled", "blow": "blew blown",
    "break": "broke broken", "breed": "bred", "bring": "brought", "build": "built", "buy": "bought",
    "catch": "caught", "choose": "chose chosen", "cling": "clung", "come": "came", "creep": "crept",
    "deal": "dealt", "dig": "dug", "do": "does did done", "draw": "drew drawn", "drink": "drank drunk",
    "drive": "drove driven", "eat": "ate eaten", "fall": "fell fallen", "feed": "fed", "feel": "felt",
    "fight": "fought", "find": "found", "flee": "fled", "fly": "flew flown", "forbid": "forbade forbidden",
    "forget": "forgot forgotten", "forgive": "forgave forgiven", "freeze": "froze frozen", "get": "got gotten",
    "give": "gave given", "go": "goes went gone", "grow": "grew grown", "hang": "hung", "have": "has had",
    "hear": "heard", "hide": "hid hidden", "hold": "held", "keep": "kept", "know": "knew known",
    "lay": "laid", "lead": "led", "leave": "left", "lend": "lent", "lie": "lay lain lying", "light": "lit",
    "lose": "lost", "make": "made", "mean": "meant", "meet": "met", "pay": "paid", "ride": "rode ridden",
    "ring": "rang rung", "rise": "rose risen", "run": "ran", "say": "said", "see": "saw seen",
    "seek": "sought", "sell": "sold", "send": "sent", "shake": "shook shaken", "shine": "shone",
    "shoot": "shot", "show": "shown", "shrink": "shrank shrunk", "sing": "sang sung", "sink": "sank sunk",
    "sit": "sat", "sleep": "slept", "slide": "slid", "speak": "spoke spoken", "spend": "spent",
    "spin": "spun", "spring": "sprang sprung", "stand": "stood", "steal": "stole stolen", "stick": "stuck",
    "sting": "stung", "strike": "struck stricken", "swear": "swore sworn", "sweep": "swept",
    "swim": "swam swum", "swing": "swung", "take": "took taken", "teach": "taught", "tear": "tore torn",
    "tell": "told", "think": "thought", "throw": "threw thrown", "understand": "understood",
    "wake": "woke woken", "wear": "wore worn", "weave": "wove woven", "weep": "wept", "win": "won",
    "wind": "wound", "withdraw": "withdrew withdrawn", "write": "wrote written", "foot": "feet",
    "tooth": "teeth", "goose": "geese", "mouse": "mice", "child": "children", "man": "men",
    "woman": "women", "person": "people", "ox": "oxen", "leaf": "leaves", "life": "lives",
    "knife": "knives", "wife": "wives", "wolf": "wolves", "shelf": "shelves", "thief": "thieves",
    "half": "halves", "loaf": "loaves", "self": "selves", "die": "dying", "tie": "tying",
    "good": "better best", "bad": "worse worst", "far": "farther further farthest furthest",
    "analysis": "analyses", "basis": "bases", "crisis": "crises", "criterion": "criteria",
    "phenomenon": "phenomena", "hypothesis": "hypotheses", "thesis": "theses", "index": "indices",
    "medium": "media", "datum": "data", "bacterium": "bacteria", "fungus": "fungi", "axis": "axes",
}


def forms(answer):
    """只用于例句遮词，不扩展拼写题的允许答案。"""
    answer = answer.lower().strip()
    prefix, _, word = answer.rpartition(" ")
    prefix = prefix + " " if prefix else ""
    result = {word, word + "s", word + "es", word + "ed", word + "ing", word + "er", word + "est"}
    if word.endswith("e"):
        result.update([word + "d", word[:-1] + "ing", word + "r", word + "st"])
    if word.endswith("y"):
        result.update([word[:-1] + "ies", word[:-1] + "ied", word[:-1] + "ier", word[:-1] + "iest"])
    if len(word) > 2 and re.search(r"[aeiou][bcdfghjklmnpqrstvwxyz]$", word):
        result.update([word + word[-1] + ending for ending in ("ed", "ing", "er", "est")])
    if word.endswith("ie"):
        result.add(word[:-2] + "ying")
    result.update(IRREGULAR.get(word, "").split())
    return {prefix + part for part in result}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path)
    parser.add_argument("--inspect", action="store_true")
    args = parser.parse_args()
    cache = ROOT / ".tools" / "vocabulary-source.js"
    cache.parent.mkdir(exist_ok=True)
    if args.source:
        raw = args.source.read_bytes()
    elif cache.exists():
        raw = cache.read_bytes()
    else:
        with urllib.request.urlopen(URL, timeout=45) as response:
            raw = response.read()
    if hashlib.sha256(raw).hexdigest() != SHA256:
        raise ValueError("词库来源校验失败，拒绝导入未知快照")
    cache.write_bytes(raw)
    text = raw.decode("utf-8-sig")
    source = json.loads(text[text.index("{"):text.rindex("}") + 1])
    correction_path = ROOT / "data" / "corrections.json"
    corrections = json.loads(correction_path.read_text(encoding="utf-8")) if correction_path.exists() else {}
    entries, unresolved, changes = [], [], []
    for chapter, content in source.items():
        for group_number, group in enumerate(content["words"], 1):
            for original in group:
                override = corrections.get(str(original["id"]), {})
                example = override.get("example", original["example"])
                meaning = override.get("meaning", original["meaning"])
                surface_forms = set(override.get("surfaceForms", []))
                for answer in original["word"]:
                    surface_forms.update(forms(answer))
                pattern = re.compile(r"(?<![A-Za-z])(?:" + "|".join(re.escape(s) for s in sorted(surface_forms, key=lambda s: (-len(s), s))) + r")(?![A-Za-z])", re.I)
                matches = list(pattern.finditer(example))
                cloze = pattern.sub(BLANK, example)
                if not matches:
                    unresolved.append({"id": original["id"], "word": original["word"], "meaning": meaning, "example": example})
                if override:
                    changes.append({"id": original["id"], "reason": override.get("reason", "reviewed surface form")})
                entries.append({"Id": original["id"], "Chapter": chapter, "Group": group_number,
                                "Answers": original["word"], "PartOfSpeech": original["pos"], "Meaning": meaning, "OriginalMeaning": original["meaning"],
                                "OriginalExample": original["example"], "Example": example, "Cloze": cloze,
                                "MaskedForms": sorted({m.group(0) for m in matches}),
                                "ExampleOrigin": "locally-authored" if "example" in override else "source",
                                "Extra": original["extra"]})
    (ROOT / "artifacts").mkdir(exist_ok=True)
    (ROOT / "artifacts" / "unresolved-masks.json").write_text(json.dumps(unresolved, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"entries": len(entries), "masked": len(entries)-len(unresolved), "unresolved": len(unresolved), "corrections": len(changes)}, ensure_ascii=False))
    if unresolved:
        for entry in unresolved:
            print(f"{entry['id']} {entry['word']} | {entry['example']}")
        if not args.inspect:
            raise SystemExit("仍有例句未处理，拒绝生成不完整词库")
        return
    assert len(entries) == 3674 and len({e['Id'] for e in entries}) == 3674
    assert all(e["Meaning"].strip() and BLANK in e["Cloze"] for e in entries)
    for entry in entries:
        for answer in entry["Answers"]:
            assert not re.search(r"(?<![A-Za-z])" + re.escape(answer) + r"(?![A-Za-z])", entry["Cloze"], re.I), entry["Id"]
            assert not re.search(r"(?<![A-Za-z])" + re.escape(answer) + r"(?![A-Za-z])", entry["Meaning"], re.I), entry["Id"]
    (ROOT / "data").mkdir(exist_ok=True)
    (ROOT / "data" / "vocabulary.json").write_text(json.dumps({"SchemaVersion": 1, "SourceUrl": URL, "SourceCommit": COMMIT, "Entries": entries}, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    (ROOT / "artifacts" / "mask-audit.json").write_text(json.dumps({"sourceSha256": SHA256, "entries": len(entries), "unresolved": [], "changes": changes}, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
