#!/usr/bin/env python3
"""잠깨비 도깨비 프롬프트 시험기.

data/ 의 장면마다 프롬프트를 만들어 로컬 모델(Ollama 또는 LM Studio)에 넣고, 자동 검사 결과와 함께
CSV로 저장한다. 말투·상황 반영 점수는 사람이 CSV에서 채운다. 표준 라이브러리만 쓴다.

장면은 터치 상호작용 기획안(docs/npc-touch-interaction-design.pdf)의 흐름을 따른다.
주인: NPC 탭 → 짧은 인사 → 선택지(안부 묻기 · 관리하기 · 이야기 듣기) → 반응, 그리고 끌어당기기.
손님: 건드릴 수 없고 관람만 한다. 도깨비는 짧은 말풍선을 띄운 채 걸어 다닌다.
행동은 네 애니메이션(Idle · Walking · Positive · Struggle) 중 하나로 표현되고, 세부 감정은 대사로 드러낸다.
이야기 듣기는 AI 로 만들지 않고 정해진 이야기 대사를 쓰고, 끌려갈 때(끌어당기기)는 대사를 띄우지 않으므로 그 장면은 건너뛴다.

    python run.py --models gemma3:1b,qwen3:1.7b
    python run.py --backend lmstudio --list-models
    python run.py --backend lmstudio --models google/gemma-3-1b --repeat 3
    python run.py --dry-run --variants single --scenes S05
"""

import argparse
import random
import csv
import http.client
import json
import re
import statistics
import sys
import time
import urllib.error
import urllib.request
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path

BASE = Path(__file__).resolve().parent
DATA = BASE / "data"
# 게임의 기본 대사 (출력 검사에 걸려 기본 대사로 대신할 때 무엇을 띄우는지 흉내 낸다). 없으면 흉내 내지 않는다
DEFAULT_LINES = BASE.parent / "unity-npc-generator" / "Data" / "default_lines.json"

VARIANTS = {
    "single": "한 번 호출. 상황은 키-값, 예시 대사와 대화 기억 포함",
    "facts_sentence": "single과 같되 상황을 문장으로",
    "no_examples": "single에서 예시 대사를 뺌",
    "no_memory": "single에서 대화 기억을 뺌",
    "two_step": "행동 선택과 대사 생성을 두 번에 나눠 호출",
}

# 'localhost' 가 아니라 127.0.0.1 로 붙는다. Windows 에서 localhost 는 IPv6(::1)부터 풀리는데 두 서버는 IPv4 에서만 듣고,
# Windows 는 거절된 연결을 약 2초 붙잡다가 IPv4 로 넘어간다. 그 2초가 모든 요청의 응답ms 에 더해졌다
# (2026-11-11 시험: 모델·프롬프트 길이와 상관없이 응답ms 가 '2초 + 출력 토큰당 약 55ms' 였다)
BACKENDS = {
    "ollama": {"name": "Ollama", "host": "http://127.0.0.1:11434"},
    "lmstudio": {"name": "LM Studio", "host": "http://127.0.0.1:1234"},  # <- 사용
}

# (내부 키, CSV 머리글). 뒤의 세 칸은 사람이 채운다.
FIELDS = [
    ("run_at", "실행시각"), ("model", "모델"), ("variant", "방식"), ("format", "형식제약"),
    ("scene", "장면"), ("spirit", "도깨비"), ("mode", "모드"), ("input", "선택지"), ("repeat", "반복"),
    ("intimacy", "친밀도"), ("address_expected", "기대호칭"), ("address_used", "대사호칭"), ("address_ok", "호칭OK"),
    ("action", "행동"), ("animation", "애니메이션"), ("target", "대상"), ("line", "대사"),
    ("final", "최종"), ("attempts", "시도"), ("rejected", "거른이유"),
    ("full_line", "자르기전대사"), ("speech_fixed", "다듬기전대사"), ("ask_filled", "코드가채운부탁"), ("fallback_line", "띄운기본대사"), ("cut", "문장자름"), ("truncated", "토큰한도끊김"),
    ("json_ok", "JSON"), ("pure_json", "순수JSON"), ("action_ok", "행동OK"), ("target_ok", "대상OK"),
    ("expected_ok", "기대행동"), ("forbidden_ok", "금지어없음"), ("mention_ok", "필수어포함"),
    ("bad_ok", "공통금칙어없음"), ("bad_hits", "걸린금칙어"),
    ("original_ok", "예시안베낌"), ("fresh_ok", "반복안됨"), ("copy_hit", "겹친구절"),
    ("length", "길이"), ("length_ok", "길이OK"), ("newline_ok", "줄바꿈OK"), ("foreign_ok", "외국어없음"),
    ("ending", "어미(참고)"),
    ("wall_ms", "응답ms"), ("load_ms", "로드ms"), ("prompt_tokens", "입력토큰"), ("gen_tokens", "출력토큰"),
    ("reasoning_tokens", "생각토큰"), ("tok_per_s", "초당토큰"), ("system_merged", "시스템합침"),
    ("raw", "원본출력"), ("error", "오류"), ("note", "장면메모"),
    ("score_style", "말투(1-5)"), ("score_situation", "상황반영(1-5)"), ("memo", "메모"),
]

# 장면에 따라 사용자 메시지에 싣는 페르소나 칸 (키, 이름). 이 순서로 싣는다.
# 성격·감정 표현·말투·예시는 도깨비마다 고정한 시스템 메시지에 늘 싣는다.
PERSONA_FIELDS = [
    ("values", "가치관"),
    ("onCare", "관리를 받으면"),
]
# 행동마다 싣는 칸. 장면과 상관없는 칸까지 실으면 프롬프트만 길어지고, 작은 모델이 탭 인사에 관리 감사 문구를
# 섞는 식으로 끌어 쓴다. 감사에는 좋아하는 것·가치관이 필요 없다.
ACTION_FIELDS = {
    # 좋아하는 것·싫어하는 것 칸은 뺐다(2026-11-13): 탭 인사에 실었더니 그 낱말을 상황과 상관없이 대사에 끼워 넣었다.
    # (그 전에도 Kanana 1.5 가 흐려진 상태 대신 날씨·하늘 이야기를 했다(S03 2/3))
    "greet": [],                            # 가치관은 성격(시스템 메시지)에 있다. 읽을 글을 줄인다
    "answer_status": ["onCare"],
    "recall_talk": [],                      # 꺼낼 기억 하나에 집중한다
    "thank_joyful": ["onCare"],
    "thank_calm": ["onCare"],
    # 끌려갈 때(struggle_*)는 대사가 없어 칸이 없다
    "wander_greet": [],                     # 20자 말풍선이라 상대 태도만으로 충분하다
    "wander_recall": [],
}
# 관리를 못 받은 상태(선명도가 흐려짐·재봉인)에서는 '관리를 받으면' 칸을 싣지 않는다. 실으면 Mi:dm 이 닦아 주지도 않았는데
# "닦아 주셔서 감사합니다", "선명해졌어요" 하고 고마워했다(S03 3/3, 2026-11-11 시험)
UNCARED_CLARITY = {"흐려짐", "재봉인"}
# AI 로 만들지 않고 정해진 대사를 쓰는 행동. 이야기는 2B 모델이 사실을 지어내거나 빼먹어서, 검수한 이야기 대사
# 3개(unity-npc-generator/Data/default_lines.json 의 stories)를 게임이 차례로 꺼내 쓴다. 이 시험기는 건너뛴다.
FIXED_ACTIONS = {"tell_story"}
# 대사 없이 몸짓(애니메이션)만 하는 행동. 끌려갈 때는 대사를 띄우지 않는다(2026-11-13) — 게임은 모델을 부르지 않고 행동만 고른다.
# 이 시험기도 건너뛴다 (C# NpcActions.IsSilent)
SILENT_ACTIONS = {"struggle_protest", "struggle_play"}

# 형식 설명은 형식 제약(--no-format 이 아닐 때의 JSON 스키마, 기기의 GBNF)이 없을 때만 싣는다.
# 제약이 없으면 행동 칸에 설명이나 선택지 이름("안부 묻기")을 적는 일이 있어 '영문 ID'라고 못 박는다.
FORMAT_SINGLE = '출력 형식: {"action": "후보의 행동 ID(영문)", "target": "대상 ID(영문)", "line": "대사"}'
FORMAT_CHOICE = '출력 형식: {"action": "후보의 행동 ID(영문)", "target": "대상 ID(영문)"}'
FORMAT_LINE = '출력 형식: {"line": "대사"}'
# 응답 시간은 거의 출력 토큰 수에 비례한다(Kanana 2B 는 초당 6토큰 안팎, 2026-11-12 시험 중앙값 7.4초). 대상이 하나뿐이면 target 칸을,
# 행동도 하나뿐이면 action 칸까지 빼서 JSON 틀이 차지하는 토큰(약 20개)을 줄인다. 빠진 칸은 읽을 때 후보로 채운다(evaluate).
# 형식 제약(JSON 스키마·GBNF)이 있을 때는 그대로 둔다
FORMAT_ACTION_LINE = '출력 형식: {"action": "후보의 행동 ID(영문)", "line": "대사"}'
CANDIDATES_HEAD_ACTION = "후보 (action 에는 행동 ID 를 아래 영문 그대로 적는다):"
CANDIDATES_HEAD = "후보:"
CANDIDATES_HEAD_FREE = "후보 (action 에는 행동 ID, target 에는 대상 ID 를 아래 영문 그대로 적는다):"

THINK_RE = re.compile(r"<think>.*?</think>", re.S)
OBJECT_RE = re.compile(r"\{.*\}", re.S)
# 영문, 일본 가나(U+3040~30FF), 한자(U+4E00~9FFF)
FOREIGN_RE = re.compile(f"[A-Za-z{chr(0x3040)}-{chr(0x30FF)}{chr(0x4E00)}-{chr(0x9FFF)}]")
TRAILING_RE = re.compile(r"[\s.!?…~,·\"'“”‘’)\]]+$")
SENTENCE_END = ".!?~。"          # 이것 뒤에 공백이나 글 끝이 오면 문장이 끝난다. 점 두 개 이상(...)과 '…'는 말줄임이라 끝이 아니다
SENTENCE_CLOSE = "\"'”’)]」』"   # 문장 끝 뒤에 붙는 닫는 따옴표·괄호
PUNCT_RE = re.compile(r"[\s.!?…~,·\"'“”‘’]")
SPACE_RE = re.compile(r"\s+")
# 띄어쓰기·문장부호를 뺀 글자 수. 두 대사가 이만큼 같은 구절을 가지면 같은 대사(베낌·반복)로 본다.
# "덕분에 향이 다시 맑아졌구먼"(12자)은 걸리고, 인사말 "주인님 안녕하"(6자)만 같은 것은 걸리지 않는다
COPY_MIN = 8
# 같은 대사로 보는 기준 (게임의 NpcLineChecks 와 같다). 2026-11-11 17:29·18:03 결과로 정했다
# 2026-11-12 11:15 시험 뒤 60% → 80%, 2026-11-13 시험 뒤 80% → 90%로 바꿨다. 60%에서는 "이틀 전에 닦아 줘서 정말 고마워"처럼 꼭 들어가야 할 말만 같아도 반복으로 거르고,
# 다시 만들어도 같은 말이 나와 기본 대사로 넘어갔다(첫 시도 통과 63%). 최근 대사와 90% 이상 겹치면 같은 대사로 보고 다른 대사를 만든다
SIMILAR_PAIRS = 0.9     # 글자 쌍이 이만큼 이상 겹치면 같은 대사 (2026-11-13: 80% → 90%. 80% 에서는 "정성껏 관리해 줘서 고마워."↔"관리해 줘서 고마워."(82%)까지 반복으로 보고 기본 대사로 넘어갔다)
CONTAINED_SHARE = 0.9   # 두 대사가 함께 가진 가장 긴 구절이 더 긴 대사의 이만큼이면 (짧은 쪽이 긴 쪽에 들어 있다고 같다고 보지 않는다: "정성껏 관리해 줘서 고마워."↔"관리해 줘서 고마워."는 다른 대사)
CONTAINED_MIN = 5       # 그 겹침이 적어도 이 글자 수 ("고마워" 하나로는 같다고 보지 않는다)
SIMILAR_OPENING = 0.9   # 앞 OPENING_CHARS 글자가 이만큼 겹치면 ("주인님, 관리해 줘서 고마워요!"로 늘 시작)
OPENING_CHARS = 7       # 앞부분으로 비교하는 글자 수. 첫 문장으로 비교하면 "주인님~"의 '~'에서 끊겨 놓쳤다. 10 → 7 (2026-11-17): 탭 인사 12건 중 9건이 "반가워, 오늘 아침…"으로 시작해도 뒤가 달라 반복으로 잡히지 않았다 (7글자 = "반가워오늘아침")
OPENING_MIN = 6         # 앞부분 비교에 필요한 글자 수 ("고마워." 같은 짧은 말은 보지 않는다)
RECENT_LINES = 10       # 시나리오(같은 도깨비의 같은 선택지)마다 기억하는 최근 대사 수 (게임의 NpcRequestManager.RecentLineCount). 10개 중 하나와 90% 이상 겹치면 새로 만든다
# 어미를 볼 때 건너뛰는 감탄사만 있는 문장·마디 ("어흠.", "허허,", "히힛!"). 짧은 문장을 다 건너뛰면 해요체 도깨비의
# "고마워! 다시 오세요."의 "고마워!"(3글자)까지 건너뛰어서, 알려진 감탄사만 건너뛴다. 게임의 NpcLineChecks.IsInterjection 과 같다
# 홀로 쓴 대답 "네!", "예." 도 어미를 보지 않는다 (구름결 "네! 천천히 보세요."). 게임의 NpcLineChecks.Acknowledgment 와 같다
ACKNOWLEDGMENT_RE = re.compile("^(네|예)$")
INTERJECTION_RE = re.compile("^(어흠|에헴|흠|허허|어허|허|히힛|히히|히잉|헤헤|킁킁|앗싸|앗|꺄|꺅|오예|야호|와|오|어|음|아)+$")
VOCATIVE_RE = re.compile(r"[\s,]*(주인님|주인|손님)$")  # 문장 끝에 붙은 부르는 말
STORY_COUNT = 3  # 도깨비마다 실화 이야기 수


class BackendError(Exception):
    pass


# ---------- 데이터 ----------

def load_content():
    action_file = read_json("actions.json")
    scene_file = read_json("scenes.json")
    return {
        "personas": {p["relicId"]: p for p in read_json("personas.json")["personas"]},
        "actions": {a["id"]: a for a in action_file["actions"]},
        "animations": action_file["animations"],
        "inputs": scene_file["inputs"],
        "labels": scene_file["labels"],
        "scenes": scene_file["scenes"],
        "forbidden": compile_forbidden(read_json("forbidden.json")),
        "default_lines": json.loads(DEFAULT_LINES.read_text(encoding="utf-8")) if DEFAULT_LINES.exists() else None,
        "last_fallback": {},  # 기본 대사 고르기에서 (도깨비:행동:변형) 마다 바로 앞에 띄운 것
    }


def read_json(name):
    with open(DATA / name, encoding="utf-8") as f:
        return json.load(f)


# patterns 의 {ㄴ받침}: 받침이 ㄴ인 한글 글자 399자 ('-ㄴ다/-는다' 동사 현재 평서형을 가린다). C# NpcLineFilter.NieunFinal 과 같다
NIEUN_FINAL = "".join(chr(0xAC00 + i) for i in range(11172) if i % 28 == 4)


def compile_forbidden(data):
    """공통 금칙어 (data/forbidden.json). 대사와 낱말의 공백을 지우고 찾는다 — C# NpcLineFilter 와 같은 규칙."""
    groups = []
    for g in data["groups"]:
        words = [SPACE_RE.sub("", w) for w in g.get("words", []) if w.strip()]
        patterns = [re.compile(p.replace("{ㄴ받침}", NIEUN_FINAL)) for p in g.get("patterns", [])]
        groups.append((g["label"], words, patterns))
    return groups


def forbidden_hits(line, groups):
    core = SPACE_RE.sub("", line)
    hits = []
    for label, words, patterns in groups:
        hits += [f"{label}:{w}" for w in words if w in core]
        for p in patterns:
            m = p.search(core)
            if m:
                hits.append(f"{label}:{m.group(0)}")
    return hits


def validate(content):
    problems = []
    actions = content["actions"]
    for p in content["personas"].values():
        if len(p.get("stories", [])) != STORY_COUNT:
            problems.append(f"{p['name']}: 실화 이야기(stories)가 {STORY_COUNT}개가 아니다.")
        for e in p.get("examples", []):
            hits = forbidden_hits(e, content["forbidden"])
            if hits:
                problems.append(f"{p['name']}: 예시 대사 '{e}' 가 공통 금칙어에 걸린다 ({', '.join(hits)}).")
        hint = p.get("endingHint") or {}
        if not (hint.get("pattern") or hint.get("notPattern")):
            problems.append(f"{p['name']}: 어미 규칙(endingHint)이 없다. 존댓말·반말 섞기를 거르지 못한다.")
        for key in FLAVOR_KEYS:
            items = (p.get("flavors") or {}).get(key) or []
            if len(items) < 2:
                problems.append(f"{p['name']}: 곁들일 거리(flavors.{key})가 2개보다 적다. 같은 상황마다 같은 대사가 나온다.")
            for item in items:
                if forbidden_hits(item, content["forbidden"]):
                    problems.append(f"{p['name']}: 곁들일 거리 '{item}' 가 공통 금칙어에 걸린다.")
    for a in actions.values():
        if a["animation"] not in content["animations"]:
            problems.append(f"행동 '{a['id']}' 의 애니메이션 '{a['animation']}' 이 animations 목록에 없다.")
    for s in content["scenes"]:
        sid = s["id"]
        if s["relicId"] not in content["personas"]:
            problems.append(f"{sid}: relicId {s['relicId']} 의 페르소나가 없다.")
        if s["input"] not in content["inputs"]:
            problems.append(f"{sid}: 선택지 '{s['input']}' 가 inputs 에 없다.")
        for c in s["candidates"]:
            action = actions.get(c["action"])
            if action is None:
                problems.append(f"{sid}: 행동 '{c['action']}' 이 actions.json 에 없다.")
                continue
            if s["input"] not in action.get("inputs", []):
                problems.append(f"{sid}: 행동 '{c['action']}' 은 선택지 '{s['input']}' 에서 쓰지 않는다.")
            for t in c["targets"]:
                if t not in ("player", "none") and t not in content["labels"]:
                    problems.append(f"{sid}: 대상 '{t}' 의 이름이 labels 에 없다.")
        candidate_actions = {c["action"] for c in s["candidates"]}
        for a in s.get("expected", []):
            if a not in candidate_actions:
                problems.append(f"{sid}: 기대 행동 '{a}' 가 후보에 없다.")
        # 선명도는 방치 상태라 친구에게 내보내지 않는다 (ExhibitSpiritView 참고).
        if s["mode"] == "visitor" and any(f["key"] == "clarity" for f in s["facts"]):
            problems.append(f"{sid}: 방문자 장면에 선명도(clarity)가 들어 있다.")
        if s["input"] == "listen_story":
            problems += story_problems(s, content["personas"].get(s["relicId"]))
    return problems


def story_problems(scene, persona):
    """이야기 장면의 자료는 페르소나 stories 중 storyIndex 번째와 같아야 한다 (게임이 돌려 가며 하나를 준다)."""
    sid = scene["id"]
    if persona is None:
        return []
    stories = persona.get("stories", [])
    idx = scene.get("storyIndex")
    if not isinstance(idx, int) or not 0 <= idx < len(stories):
        return [f"{sid}: storyIndex 가 없거나 stories 범위 밖이다."]
    texts = [f["text"].removeprefix("검수된 사실: ") for f in scene["facts"] if f["key"] == "verified_story"]
    if texts != [stories[idx]["fact"]]:
        return [f"{sid}: 이야기 자료가 {persona['name']} 의 stories[{idx}] 와 다르다."]
    return []


def meaning(content, action_id):
    action = content["actions"].get(action_id)
    return action["meaning"] if action else action_id


# ---------- 프롬프트 ----------

# 2026-11-13 세 번째 시험: 프롬프트에 하지 말 것('~하지 않는다', '넣지 않는다')을 늘어놓으면 작은 모델이 그 말에 끌려 설명문을 읊거나 헷갈렸다.
# 규칙은 해야 할 것만 짧게 적는다. 하지 말아야 할 것(밀어내기·낮춰 부르기·자기 이름·영어·몸짓 설명·존댓말·날씨…)은 프롬프트에 적지 않고
# 말투 다듬기(normalize_speech)와 출력 검사(game_problem·공통 금칙어)가 맡는다. 규칙이 짧을수록 읽는 시간(응답 시간의 대부분)도 줄어든다.
# 규칙에 '네 몸', '네 것'을 쓰면 "네 자개는"으로 옮기므로 '네'를 쓰지 않는다. C# PromptAssembler 의 Rule… 과 같다
# 2026-11-17 18:21 시험: 모델이 자기가 전시관을 구경하러 온 것처럼 말했다("전시관을 둘러보니 참 멋진데!", "반가워서 아침부터 달려왔어", "반가워서 왔어"). 도깨비는 주인의 전시관에 살고
# 찾아온 상대(주인·친구)를 맞는 쪽이다. 20:39 시험: 규칙의 "맞이한다"를 읽고 "마중 나올 줄 알았어"·"반갑게 맞이할 시간이야"처럼 행동을 설명했다 — 행동 동사 대신 "말한다"로 쓴다. 한 줄로 못 박는다 (시스템 메시지는 도깨비마다 한 번만 읽는다). C# PromptAssembler.RuleBond 와 같다
# 2026-11-17 22:07 시험: 규칙의 "전시관"을 읽은 모델이 "전시관이 참 포근하네"처럼 우리 전시관의 인상을 말했다. 명사 "전시관"을 규칙·손님 상황 문장에서 뺐다
RULE_BOND = "- 너는 이곳에 살며, 찾아온 상대가 정이 들도록 다정하게 말한다."
RULE_FACTS = "- 자료에 있는 일만 말하고, 시간대와 며칠 전은 적힌 대로 말한다."
RULE_ROLE = "- 관리는 상대가 너를 닦아 주는 일이다. 바라는 것은 상대에게 부탁하고, 몸과 기분은 느끼는 대로 '내 ~'라고 말한다."
RULE_LINE = "- 소리 내어 하는 한국어 말만 쓴다."
# 도깨비는 모두 반말이라 말투를 공통 규칙으로 둔다. 어긋난 어미는 말투 다듬기가 고치고 출력 검사가 문장마다 어미를 본다
RULE_SPEECH = "- 친한 친구에게 말하듯 편한 현대 반말로 말한다."
# 한 문장이면 출력 토큰이 줄어 응답이 빨라진다 (응답 시간은 읽는 토큰 + 쓰는 토큰에 비례한다)
RULE_ORDER = "- 지금 상황에 맞는 말을 짧은 한 문장으로, 우리말 어순대로 말한다. 묻는 말만 ?로 끝낸다."
# 2026-11-17: "곁들일 거리나 지금 상황의 한마디를 덧붙여"를 읽은 모델이 없는 사정을 한마디로 지어냈다("궁금한 점이 많겠네", "오늘 밤은 너무 재미있었어"). 덧붙이라고 하지 않고 짧게 말하게 한다
RULE_VARY = "- 첫 낱말로 시작해 매번 다르게 말한다."
# (규칙 줄 "부탁은 '~해 줘'처럼 바로 말한다"는 뺐다: 부탁의 꼴은 '할 일' 줄과 첫 낱말이 이미 정하고, 없으면 코드가 채운다(ask_fallback). 읽는 토큰을 줄인다)
# 프롬프트에 싣는 예시 대사 수. 예시를 줄이면 읽는 토큰이 줄어 처음 한 번의 응답이 빨라진다 (22:07 시험: 첫 요청은 읽기가 응답 시간의 대부분). C# PromptAssembler.PromptExamples 와 같다
PROMPT_EXAMPLES = 2
# 작은 모델은 예시 대사와 사용자 메시지의 페르소나 칸·상황 문장을 그대로 옮겨, 같은 상황마다 같은 대사를 냈다
RULE_EXAMPLES = "- 예시와 자료는 참고만 하고, 지금 상황에 맞게 새로 말한다."
# 기억이 있을 때만 사용자 메시지의 대사 줄에 붙인다
RULE_NO_REPEAT = "'기억'의 일은 새 말로 떠올린다."

RULE_CHOOSE = "반드시 후보 중 하나의 행동과, 그 행동에 적힌 대상 중 하나를 고른다. 후보에 없는 것은 만들지 않는다."
RULE_CHOOSE_ACTION = "반드시 후보 중 하나의 행동을 고른다. 후보에 없는 것은 만들지 않는다."
RULE_JSON = "JSON 한 개만 출력한다. 설명이나 다른 글은 쓰지 않는다."

# 키-값으로 실을 때 영어 키 대신 쓰는 이름. 영어 키만 주면 작은 모델이 방문 횟수 같은 사실을 놓친다.
FACT_LABELS = {
    "time_of_day": "지금 시간대",
    "visit_days": "최근 방문한 날",
    "visit_count_30d": "최근 30일 동안 온 횟수",
    "awakening": "각성 단계",
    "clarity": "몸 상태",
    "last_care_days_ago": "마지막 관리(며칠 전)",
    "care_result": "관리 결과",
}
# 게임과 규칙 기반 생성기만 쓰는 사실. 프롬프트에는 싣지 않는다 (story_index: 몇 번째 실화인지).
# 각성 단계는 누구의 단계인지 몰라 "주인님께서 각성하셨으니"라고 했고(18:03 시험), 말투·대사에 쓰이는 데가 없어 싣지 않는다
# 지금 시간대(time_of_day)는 상황 문장("주인이 아침에 찾아와 너를 탭했다")에 있어 사실 칸에 또 싣지 않는다 — 출력 검사만 쓴다
HIDDEN_FACTS = {"story_index", "awakening", "time_of_day", "visit_count_30d", "care_result"}  # 방문 횟수는 상황 문장에 있다

# 같은 상황이 거듭되면 프롬프트가 글자 하나 다르지 않아, 작은 모델은 반복마다 비슷한 대사를 냈다(2026-11-11 17:29 시험:
# Kanana 구름결 흐려진 안부 4번이 모두 "오래 관리를 못 받아 흐려졌어요."로 시작). 그래서 요청마다 도깨비의 곁들일 거리
# (personas.json 의 flavors)를 하나씩 돌려 싣는다. 게임은 컨텍스트 구성기가 요청마다 몇 번째인지(Variation)를 바꾸고,
# 이 시험기는 반복 번호로 돌린다(1번째 반복 = 0번째 거리)
# "또렷해진 구름 무늬가 고마워요"처럼 거리를 고마움의 대상에 끼워 넣거나 길게 늘여 붙여서(18:03 시험), 그것에 대한 느낌을
# 한마디만, 어울릴 때만 곁들이게 한다
FLAVOR_LABEL = "떠올린 것"
# "어울릴 때만"으로 두었더니 거리를 건너뛰어 같은 말이 반복됐다(18:46 시험: 구름결 이틀 전 관리 4번 중 3번이 반복). 한마디는 넣게 한다
# "한마디 넣는다"로 두었더니 거리를 둘째 문장에 붙여, 첫 문장은 늘 상황 문장을 옮긴 말("이틀 전에 닦아 주셔서 고마워요")이었고
# 40자로 자르면 거리가 잘려 나가 같은 대사만 남았다(20:02 시험: S04 4번 중 2번이 반복으로 기본 대사, S03·S07·S08 도 같은 말로 시작).
# 바뀌는 거리로 말을 시작하게 해 대사 앞부분이 요청마다 달라지게 한다
FLAVOR_HINT = "(내 이야기)"   # "이 느낌을 곁들인다"는 "말 그림 보러 왔구나?"처럼 거리를 일어난 일로 읽었다 (2026-11-14 시험)
# 첫 낱말: 관리 감사가 거의 모두 "관리해 줘서 고마워"로 같았다(2026-11-13 세 번째 시험: 같은 시나리오 4번 중 3~4번, 두 번 다시 만들어도 같아서 기본 대사로 넘어갔다).
# 프롬프트가 요청마다 거의 같으면 작은 모델은 같은 말을 내므로, 요청마다 다른 첫 낱말을 하나 정해 준다. 곁들일 거리와 같은 번호(variation)로 돌린다.
# C# PromptAssembler.Starter 와 같다
STARTER_LABEL = "첫 낱말"
STARTERS = {
    # 2026-11-14 시험 4: "불러줘서"는 "발굴자라 불러줘서 반가워"(부른 쪽이 뒤바뀜), "잠깐"은 "잠깐 전 관리를 못 받아서", "한결"은 "한결 같아져서 고마워"로 이어져 뺐다
    # 2026-11-14 시험 3: "어서"·"여기"는 "어서 안녕?"·"여기, 안녕!", "기분이"는 "기분이 조용해지는 것 같아", "역시"는 "덕분에 역시 기뻐"·"역나", "반갑게"는 "반갑게 인사하며"로 이어져 뺐다
    # 2026-11-14 시험: "잘"은 "잘 왔지? 다음엔 꼭 같이 가자!"처럼 없는 약속을 지어냈고, "오늘은"은 "오늘은 관리해 줘서 고마워"처럼 어색했다
    # 2026-11-17 시험: "찾아와서"는 "찾아와서 반가워, 오늘 아침부터 부르네."처럼 방문과 부름을 뒤섞었고, "만나서"는 "만나서 반가워"로 거의 모두 같게 시작해 뺐다. "기다렸어"를 더했다
    "tap": ("안녕", "왔구나", "와", "어서", "드디어"),   # 2026-11-17 20:39 시험: "반가워"는 "반가워서 인사…·왔…"로 이어져 금칙어에 걸린 것이 4건이라 "어서"로 바꿨다("어서 왔어"는 말투 다듬기가 "어서 와"로 고친다)   # 2026-11-17 19:43 시험: "기다렸어"는 그것만 대사로 내(S01 #2·S22 #1·S24 #1) 인사말뿐·반복으로 거르고 다시 만들었다. 이어 쓰게 되는 "드디어"(기다린 마음)로 바꿨다
    "faded": ("살살", "괜찮으면", "시간 되면", "조심조심", "이왕이면"),   # 2026-11-17 21:45 시험 뒤: 흐려진 안부는 '이미 한 말'(상태·며칠 전) 뒤에 이어질 부탁의 말머리   # 2026-11-17 18:21 시험: "점점"은 "점점 새로워지고 싶어", "혹시"는 "혹시, 오랜만에 깨워줘"처럼 뜬금없는 말로 이어져 "어느새"·"자꾸"로 바꿨다. "조금"은 "조금 전 관리를 못 받아서"(금칙어)로 이어져 뺐다 (2026-11-17 시험 S03 #3)
    # 2026-11-17 20:39 시험 뒤: 흐려진 안부는 요청마다 사실을 하나만 말한다. "마지막 관리가 며칠 전이었는지"를 말하는 요청(ask_variant == "days")의 첫 낱말
    "faded_days": ("마지막 관리가", "마지막", "벌써", "어느새"),   # "마지막으로"는 "마지막으로 닦아주면 감사할 것 같아"(뜻이 뒤섞임)로 이어져 뺐다 (2026-11-17 21:09 시험 S03 #2)
    "clear": ("덕분에", "정말", "훨씬", "몸이", "개운하게", "깨끗하게"),   # 2026-11-17 21:27 시험: "이제"는 "이제 관리해 줘서 고마워"(때가 뒤섞임)로 이어져 "정말"로 바꿨다   # 2026-11-17 18:21 시험: "깨끗해졌어"·"개운해"는 그것만 대사로 내(S04 #1 "깨끗해졌어", S09 #3 "개운해" 인사말뿐) "~하게"로 이어 쓰게 했다. "깨끗해져서"는 "깨끗해져서 닦아줘서 고마워"처럼 '~서'를 거듭 이었다 (2026-11-17 시험 S04 #1)
    "visit": ("안녕", "반가워", "와", "환영해", "마음껏 둘러봐", "천천히 둘러봐"),   # "구경하러"는 "구경하러 온 친구를 반가이 맞아."처럼 상대를 남 얘기하듯 옮겼다 (2026-11-17 18:21 시험 S05 #4)
    "recall": ("자주", "이번에도", "또", "다시", "반가워", "어서"),   # 단골에게는 "오랜만"을 부르는 말을 피한다
}
FLAVOR_KEYS = ("tap", "faded", "clear", "visit")


# 2026-11-17 21:09 시험: "떠올린 것: 구름 무늬" 줄을 읽은 모델이 상황과 상관없는 거리로 말했다("이제 구름 무늬처럼 부드럽게 도윤 안부에 답해 줄게", "이제 구름 무늬를 떠올리니, 오랜만에…").
# 다양성은 첫 낱말이 맡으므로 곁들일 거리 줄은 싣지 않는다 (되돌리려면 True). C# PromptAssembler.FlavorsEnabled 와 같다
FLAVORS_ENABLED = False


def flavor_key(scene):
    if not FLAVORS_ENABLED:
        return None
    return flavor_key_enabled(scene)


def flavor_key_enabled(scene):
    """곁들일 거리 목록의 이름. 탭 인사·손님 관람에는 싣지 않는다(2026-11-14 시험: "반가워서 내 말 그림 줬어"처럼 거리를 일어난 일로 읽었다 — 다양성은 첫 낱말이 맡는다).
    지난 대화 꺼내기도 그 기억 하나에 집중하도록 싣지 않는다(None). C# PromptAssembler.FlavorKey 와 같다."""
    key = starter_key(scene)
    return None if key in ("tap", "visit") else key


def starter_key(scene):
    """첫 낱말 목록의 이름: tap · visit · faded · clear. 지난 대화 꺼내기·끌어당기기는 None. C# PromptAssembler.StarterKey 와 같다."""
    inp = scene["input"]
    if inp in ("tap", "visit"):
        return inp
    if inp == "care":
        return "clear"
    if inp == "ask_status" and any(c["action"] == "answer_status" for c in scene["candidates"]):
        uncared = any(f["key"] == "clarity" and f["value"] in UNCARED_CLARITY for f in scene["facts"])
        return "faded" if uncared else "clear"
    return None


def starter_word(scene, variation):
    """이번 요청의 첫 낱말. 곁들일 거리가 없는 장면(지난 대화 꺼내기)은 None."""
    key = starter_key(scene)
    if key is None:
        return None
    if key == "visit" and any(c["action"] == "wander_recall" for c in scene["candidates"]):
        key = "recall"
    words = STARTERS[key]
    return words[(variation + scene["relicId"]) % len(words)]


def flavor_line(scene, persona, variation):
    if ask_variant(scene, variation) == "days":
        return None  # 며칠 전만 말하는 요청에 '흐려진 몸'류 거리를 실으면 상태를 섞어 말한다
    items = (persona.get("flavors") or {}).get(flavor_key(scene)) or []
    return f"{FLAVOR_LABEL}: {items[variation % len(items)]} {FLAVOR_HINT}" if items else None


def is_fixed(scene):
    """AI 를 부르지 않는 장면인지: 정해진 대사를 쓰는 이야기 듣기, 대사가 없는 끌려갈 때. C# NpcActions.SkipsModel 과 같다."""
    return all(c["action"] in FIXED_ACTIONS or c["action"] in SILENT_ACTIONS for c in scene["candidates"])


def shared_system_head(with_examples):
    """시스템 메시지의 앞부분: 모든 도깨비에 똑같은 소개와 규칙. C# PromptAssembler.SharedSystemHead 와 같다.

    llama.cpp 는 앞 프롬프트와 글자 하나까지 같은 앞부분을 다시 읽지 않는다. 도깨비마다 다른 것(정체·성격·말투·예시)을 뒤에 두면
    다른 도깨비로 넘어가도 이 부분은 다시 읽지 않는다 (2026-11-24 기기 시험: 도깨비가 바뀔 때마다 시스템 메시지를 처음부터 읽는 데 15~20초).
    """
    rules = [RULE_BOND, RULE_FACTS, RULE_ROLE, RULE_LINE, RULE_SPEECH, RULE_ORDER, RULE_VARY]
    if with_examples:
        rules.append(RULE_EXAMPLES)
    return "\n".join(["너는 모바일 게임 '잠깨비'의 도깨비다.", "", "규칙:", *rules])


def system_block(p, with_examples):
    """도깨비마다 고정한 시스템 메시지. 상대·선택지·상황에 따라 바뀌는 것은 넣지 않는다.

    llama.cpp 는 프롬프트 앞부분이 글자 하나까지 같으면 이미 읽은 것을 다시 쓴다. 같은 도깨비와 이어 말할 때
    시스템 메시지를 다시 읽지 않도록, 바뀌는 것(상대 태도, 행동에 맞는 칸, 대사 길이, 기억 규칙)은 사용자 메시지에 둔다.
    모든 도깨비에 같은 규칙은 맨 앞(shared_system_head)에 두어, 다른 도깨비로 넘어갈 때도 그 부분은 다시 읽지 않게 한다.
    """
    lines = [
        shared_system_head(with_examples),
        "",
        # 이름은 싣지 않는다: 이름을 읽은 모델이 "훨씬 선명해진 향산이,"·"향산이가 인사해"처럼 자기 이름을 말했다 (자기 이름은 검사가 지운다). 성격·말씨로 도깨비가 갈린다. C# PromptAssembler.SystemFor 와 같다
        f"너는 {p['origin']}에 깃든 정령이다.",
        f"성격: {p['traits']}",
    ]
    # '감정 표현' 줄은 싣지 않는다 (2026-11-17 21:45 시험): "기쁘면 말 앞에 '어흠'을 붙인다"를 읽은 모델이 "다시 와 줘서 어흠, 고마워!"·"오늘 또 보니 어흠."처럼 말 가운데에 끼웠고,
    # "기쁘면 헛기침을 한다"류는 몸짓을 서술했다("폴짝폴짝 뛰는구나"). 말씨는 '말투' 줄과 예시가 맡는다. C# PromptAssembler.SystemFor 와 같다
    lines.append(f"말투: {p['speechStyle']}")
    if with_examples and p.get("examples"):
        lines.append("대사 예시:")
        lines += [f"- {e}" for e in p["examples"][:PROMPT_EXAMPLES]]
    return "\n".join(lines)


STRANGER = "발굴자"  # 친밀하지 않은 상대를 부르는 호칭 (C# NpcAddress.Stranger)
LEVEL_UP_CARE_COUNTS = (3, 7, 12, 20)  # 주인의 누적 관리 성공 횟수가 이만큼 이상이면 친밀도가 한 단계씩 오른다 (C# NpcAddress.LevelUpCareCounts, 게임의 IntimacyPolicy 기본값)
LEVEL_UP_VISITS = (3, 7, 12, 20)  # 손님이 최근 30일에 방문한 날 수가 이만큼 이상이면 호칭 단계가 한 단계씩 오른다 (C# NpcAddress.LevelUpVisits)
NICKNAME_LEVEL = 3  # 이 단계(3단계 = 주인은 관리 12번, 손님은 30일 방문 12번) 이상이면 닉네임으로 부른다 (C# NpcAddress.NicknameLevel). 0~2단계는 '발굴자'


def intimacy_of_care(care_count):
    """주인의 누적 관리 성공 횟수에서 친밀도 0~4단계를 구한다. 모르면 0. C# NpcAddress.IntimacyOf 와 같다."""
    if care_count is None:
        return 0
    return sum(1 for need in LEVEL_UP_CARE_COUNTS if care_count >= need)


def visitor_level_of(visits):
    """손님이 최근 30일에 방문한 날 수(같은 날은 하루)에서 호칭 단계 0~4를 구한다. 기록이 없으면 0. C# NpcAddress.VisitorLevelOf 와 같다."""
    if visits is None:
        return 0
    return sum(1 for need in LEVEL_UP_VISITS if visits >= need)


def intimacy(scene):
    """장면의 친밀도. 'intimacy'(0~4)가 있으면 그것, 없으면 예전 칸 'familiar'(True → 닉네임 단계(3), False → 0단계). C# NpcTestScenes.IntimacyOf 와 같다."""
    value = scene.get("intimacy")
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        return max(0, min(len(LEVEL_UP_CARE_COUNTS), int(value)))
    return NICKNAME_LEVEL if scene.get("familiar") is True else 0


def address_name(scene):
    """프롬프트에 싣고 출력 검사가 지키는 호칭: 친밀하고(3단계 이상) 닉네임이 있으면 닉네임, 아니면 '발굴자'. C# ReactionRequest.AddressName 과 같다."""
    nickname = scene.get("nickname")
    return nickname if intimacy(scene) >= NICKNAME_LEVEL and nickname else STRANGER


def partner_lines(p, scene, variation=0):
    """사용자 메시지의 맨 앞: 지금 상대와 그 상대를 대하는 태도, 이번 후보 행동에 맞는 페르소나 칸.

    같은 도깨비·같은 상대면 이 부분도 앞 요청과 같아 다시 읽지 않는 범위가 늘어난다.
    두 태도를 다 실으면 작은 모델이 주인에게 손님 태도로 말해서 지금 상대의 것만 싣는다.
    """
    # 호칭은 친밀도 3단계(주인은 관리 12번, 손님은 30일에 12번 이상 방문)부터 닉네임, 아니면 '발굴자'. 꼭 부르지 않아도 된다 (모든 대사에 넣으면 부담스럽다)
    # 닉네임·'발굴자'를 대사에 꼭 넣을 필요는 없지만 가끔은 나와야 정이 든다: 세 번에 한 번(variation 0·3·6…)은 "한 번 불러 준다"고 적고, 나머지는 호칭만 알려 준다.
    # "도윤님"처럼 '님'을 붙이면 출력 직전에 뗀다 (strip_nim). C# PromptAssembler.PartnerLines 와 같다
    address = f"호칭: {address_name(scene)}" + ("(한 번 불러 준다)" if variation % 3 == 0 else "")
    if scene["mode"] == "owner":
        # 2026-11-17 21:27 시험: 호칭이 프롬프트에 네 번(지금 상대·호칭·대할 때·상황) 나와 "발굴자가 오래 걸리지 않네"처럼 호칭을 주어로 엉뚱한 말을 이었다.
        # 호칭 줄과 '대할 때' 줄의 호칭을 합쳐 두 번(지금 상대·상황)으로 줄인다 (읽는 토큰도 준다). C# PromptAssembler.PartnerLines 와 같다
        out = [f"지금 상대: {address_name(scene)}" + ("(한 번 불러 준다)" if variation % 3 == 0 else ""), f"대하는 태도: {p['toOwner']}"]
    else:
        out = ["지금 상대: 친구(구경만 한다)", address, f"친구를 대할 때: {p['toVisitor']}"]
    wanted = {key for c in scene["candidates"] for key in ACTION_FIELDS.get(c["action"], [])}
    if any(f["key"] == "clarity" and f["value"] in UNCARED_CLARITY for f in scene["facts"]):
        wanted.discard("onCare")
    out += [f"{label}: {p[key]}" for key, label in PERSONA_FIELDS if key in wanted and p.get(key)]
    return out


def line_rule(args, scene, persona, with_memory=True, variation=0):
    """대사 줄. 문장 수는 장면의 maxSentences, 없으면 --max-sentences(기본 0 = 적지 않음).
    모든 대사에 '2문장 이내'를 붙여 봤지만 Mi:dm 은 그대로(문장자름 3%)였고 Kanana 는 두 문장을 길게 써서 더 잘렸다(15% → 46%).

    기억 규칙은 '기억' 칸을 가리키므로, 그 칸을 싣지 않으면(no_memory) 규칙도 뺀다.
    한국어로만 쓰라는 규칙은 장면과 상관없어 시스템 메시지(RULE_LINE)에 둔다.
    """
    sentences = scene.get("maxSentences", args.max_sentences)
    # 글자 수만 적으면 그 길이를 채우려고 말을 덧붙여서, 짧아도 된다고 함께 적는다. 다만 15자 이하(탭 인사·손님 관람)에는 적지 않는다 —
    # "짧게 말한다"·"(짧아도 된다)"를 읽은 모델이 첫 낱말만 대사로 냈다("기다렸어", "왔구나", "와, 도윤!" — 2026-11-17 시험 S01·S22~S24 에서 인사말뿐·반복으로 거르고 다시 만들었다)
    chars = line_chars(args, scene)
    ask = needs_ask(scene)
    if ask:
        chars = min(chars, ASK_LINE_CHARS)  # 모델은 이어지는 부탁 한 문장만 쓴다
    rule = f"대사: 한국어 반말, {chars}자 이내" + (f", {sentences}문장 이내" if sentences else "") + ("" if chars <= SHORT_LINE_CHARS else "(짧아도 된다)")
    # 첫 낱말을 줄 바로 옆에서 다시 말해 준다: 시스템 규칙과 '첫 낱말' 줄만으로는 모델이 따르지 않아 감사가 "관리해 줘서 고마워"로 거듭 같았다(S07·S09·S13·S25 반복).
    # 15자 이하(탭 인사·손님 관람)는 첫 낱말에서 멈추지 않게 "이어 말한다"고 적는다 ("기다렸어"·"안녕"만 낸 대사가 인사말뿐으로 걸렸다). "마음을 전하는 한 문장"은
    # "반가워서 불렀어"·"반가워서 뛰어나왔어"처럼 '반가워서 ~'로 잇게 해서 뺐다 (2026-11-17 19:43 시험 S22)
    # 2026-11-17 20:39 시험: 두 문장("자꾸 흐려져서 몸이 무거워. 깨끗해질 수 있게 닦아 줄래?", "많이 흐려졌어. 마지막 관리는 9일 전이었어. 닦아줘.")을 쓰며 길어져 시간·반복·길이 초과로 다시 만들었다.
    # 상황에 맞는 한 문장이면 충분하다: 대사 줄에 "한 문장"을 적는다
    if starter_key(scene) is not None:
        # 15자 이하는 첫 낱말 뒤를 마음대로 잇게 하면 엉뚱한 말이 붙어("발굴자가 오래 걸리지 않네") 이을 방향을 적는다
        rule += ", 첫 낱말로 시작해 반가운 마음을 말한다" if chars <= SHORT_LINE_CHARS and not ask else ", 첫 낱말로 시작" if ask else ", 첫 낱말로 시작하는 한 문장"
    # 흐려진 안부 9건 중 4건이 닦아 달라는 부탁을 빼먹었다(MissingAsk). 그래서 끝을 "닦아 줘"로 맺게 했더니 "점점 새로워지고 싶어. 닦아 줘.", "혹시, 오랜만에 깨워줘 닦아 줘"처럼
    # 뜬금없거나 토막 난 말이 나왔다(2026-11-17 18:21 시험). 흐려진 느낌을 먼저 말하고 부드러운 물음("닦아 줄래?")으로 부탁하게 한다 — 기본 대사("몸이 흐려졌어. 닦아 줄래?")와 같은 꼴이다
    # 19:43 시험: "'닦아 줄래?'로 부탁한다"고 적자 모든 흐려진 안부가 "…닦아 줄래?"로 끝나 같아 보였다. 끝말을 정해 주지 않고 바라는 마음만 말하게 한다(닦아 달라는 말이
    # 들어가기만 하면 출력 검사 has_ask 가 통과시킨다)
    if ask:
        rule += ", 부탁 한 문장"
    # 말투 다짐 줄(speechReminder)은 공통 규칙 RULE_SPEECH 과 겹쳐 뺐다 (2026-11-13)
    if with_memory and scene.get("memory"):
        rule += ". " + RULE_NO_REPEAT
    return rule


# 인사 한마디의 글자 수 목표 (C# PromptOptions.LineChars 와 같다). 탭 인사·손님 관람은 15자: 20자로 적었더니 모델이 중앙값 25자를 써서 응답이 2초를 넘었다
INPUT_LINE_CHARS = {"tap": 15, "visit": 15}
SHORT_LINE_CHARS = 15  # 이 글자 수 이하면 대사 줄에 "(짧아도 된다)"를 적지 않는다 (C# PromptAssembler.ShortLineChars)


def line_chars(args, scene):
    return scene.get("lineChars", INPUT_LINE_CHARS.get(scene.get("input"), args.line_chars))


def max_chars(args, scene):
    return scene.get("maxChars", args.max_chars)


def target_label(t, scene, labels):
    if t == "player":
        return "주인" if scene["mode"] == "owner" else "친구"
    if t == "none":
        return "대상 없음"
    return labels.get(t, t)


def fact_value(v):
    if isinstance(v, bool):
        return "true" if v else "false"
    return str(v)


# 탭 상황 문장의 시간대("주인이 아침에 찾아와 너를 불렀다")는 세 번에 한 번(variation 0·3·6…)만 싣는다. 늘 싣자 탭 인사 12건 중 9건이 "오늘 아침부터 …"로 시작했고
# ("오늘 아침부터 왜 부르는지 궁금하네", "반가워, 오늘 아침부터 부르시니 정말 반가워" — 2026-11-16 시험 S01·S22~S24) 반복·어색한 말의 가장 큰 원인이었다. C# PromptAssembler.EventText 와 같다
TAP_TIME_RE = re.compile("(?:새벽|아침|오전|낮|저녁|밤)에 (?=너를)")


def event_text(scene, variation=0):
    event = scene["event"]
    if scene.get("input") == "tap" and variation % 3 != 0:
        return TAP_TIME_RE.sub("", event)
    if ask_variant(scene, variation):
        return "주인이 너를 보러 찾아왔다."  # (22:07 시험: "안부를 물었다"를 읽은 모델이 "혹시 안부를 물어봐줘?"로 되받았다) 사실은 '이미 한 말'(ask_prefix)로 코드가 말한다 — 상황 문장에 두면 모델이 사실을 다시 풀어 쓴다
    return event


# 흐려진 안부의 '이미 한 말'(2026-11-17 21:45 시험 뒤): 사실 문장은 서버 값으로 코드가 만들어 대사 앞에 두고, 모델은 이어서 부탁하는 한 문장만 쓴다.
# 사실을 모델에 맡기자 "마지막 관리가 16일 전이라서, 이제는 스스로를 닦아야 해"처럼 없는 설정을 지어냈다. 요청마다 번갈아(variation 짝수 = 상태, 홀수 = N일 전) 한 사실만 말한다.
# C# PromptAssembler.LinePrefix 와 같다
ASK_PREFIXES = {
    "days": ("마지막 관리가 {n}일 전이었어.", "관리받은 지 {n}일이 지났어."),
    "faded": ("몸이 흐려졌어.", "몸이 많이 흐려졌어."),
    "resealed": ("몸이 흐려지다 못해 봉인됐어.", "다시 봉인될 만큼 흐려졌어."),
    "partly": ("아직 몸이 좀 흐려.", "아직 다 맑아지지 않았어."),  # 재봉인에서 한 칸 올라 흐려짐 (partly_restored_ask)
}
ASK_LINE_CHARS = 12  # 모델이 쓰는 부탁 문장의 글자 수 목표

# 모델이 부탁을 빼먹거나("혹시 안부를 물어봐줘?"·"스스로 닦아야 해") 사실을 또 말하면, 거르고 다시 만들지 않고 코드가 부탁 문장을 채운다 (2026-11-17 22:07 시험:
# 흐려진 안부의 부탁 없음이 시간 초과·기본 대사로 이어졌다). 사실 문장은 이미 코드가 말했으므로 이 문장만 붙이면 대사가 완성된다. C# PromptAssembler.AskFallback 과 같다
ASK_FALLBACKS = ("살살 {v} 줄래?", "시간 되면 {v} 줄래?", "조심조심 {v} 줘.", "괜찮으면 {v} 줄래?", "이왕이면 {v} 줘.")


def ask_fallback(scene, variation):
    verb = "깨워" if fact_of(scene, "clarity") in ("재봉인", "RESEALED") else "닦아"
    return ASK_FALLBACKS[(variation + scene["relicId"]) % len(ASK_FALLBACKS)].format(v=verb)


def ask_prefix(scene, variation):
    """흐려진 안부에서 코드가 정한 앞 문장. 흐려진 안부가 아니면 None."""
    variant = ask_variant(scene, variation)
    if variant is None:
        return None
    pick = (variation // 2) % 2
    if variant == "days":
        return ASK_PREFIXES["days"][pick].format(n=fact_of(scene, "last_care_days_ago"))
    if partly_restored_ask(scene):
        return ASK_PREFIXES["partly"][pick]
    return ASK_PREFIXES["resealed" if fact_of(scene, "clarity") in ("재봉인", "RESEALED") else "faded"][pick]


LONG_GAP_DAYS = 7  # C# NpcContextBuilder.LongGapDays


def partly_restored_ask(scene):
    """재봉인에서 한 칸 올라 흐려짐이 된 정령의 안부인지 (사실 칸이 흐려짐이고 마지막 관리가 7일 안). 방금 관리받았으므로 아직 덜 나았다고 말한다.
    C# PromptAssembler.PartlyRestoredAsk 와 같다."""
    if not needs_ask(scene) or fact_of(scene, "clarity") not in ("흐려짐", "FADED"):
        return False
    days = fact_of(scene, "last_care_days_ago")
    return days is not None and days.isdigit() and int(days) < LONG_GAP_DAYS


def ask_prefix_candidates(scene):
    """이 장면이 쓸 수 있는 '이미 한 말' 전부 (최근 대사에서 앞 문장을 떼어 부탁 부분끼리 견준다). 흐려진 안부가 아니면 빈 목록. C# PromptAssembler.AskPrefixes 와 같다."""
    if not needs_ask(scene):
        return []
    out = []
    n = fact_of(scene, "last_care_days_ago")
    if n is not None:
        out += [t.format(n=n) for t in ASK_PREFIXES["days"]]
    out += list(ASK_PREFIXES["faded"]) + list(ASK_PREFIXES["resealed"]) + list(ASK_PREFIXES["partly"])
    return out


def strip_ask_prefix(line, prefixes):
    for prefix in prefixes:
        if (line or "").startswith(prefix):
            return line[len(prefix):].strip()
    return line


# 흐려진 안부의 '이미 한 말' 꼴 전부 — 날 수는 어느 값이든, 칸('{날수}')이어도 맞는다. C# PromptAssembler.AnyAskPrefix 와 같다
ANY_ASK_PREFIX_RE = re.compile(r"^(?:마지막 관리가 (?:[0-9]+|\{날수\})일 전이었어\.|관리받은 지 (?:[0-9]+|\{날수\})일이 지났어\.|몸이 흐려졌어\.|몸이 많이 흐려졌어\."
                               r"|몸이 흐려지다 못해 봉인됐어\.|다시 봉인될 만큼 흐려졌어\."
                               r"|아직 몸이 좀 흐려\.|아직 다 맑아지지 않았어\.)\s*")
ASK_DAYS_RE = re.compile(r"(?:[0-9]+|\{날수\})일")


def ask_prefix_shape(line):
    """흐려진 안부 대사의 (앞 문장 꼴, 뒤의 부탁 부분). 날 수는 '{날수}일'로 바꿔 꼴을 견준다. 앞 문장이 없으면 (None, 대사). C# PromptAssembler.AskPrefixShape 와 같다."""
    m = ANY_ASK_PREFIX_RE.match(line or "")
    if not m:
        return None, line
    return ASK_DAYS_RE.sub("{날수}일", m.group(0).strip()), line[m.end():]


def similar_line(a, b):
    """두 대사가 같은 대사인지. 흐려진 안부 대사는 앞 문장까지 포함한 전체 대사로 본다: 앞 문장 꼴이 같으면 부탁 부분끼리,
    다르거나 한쪽에만 있으면 전체끼리 견준다 (2026-11-24). C# PromptAssembler.SimilarLine 과 같다."""
    shape_a, rest_a = ask_prefix_shape(a)
    shape_b, rest_b = ask_prefix_shape(b)
    return similar(rest_a, rest_b) if shape_a is not None and shape_a == shape_b else similar(a, b)


def find_repeat(line, recent):
    """최근 대사 중 같은 대사(similar_line)가 있으면 (그 대사, 이유), 없으면 None. C# PromptAssembler.FindRepeat 와 같다."""
    for other in recent or []:
        why = similar_line(line, other)
        if why:
            return other, why
    return None


def ask_task(scene, variation):
    """흐려진 안부의 '할 일' 줄. 재봉인은 깨워 달라고, 흐려짐은 닦아 달라고 이어 부탁한다."""
    if ask_variant(scene, variation) is None:
        return None
    verb = "깨워" if fact_of(scene, "clarity") in ("재봉인", "RESEALED") else "마저 닦아" if partly_restored_ask(scene) else "닦아"
    return f"이어서 {verb} 달라고 부탁하는 말을 한다"


# (이전 기록) 흐려진 안부는 사실을 하나만 말하게 한다 (2026-11-17 20:39 시험 뒤). "마지막 관리가 9일 전이었어. 닦아 줄래?"에 "흐려져서"까지 넣으면("흐려진 몸이지만, 마지막 관리가 9일 전이라
# 오늘따라 더 흐릿해졌어") 어색해진다. 요청마다 번갈아(variation 짝수 = 상태, 홀수 = 며칠 전) 한 사실만 프롬프트에 싣는다: 상태 요청은 마지막 관리 사실을 빼고,
# 며칠 전 요청은 상황 문장을 "마지막 관리를 받은 지 N일이 지났다"로 바꾸고 상태 사실·곁들일 거리를 뺀다. 출력 검사(needs_ask)는 사실 칸이 그대로라 똑같이 본다.
# 마지막 관리 사실이 없으면 늘 상태 요청. C# PromptAssembler.AskVariant 와 같다
ASK_SENTENCE_RE = re.compile("주인에게 [^.]*달라고 부탁한다\\.")


def ask_variant(scene, variation):
    """흐려진 안부 요청이 말할 사실: "state"(몸이 흐려졌다) · "days"(마지막 관리가 N일 전). 흐려진 안부가 아니면 None."""
    if not needs_ask(scene):
        return None
    # 한 칸 올라 흐려짐이 된 정령은 며칠 전 문장을 쓰지 않는다 ("마지막 관리가 0일 전이었어"는 방치된 것처럼 들린다)
    return "days" if variation % 2 == 1 and fact_of(scene, "last_care_days_ago") is not None and not partly_restored_ask(scene) else "state"


def situation_lines(scene, content, facts_as_sentence, with_memory, variation=0):
    # '고른 선택지: 탭(교류 시작)' 줄은 상황 문장과 겹치고, Mi:dm 이 그것을 'tap' 으로 옮겨 행동 칸에 적었다(S01 2/3). 싣지 않는다
    call = address_name(scene) if scene["mode"] == "owner" else None
    out = [f"상황: {owner_to_call(event_text(scene, variation), call)}"]
    prefix = ask_prefix(scene, variation)
    if prefix:
        out.append(f"이미 한 말: {prefix}")
    variant = ask_variant(scene, variation)
    hidden = HIDDEN_FACTS | ({"last_care_days_ago", "clarity"} if variant else set())  # 상황 문장에 이미 있어 둘 다 뺀다 (읽는 토큰 감소)
    facts = [f for f in scene["facts"] if f["key"] not in hidden]
    if facts:
        if facts_as_sentence:
            out.append("알고 있는 것: " + owner_to_call(" ".join(f["text"].rstrip(".") + "." for f in facts), call))
        else:
            out.append("알고 있는 것:")
            out += [f"- {FACT_LABELS.get(f['key'], f['key'])}: {fact_value(f['value'])}" for f in facts]
    if with_memory and scene.get("memory"):
        out.append("기억:")
        for m in scene["memory"]:
            when = "오늘" if m["daysAgo"] == 0 else f"{m['daysAgo']}일 전"
            # 요약이 있으면 대사 원문은 싣지 않는다. 원문을 주면 작은 모델이 그대로 다시 읊는다.
            if m.get("summary"):
                out.append(f"- {when}: {owner_to_call(m['summary'], call)}")
            else:
                out.append(f'- {when}: {meaning(content, m["action"])} — "{m["line"]}"')
    return out


def candidates_block(scene, content, describe_format, shape="full", variation=0):
    """후보 행동과 대상. ID와 뜻을 따로 보여 준다.

    'player(주인)'처럼 붙여 쓰면, 형식 제약이 없을 때 모델이 그 모양 그대로 대상 칸에 적는다.
    행동이 하나뿐(shape="line")이면 고를 것이 없어 후보 목록 대신 '할 일' 한 줄만 싣는다 (행동 ID 도 대상 줄도 없다) — 읽는 토큰을 줄인다.
    """
    if shape == "line":
        return f"할 일: {ask_task(scene, variation) or meaning(content, scene['candidates'][0]['action'])}"
    if shape == "full":
        out = [CANDIDATES_HEAD_FREE if describe_format else CANDIDATES_HEAD]
    else:
        out = [CANDIDATES_HEAD_ACTION if describe_format else CANDIDATES_HEAD]
    targets = list(dict.fromkeys(t for c in scene["candidates"] for t in c["targets"]))
    for c in scene["candidates"]:
        # 대상이 하나뿐이라 적지 않는 모양(shape)이면 대상 칸과 '대상 ID 뜻' 줄도 싣지 않는다 (상대는 '지금 상대' 줄에 있다)
        out.append(f"- {c['action']}: {meaning(content, c['action'])}" + (f" / 대상: {', '.join(c['targets'])}" if shape == "full" else ""))
    if shape == "full":
        out.append("대상 ID 뜻: " + ", ".join(f"{t} = {target_label(t, scene, content['labels'])}" for t in targets))
    return "\n".join(out)


def schema_for_shape(scene, shape):
    """모델이 적을 칸에 맞춘 JSON 스키마: "line" 은 line 만, "action_line" 은 action+line, "full" 은 전부. C# PromptAssembler.Schema 와 같다."""
    if shape == "line":
        return SCHEMA_LINE
    if shape == "action_line":
        acts = list(dict.fromkeys(c["action"] for c in scene["candidates"]))
        return {"type": "object", "properties": {"action": {"type": "string", "enum": acts}, "line": {"type": "string"}}, "required": ["action", "line"]}
    return schema_choice(scene, with_line=True)


def schema_choice(scene, with_line):
    acts = list(dict.fromkeys(c["action"] for c in scene["candidates"]))
    targets = list(dict.fromkeys(t for c in scene["candidates"] for t in c["targets"]))
    props = {
        "action": {"type": "string", "enum": acts},
        "target": {"type": "string", "enum": targets},
    }
    if with_line:
        props["line"] = {"type": "string"}
    return {"type": "object", "properties": props, "required": list(props)}


SCHEMA_LINE = {"type": "object", "properties": {"line": {"type": "string"}}, "required": ["line"]}


def output_shape(scene):
    """모델이 적을 칸. 행동·대상이 하나뿐이면 "line", 대상만 하나뿐이면 "action_line", 아니면 "full". C# PromptAssembler.OutputShape 와 같다."""
    actions = {c["action"] for c in scene["candidates"]}
    targets = {t for c in scene["candidates"] for t in c["targets"]}
    if len(targets) != 1:
        return "full"
    return "line" if len(actions) == 1 else "action_line"


def format_block(format_line, rule_choose=RULE_CHOOSE):
    """형식 제약이 없을 때 사용자 메시지 끝에 붙이는 형식 설명. 고를 것이 없으면(rule_choose=None) 고르라는 규칙은 뺀다."""
    if rule_choose is None:
        return f"\n\n{RULE_JSON}\n{format_line}"
    return f"\n\n{rule_choose}\n{RULE_JSON}\n{format_line}"


def build_prompts(args, variant, scene, persona, content, variation=0):
    """(시스템, 사용자, 스키마, 상황) 을 돌려준다. two_step 의 둘째 단계는 첫 결과가 있어야 만든다.

    시스템 메시지는 도깨비마다 고정이다(방식이 같으면 장면이 달라도 같다). 형식 설명은 형식 제약을 끈
    --no-format 일 때만 싣는다. 기기(GBNF)와 LM Studio(JSON 스키마)는 형식을 강제하므로 설명이 필요 없다.
    variation 은 몇 번째 곁들일 거리를 실을지다 (반복 번호 - 1).
    """
    with_memory = variant != "no_memory"
    with_examples = variant != "no_examples"
    describe = not getattr(args, "format", True)
    system = system_block(persona, with_examples)
    flavor = flavor_line(scene, persona, variation)
    starter = starter_word(scene, variation)
    situation = "\n".join(partner_lines(persona, scene, variation)
                          + situation_lines(scene, content, facts_as_sentence=variant == "facts_sentence",
                                            with_memory=with_memory, variation=variation)
                          + ([flavor] if flavor else [])
                          + ([f"{STARTER_LABEL}: {starter}"] if starter else []))
    if variant == "two_step":
        candidates = candidates_block(scene, content, describe)
        user = f"{situation}\n\n{candidates}" + (format_block(FORMAT_CHOICE) if describe else "")
        return system, user, schema_choice(scene, with_line=False), situation
    # 형식 제약(JSON 스키마·GBNF)이 있어도 모델이 적을 칸을 줄인다 — 응답 시간은 쓰는 토큰 수에 비례하고 `{"action": "greet", "target": "player", "line": "` 가
    # 약 17토큰, `{"line": "` 이 약 5토큰이다. 빠진 칸은 evaluate 가 후보로 채운다
    shape = output_shape(scene)
    candidates = candidates_block(scene, content, describe, shape, variation)
    fmt = {"line": lambda: format_block(FORMAT_LINE, None), "action_line": lambda: format_block(FORMAT_ACTION_LINE, RULE_CHOOSE_ACTION),
           "full": lambda: format_block(FORMAT_SINGLE)}[shape]
    user = (f"{situation}\n\n{candidates}\n{line_rule(args, scene, persona, with_memory, variation)}"
            + (fmt() if describe else ""))
    return system, user, schema_for_shape(scene, shape), situation


def build_line_prompt(args, scene, persona, situation, action_meaning, label):
    system = system_block(persona, True)
    user = (f"{situation}\n\n너는 방금 이 행동을 하기로 했다: {action_meaning} (대상: {label})\n"
            f"이 행동의 의미는 바꾸지 않고, 어울리는 대사를 한다.\n{line_rule(args, scene, persona)}")
    if not getattr(args, "format", True):
        user += f"\n\n{RULE_JSON}\n{FORMAT_LINE}"
    return system, user


# ---------- 모델 서버 ----------

def chat(args, model, system, user, schema, seed=None):
    """(응답 본문, 측정값) 을 돌려준다.

    채팅 틀이 시스템 메시지를 빼먹는 모델(args.system_in_user)은 시스템 메시지를 사용자 메시지 앞에 붙여 보낸다.
    글은 시스템 메시지가 앞에 오는 그대로라, 같은 도깨비면 앞부분을 다시 읽지 않는 것도 그대로다.
    """
    if system is not None and model in getattr(args, "system_in_user", ()):
        system, user = None, f"{system}\n\n{user}"
    if args.backend == "lmstudio":
        return chat_lmstudio(args, model, system, user, schema, seed)
    return chat_ollama(args, model, system, user, schema, seed)


def messages_of(system, user):
    head = [] if system is None else [{"role": "system", "content": system}]
    return head + [{"role": "user", "content": user}]


# 시스템 메시지가 들어가는지 볼 때 쓰는 두 글. 긴 쪽은 짧은 쪽 뒤에 수십 토큰을 더 붙였다
SYSTEM_PROBE_SHORT = "짧게 답한다."
SYSTEM_PROBE_LONG = SYSTEM_PROBE_SHORT + " 이 문장은 시스템 메시지가 모델에 들어가는지 보려고 덧붙인 글이다." * 3
SYSTEM_PROBE_MIN_GAP = 10  # 시스템 메시지가 들어가면 두 호출의 입력 토큰이 적어도 이만큼 차이 난다


def drops_system(args, model):
    """LM Studio 가 이 모델의 시스템 메시지를 빼고 넣는지. (빠짐 여부, 짧은 시스템의 입력 토큰, 긴 시스템의 입력 토큰)

    같은 사용자 메시지에 길이가 다른 시스템 메시지를 붙여 두 번 보내고 입력 토큰 수를 비교한다. 들어가면 긴 쪽이 수십 토큰 많고,
    빠지면 같다. 시스템 메시지 '없이' 보내 비교하지 않는 것은, Mi:dm 처럼 시스템 메시지가 없으면 채팅 틀이 기본 시스템 문구를
    넣는 모델이 있어서다. EXAONE 4.0 1.2B 는 시스템 메시지를 통째로 빼먹었다(2026-11-11 시험: 1,181자 프롬프트의 입력 토큰이
    245개, 같은 프롬프트로 Kanana 는 743개). 그래서 말투·'한국어로만' 규칙을 못 보고 영어로 답했다.
    토큰 수를 알려 주지 않으면(0) 판단하지 않는다.
    """
    _, short = chat_lmstudio(args, model, SYSTEM_PROBE_SHORT, "안녕", None, None, max_tokens=1)
    _, long = chat_lmstudio(args, model, SYSTEM_PROBE_LONG, "안녕", None, None, max_tokens=1)
    a, b = short["prompt_tokens"], long["prompt_tokens"]
    return (a > 0 and b - a < SYSTEM_PROBE_MIN_GAP), a, b


def request_json(args, path, body=None):
    """서버에 JSON 을 보내고 JSON 응답을 받는다.

    연결부터 못 하면(서버가 꺼짐) URLError 를 그대로 올려 '서버에 연결하지 못했다'로 안내하게 하고,
    연결한 뒤의 실패(HTTP 오류, 응답을 읽다 시간 초과·연결 끊김, JSON 이 아닌 응답)는 BackendError 로 바꿔
    그 한 건만 실패로 적고 다음 장면으로 넘어가게 한다.
    """
    data = None if body is None else json.dumps(body).encode("utf-8")
    req = urllib.request.Request(f"{args.host}{path}", data=data, headers={"Content-Type": "application/json"})
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=args.timeout) as res:
            raw = res.read()
    except urllib.error.HTTPError as e:
        raise BackendError(f"HTTP {e.code}: {e.read().decode('utf-8', 'replace')[:300]}") from e
    except urllib.error.URLError:
        raise
    except (TimeoutError, ConnectionError, http.client.HTTPException) as e:
        # 응답을 읽는 중의 시간 초과, 서버가 연결을 끊음(ConnectionResetError · RemoteDisconnected) 등
        raise BackendError(f"응답을 읽지 못했다 ({type(e).__name__}): {e}") from e
    try:
        result = json.loads(raw.decode("utf-8"))
    except ValueError as e:  # UnicodeDecodeError · JSONDecodeError
        raise BackendError(f"JSON 이 아닌 응답: {raw[:300].decode('utf-8', 'replace')}") from e
    return result, (time.perf_counter() - started) * 1000


def chat_ollama(args, model, system, user, schema, seed):
    body = {
        "model": model,
        "messages": messages_of(system, user),
        "stream": False,
        "options": {"temperature": args.temperature, "num_predict": args.num_predict},
    }
    if seed is not None:
        body["options"]["seed"] = seed
    if schema is not None and args.format:
        body["format"] = schema
    if args.think_off:
        body["think"] = False
    data, wall_ms = request_json(args, "/api/chat", body)
    metrics = {
        "wall_ms": wall_ms,
        "load_ms": data.get("load_duration", 0) / 1e6,
        "prompt_tokens": data.get("prompt_eval_count", 0),
        "gen_tokens": data.get("eval_count", 0),
        "gen_ms": data.get("eval_duration", 0) / 1e6,
    }
    return data.get("message", {}).get("content", ""), metrics


def chat_lmstudio(args, model, system, user, schema, seed, max_tokens=None):
    """LM Studio 의 OpenAI 호환 API. 생성 시간을 따로 주지 않아 초당토큰은 전체 응답 시간으로 어림한다.

    생각 모드는 기본으로 끈다. 켜 두면 Gemma 4 처럼 출력 한도를 생각에 다 쓰고 대사를 비워 돌려주는
    모델이 있고, 게임에서는 어차피 생각할 시간이 없다. system 이 None 이면 사용자 메시지만 보낸다.
    """
    body = {
        "model": model,
        "messages": messages_of(system, user),
        "stream": False,
        "temperature": args.temperature,
        "max_tokens": args.num_predict if max_tokens is None else max_tokens,
    }
    if seed is not None:
        body["seed"] = seed
    if not args.allow_think:
        body["reasoning_effort"] = "none"
    if schema is not None and args.format:
        body["response_format"] = {
            "type": "json_schema",
            "json_schema": {"name": "reaction", "strict": True, "schema": schema},
        }
    data, wall_ms = request_json(args, "/v1/chat/completions", body)
    usage = data.get("usage") or {}
    message = ((data.get("choices") or [{}])[0]).get("message") or {}
    metrics = {
        "wall_ms": wall_ms,
        "load_ms": 0,
        "prompt_tokens": usage.get("prompt_tokens", 0),
        "gen_tokens": usage.get("completion_tokens", 0),
        "gen_ms": wall_ms,
        "reasoning_tokens": (usage.get("completion_tokens_details") or {}).get("reasoning_tokens", 0),
    }
    return message.get("content") or "", metrics


def list_models(args):
    if args.backend == "lmstudio":
        data, _ = request_json(args, "/v1/models")
        return [m["id"] for m in data.get("data", [])]
    data, _ = request_json(args, "/api/tags")
    return [m["name"] for m in data.get("models", [])]


def add_metrics(a, b):
    return {k: a.get(k, 0) + b.get(k, 0) for k in set(a) | set(b)}


# ---------- 검사 ----------

def strip_think(text):
    return THINK_RE.sub("", text).strip()


def parse_json(text):
    """(JSON 객체 또는 None, 토큰 한도에서 끊겼는지). C# NpcOutputValidator 와 같은 규칙이다.

    출력 토큰 한도(--num-predict)에 걸리면 대사 문자열 안에서 끊긴다. 문자열 안에서 끝났으면 닫아서 읽고,
    끊겼다고 알린다 — 대사는 끝난 문장까지만 쓴다(fit_sentences).
    """
    body = strip_think(text)
    m = OBJECT_RE.search(body)
    if m:
        try:
            obj = json.loads(m.group(0))
            return (obj, False) if isinstance(obj, dict) else (None, False)
        except json.JSONDecodeError:
            pass
    start = body.find("{")
    if start >= 0 and not ends_in_string(body[start:]):
        # 대사 문자열을 닫은 뒤 토큰 한도에서 끊겨 객체의 '}' 만 빠진 경우 ("…고마워요!\"" 로 끝남, 2026-11-14 시험 S08 #4)
        try:
            obj = json.loads(body[start:].rstrip() + "}")
            return (obj, False) if isinstance(obj, dict) else (None, False)
        except json.JSONDecodeError:
            return None, False
    if start < 0:
        return None, False
    try:
        obj = json.loads(body[start:].rstrip("\\") + '"}')
    except json.JSONDecodeError:
        return None, False
    return (obj, True) if isinstance(obj, dict) else (None, False)


def ends_in_string(text):
    """글 끝이 JSON 문자열 안인지 (닫히지 않은 따옴표가 있는지)."""
    inside = escaped = False
    for c in text:
        if escaped:
            escaped = False
        elif c == "\\":
            escaped = inside
        elif c == '"':
            inside = not inside
    return inside


def sentence_ends(line):
    """문장이 끝나는 자리(끝 글자 다음 위치)들. 마침표·느낌표·물음표·물결 뒤에 공백이나 글 끝이 오면 문장 끝이다.
    점 두 개 이상(...)이나 '…'는 말줄임이라 문장 끝으로 보지 않는다."""
    ends = []
    i = 0
    while i < len(line):
        if line[i] not in SENTENCE_END:
            i += 1
            continue
        j = i
        while j < len(line) and line[j] in SENTENCE_END:
            j += 1
        ellipsis = j - i >= 2 and set(line[i:j]) == {"."}
        while j < len(line) and line[j] in SENTENCE_CLOSE:
            j += 1
        if not ellipsis and (j == len(line) or line[j].isspace()):
            ends.append(j)
        i = j
    return ends


def fit_sentences(line, limit, truncated=False):
    """제한 안에 드는 앞 문장들만 남긴다. 첫 문장부터 넘치면 None (TooLong).

    작은 모델은 두세 문장을 써서 길이를 넘기는 일이 많다. 다 버리지 않고 앞 문장만 쓴다.
    토큰 한도에서 끊긴 대사는 마지막의 끝나지 않은 조각을 버린다.
    """
    ends = sentence_ends(line)
    if not truncated and (not ends or ends[-1] < len(line.rstrip())):
        ends.append(len(line))  # 문장 부호 없이 끝난 마지막 문장
    fitted = None
    for end in ends:
        piece = line[:end].rstrip()
        if len(piece) > limit:
            break
        if piece:
            fitted = piece
    return fitted if fitted is not None else fit_clause(line, limit)


# 길이 제한에 두는 여유(글자). 실기기 시험(2026-11-12·14 Tab S9·S25 FE 48건 중 12건이 다시 만듦)에서 "26자(제한 25)"처럼 한두 글자 넘었다고 거르고 다시 만들어
# 한 번에 10~15초가 더 들었다. 말풍선에 한두 글자 더 들어가는 것은 문제가 아니라 제한에 이만큼 여유를 둔다. C# NpcOutputValidator.LengthTolerance 와 같다
LENGTH_TOLERANCE = 5
CLAUSE_MIN_CHARS = 5  # 쉼표로 자른 앞 마디의 최소 글자 수 (부호·띄어쓰기를 뺀 글자). C# NpcOutputValidator.ClauseMinChars
FINAL_ENDINGS = "어아야워여요죠네군지까다래자"
QUOTE_CHARS = "'" + chr(34) + "“”‘’"


def fit_clause(line, limit):
    """첫 문장이 길이 제한을 넘을 때, 제한 안에 드는 가장 긴 앞 마디(쉼표 앞)가 끝난 말이면 그것만 쓴다. 없으면 None (TooLong).

    끝난 말은 끝 글자가 어미(어·아·야·워·여·요·죠·네·군·지·까·다·래·자)이고 CLAUSE_MIN_CHARS 글자 이상인 것이다.
    다시 만드는 데 1.5~2초가 더 드는 것을 아끼려는 것이다. C# NpcOutputValidator.FitClause 와 같다.
    """
    for i in range(len(line) - 1, 0, -1):
        if line[i] not in ",，":
            continue
        piece = line[:i].rstrip()
        if not piece or len(piece) > limit:
            continue
        if sum(1 for c in piece if c in QUOTE_CHARS) % 2:
            continue  # 따옴표가 열린 채 끊긴 마디는 지문("인사하며 '오늘은 …")이다
        if sum(1 for c in piece if c.isalnum()) >= CLAUSE_MIN_CHARS and piece[-1] in FINAL_ENDINGS:
            return piece
    return None


def is_pure_json(text):
    try:
        return isinstance(json.loads(strip_think(text)), dict)
    except json.JSONDecodeError:
        return False


def clean_line(value):
    return str(value or "").strip().strip("\"“”'‘’").strip()


def sentences_of(line):
    """대사를 문장으로 나눈다 (sentence_ends 와 같은 규칙). 마지막 문장 부호 뒤에 남은 글도 한 문장이다."""
    pieces, start = [], 0
    for end in sentence_ends(line):
        pieces.append(line[start:end])
        start = end
    pieces.append(line[start:])
    return [p.strip() for p in pieces if p.strip()]


def is_interjection(text):
    """감탄사만 있는 문장·마디인지 ("어흠.", "허허…", "앗,"). 비었어도 True."""
    core = PUNCT_RE.sub("", text)
    return not core or INTERJECTION_RE.match(core) is not None or ACKNOWLEDGMENT_RE.match(core) is not None


def speech_mismatch(line, hint):
    """그 도깨비의 어미(endingHint)로 끝나지 않는 첫 문장. 없으면 None. 게임의 NpcLineChecks.SpeechMatches 와 같다.
    한 문장이라도 어긋나면 걸린다 — "놀고 싶어요~ 같이 놀자!"처럼 섞은 것도 잡는다. 감탄만 있는 짧은 문장("어흠.", "히힛!")은
    건너뛰고, "안녕하시오, 주인님."처럼 끝에 붙은 부르는 말은 떼고 본다."""
    if not hint or not line or not (hint.get("pattern") or hint.get("notPattern") or hint.get("bannedPattern")):
        return None
    # bannedPattern: 대사 어디에도 있으면 안 되는 말 (반말 도깨비의 '야'·'저', '주인님' 아닌 호칭)
    if hint.get("bannedPattern"):
        found = re.search(hint["bannedPattern"], line)
        if found:
            return found.group(0).strip()
    ending = lambda text: VOCATIVE_RE.sub("", TRAILING_RE.sub("", text))
    # pattern: 문장마다 이렇게 끝나야 한다
    if hint.get("pattern"):
        judged = [s for s in sentences_of(line) if not is_interjection(s)] or [line]
        for sentence in judged:
            if re.search(hint["pattern"], ending(sentence)) is None:
                return sentence
    # notPattern: 쉼표로 나뉜 마디도 이렇게 끝나면 안 된다 ("좋은 하루 보내요, 천천히 구경하게"처럼 한 문장 안에서 섞은 것).
    # 마디는 '~니', '~고'처럼 이어 주는 말로도 끝나서 pattern 은 문장 끝에만 쓴다
    if hint.get("notPattern"):
        for sentence in sentences_of(line):
            for clause in (c.strip() for c in sentence.split(",")):
                if not is_interjection(clause) and re.search(hint["notPattern"], ending(clause)):
                    return clause
    return None


# ---------- 말투 다듬기 ----------
# 2026-11-11 20:02 시험(Kanana)에서 거른 이유 63건 중 48건이 어미였다. 말투 규칙·다짐을 바꿔 가며 다섯 번 돌려도
# 말발굽(하오체)은 한 번도, 향산이(하게체)는 12번 중 0~2번만 첫 시도에 어미를 지켰다. 뜻은 맞는데 끝만 틀린 대사
# ("관리해 줘서 고마워요!", "모시겠습니다", "감사합니다")가 대부분이라, 검사 전에 흔한 어미를 그 도깨비 말투로 고친다.
# 게임의 NpcLineChecks.NormalizeSpeech 와 같다(Verify 가 같은 예문으로 비교한다). 해요체는 고치지 않는다.
WORD_END = "(?=[\\s.!?…~,·\"'”’)\\]]|$)"   # 낱말 끝 (문장 부호·공백·글 끝 앞)
# 말투마다 먼저 바꾸는 낱말. 앞에서부터 차례로 바꾼다 (긴 것을 먼저 둔다)
SPEECH_WORDS = {
    "반말": [
        ("안녕하세요", "안녕"), ("안녕하십니까", "안녕"),  # "하세요"→"해"보다 먼저
        ("보내세요", "보내"), ("쉬세요", "쉬어"), ("드세요", "먹어"), ("앉으세요", "앉아"), ("즐기세요", "즐겨"), ("둘러보세요", "둘러봐"), ("지요", "지"),
        ("고맙습니다", "고마워"), ("감사합니다", "고마워"), ("감사해요", "고마워"), ("주셔서", "줘서"),
        ("이에요", "이야"), ("에요", "야"), ("예요", "야"),
        ("주세요", "줘"), ("하세요", "해"), ("보세요", "봐"), ("오세요", "와"), ("가세요", "가"), ("계세요", "있어"), ("마세요", "마"), ("죠", "지"),
        # 격식 어미를 반말로 (2026-11-12 시험: "순찰 나왔습니다!", "반갑습니다!"가 어미 검사에 걸렸다)
        ("반갑습니다", "반가워"), ("겠습니다", "겠어"), ("있습니다", "있어"),
        ("없습니다", "없어"), ("않습니다", "않아"), ("합니다", "해"), ("됩니다", "돼"), ("갑니다", "가"), ("옵니다", "와"),
        *[(c + "습니다", c + "어") for c in "았었였왔갔봤줬했됐썼냈"],
        ("뵙겠습니다", "볼게"), ("뵙겠어", "볼게"), ("뵐게", "볼게"), ("뵈어", "봐"), ("뵈러", "보러"), ("뵈세요", "봐"),
        ("기쁩니다", "기뻐"), ("바쁩니다", "바빠"), ("예쁩니다", "예뻐"), ("슬픕니다", "슬퍼"), ("아픕니다", "아파"), ("나쁩니다", "나빠"), ("고픕니다", "고파"),
    ],
    "하오체": [
        ("안녕하세요", "안녕하시오"), ("안녕하십니까", "안녕하시오"), ("안녕하옵니다", "안녕하시오"), ("안녕하옵니까", "안녕하시오"),
        ("감사합니다", "고맙소"), ("감사해요", "고맙소"), ("고마워요", "고맙소"), ("반가워요", "반갑소"),
        ("좋아요", "좋소"), ("있어요", "있소"), ("없어요", "없소"), ("같아요", "같소"),
        ("네요", "구려"), ("군요", "구려"), ("해요", "하오"), ("옵니다", "오"), ("옵니까", "오"), ("세요", "시오"),
    ],
    "하게체": [
        ("처음 뵙겠습니다", "처음 보는 얼굴이구먼"), ("안녕하세요", "안녕하신가"), ("안녕하십니까", "안녕하신가"),
        ("고마워요", "고맙네"), ("감사해요", "고맙네"), ("반가워요", "반갑네"), ("좋아요", "좋네"), ("주셔서", "줘서"),
        ("네요", "네"), ("군요", "구먼"), ("나요", "나"), ("세요", "시게"),
    ],
}
SPEECH_WORD_RES = {label: [(re.compile(re.escape(a) + WORD_END), b) for a, b in pairs] for label, pairs in SPEECH_WORDS.items()}
# ~습니다 / ~ㅂ니다 / ~습니까 / ~ㅂ니까 를 바꿀 끝: (습니다, ㅂ니다, 습니까, ㅂ니까)
FORMAL_ENDINGS = {"하오체": ("소", "오", "소", "오"), "하게체": ("네", "네", "는가", "는가")}
FORMAL_RE = re.compile("([가-힣]*?)([가-힣])(니다|니까)" + WORD_END)
BANMAL_PRONOUN_RE = re.compile("(^|\\s)(저는|저를|제가|저도|저랑|저한테|제)" + WORD_END)
BANMAL_PRONOUNS = {"저는": "나는", "저를": "나를", "제가": "내가", "저도": "나도", "저랑": "나랑", "저한테": "나한테", "제": "내"}
# 반말 도깨비가 "주인아,"라고 부르면 거만하게 들린다(공통 금칙어 '낮춰 부르기'). 부르는 말만 높여 고친다
# 주인·손님 호칭은 어떤 도깨비도 말하지 않는다(공통 금칙어 demeaning_address). 모델이 프롬프트의 '주인'을 옮겨 계속 불러서,
# 걸러 내 기본 대사로 바꾸는 대신 검사 전에 호칭을 지운다: "주인님, 고마워!"→"고마워!", "고마워, 주인님!"→"고마워!", "주인아"·"주인님이"도 같다
# 앞이 한글이면(집주인) 건드리지 않고, 끝이 어절 경계가 아니면(주인공) 건드리지 않는다. 게임의 NpcLineChecks.StripAddress 와 같다
ADDRESS_PARTICLES = "(?:께서|께|이입니다|입니다|이에요|이예요|이라고|이라는|이랑|한테|에게|이야|이|가|은|는|을|를|의|도|랑|와|과|아|야)?"
ADDRESS_RES = {}
B_S = "\\s"


def name_stem(name):
    """자기 이름의 어간. 끝의 '이'를 뗀다(향산이→향산, 묘작이→묘작). 이름이 두 글자면 그대로 둔다. C# NpcLineChecks.NameStem 과 같다."""
    return name[:-1] if len(name) > 2 and name.endswith("이") else name


def address_regex(name=None):
    """지울 호칭: 주인님·주인·손님과 그 도깨비 자기 이름(어간 + 선택적 '이') + 조사. 이름에 붙은 조사('향산아', '향산이가')도 함께 지운다."""
    key = name or ""
    if key not in ADDRESS_RES:
        words = "주인님|주인"
        if name:
            stem = name_stem(name)
            # 모델이 이름을 "향 산이"처럼 띄어 쓰기도 해서 글자 사이의 띄어쓰기를 받는다
            words += "|" + (B_S + "?").join(re.escape(ch) for ch in stem) + ("(?:이)?" if stem != name else "")
        ADDRESS_RES[key] = re.compile(r"([\s,]*)(?<![가-힣])(" + words + ")(" + ADDRESS_PARTICLES + r")(?=[\s,.!?…~]|$)")
    return ADDRESS_RES[key]


def self_name_hit(line, name):
    """대사가 자기 이름이나 그 어간을 말하는지 (띄어쓰기·부호는 빼고 본다). C# NpcLineChecks.ContainsSelfName 과 같다."""
    stem = PUNCT_RE.sub("", name_stem(name))
    return bool(stem) and stem in PUNCT_RE.sub("", line)
LEADING_JUNK_RE = re.compile(r"^[\s,]+")
COMMA_BEFORE_END_RE = re.compile(r",\s*(?=[.!?…~])")
COMMA_AFTER_END_RE = re.compile(r"(?<=[.!?…~]),\s*")
MULTI_SPACE_RE = re.compile(r"\s{2,}")


# '주인을'·'주인이'처럼 조사가 붙은 주인 호칭은 지우면 말이 끊긴다("주인을 보니 반가워"→"보니 반가워", "주인한테 자랑했지"→"자랑했지", 2026-11-13 시험).
# 대명사로 바꾼다 (주인을→너를, 주인이→네가, 주인한테→너한테). 부르는 말("주인님,"·"주인아")과 도깨비 자기 이름은 그대로 지운다. C# NpcLineChecks.AddressReplacement 와 같다
ADDRESS_PRONOUNS = {"을": "너를", "를": "너를", "이": "네가", "가": "네가", "은": "너는", "는": "너는", "도": "너도", "한테": "너한테", "에게": "너한테",
                    "랑": "너랑", "이랑": "너랑", "와": "너랑", "과": "너랑", "의": "네", "께서": "네가", "께": "너한테"}


# 닦아 주는 사람이 주인이다. "주인을 닦아 주시면"(주인이 닦는 대상)은 "너를 닦아 주면"이 되어 뜻이 뒤바뀌고, "주인님께 닦아 달라고 부탁드려요"는
# "너한테 닦아 줘"가 되어 어색하다(2026-11-13 시험 S03 #4, S12 #3). 목적어·받는 이(을·를·한테·에게·께)가 부탁·관리 동사 앞에 있으면 지우고,
# 주어(이·가)는 "네가 닦아 주면"처럼 자연스러워 대명사로 바꾼다. C# NpcLineChecks.AddressReplacement 와 같다
CARE_OR_REQUEST_RE = re.compile("닦|관리|돌보|씻|문지|정돈|도와|부탁|줘|줄래|줄 수|달라|주면|주라")


# 주어(이·가·께서)가 부탁하는 말의 주어일 때는 "네가"로 바꾸지 않고 지운다 ("주인님께서 닦아 주시면"→"네가 닦아 주면"이 아니라 "닦아 주면").
# "네가 닦아줘"는 너에게 시키는 말처럼 들려 무례하고(2026-11-16 시험 S12 #1·#3·#4), 부탁은 누구에게 하는지 이미 알아서 주어가 필요 없다.
# 고마움("주인이 관리해 줘서 고마워")은 "네가 관리해 줘서"로 자연스러워 그대로 바꾼다 (줘서 는 부탁이 아니다).
# C# NpcLineChecks.RequestSubject 와 같다
REQUEST_SUBJECT_RE = re.compile("(?:닦|깨|돌봐|돌보|도와|씻|문지|정돈|관리|살펴|만져)[^,.!?…~]*?(?:줘(?!서)|주면|주시면|주라|줄래|줄 수|주세요|주길|주렴|달라)")


def _kept_comma(m):
    """지운 호칭 앞에 쉼표가 있었고 뒤에 말이 이어지면 쉼표를 남긴다 ("예쁘네, 주인님 덕분에"→"예쁘네, 덕분에"). 쉼표까지 지우면 "예쁘네 덕분에"처럼 마디가 붙어 어색했다
    (2026-11-16 시험 S09 #3·S14 #1). 호칭이 문장 끝에 있으면("고마워, 주인님!") 쉼표도 함께 지운다. C# NpcLineChecks.KeptComma 와 같다."""
    if "," not in m.group(1):
        return ""
    following = m.string[m.end():].lstrip()
    return "," if following and "가" <= following[0] <= "힣" else ""


def _address_sub(m):
    particle = m.group(3)
    if m.group(2).startswith("주인") and particle in ("이", "가", "께서"):
        clause = re.split("[,.!?…~]", m.string[m.end():], maxsplit=1)[0]
        if REQUEST_SUBJECT_RE.search(clause):
            return _kept_comma(m)
    if m.group(2).startswith("주인") and particle in ADDRESS_PRONOUNS:
        if particle not in ("이", "가", "은", "는", "께서"):
            rest = m.string[m.end():]
            if CARE_OR_REQUEST_RE.search(re.split("[.!?…~]", rest, maxsplit=1)[0]):
                return _kept_comma(m)
        comma = _kept_comma(m)
        return (comma + " " if comma else " " if m.group(1) else "") + ADDRESS_PRONOUNS[particle]
    return _kept_comma(m)


def strip_address(line, name=None):
    """대사에서 '주인님'·'주인'·'손님' 호칭과 자기 이름(name)을 지우고 남은 쉼표·띄어쓰기를 다듬는다. 조사가 붙은 주인은 대명사로 바꾼다."""
    rx = address_regex(name)
    if not line or not rx.search(line):
        return line
    line = rx.sub(_address_sub, line)
    line = LEADING_JUNK_RE.sub("", line)
    line = COMMA_BEFORE_END_RE.sub("", line)
    line = COMMA_AFTER_END_RE.sub(" ", line)
    line = MULTI_SPACE_RE.sub(" ", line).strip()
    # "주인님!"처럼 호칭만 있던 대사는 부호만 남는다. 말이 하나도 없으면 빈 대사로 본다
    return line if re.search("[가-힣A-Za-z0-9]", line) else ""
WORD_YO_RE = re.compile("([가-힣]+)요" + WORD_END)
YO_NOUNS = {"필요", "중요", "요요", "주요", "수요"}  # 반말에서 '요'를 떼면 안 되는 낱말


def formal(m, endings):
    """~습니다·~ㅂ니다(·니까)를 그 말투의 끝으로. 앞 글자에 받침 ㅂ 이 있으면 떼고(합 → 하) 붙인다."""
    head, last, ask = m.group(1), m.group(2), m.group(3) == "니까"
    after_seup, after_b, ask_seup, ask_b = endings
    if last == "습":
        return head + (ask_seup if ask else after_seup)
    code = ord(last) - 0xAC00
    if 0 <= code < 11172 and code % 28 == 17:  # 받침 ㅂ
        return head + chr(ord(last) - 17) + (ask_b if ask else after_b)
    return m.group(0)


# 반말 도깨비가 '닦아 주시면', '오셨어'처럼 -시- 높임을 섞으면 어색하다. 높임을 뺀다. 게임의 NpcLineChecks.NormalizeSpeech 와 같다
# '인사드려요'·'부탁드려요'의 '드리다'는 '주다'가 아니라 '하다'로 바꾼다 (2026-11-12 시험: "향산이 인사드려요"→"인사줘", "부탁드려요"→"부탁줘")
# "말씀"은 뺐다: "말씀드릴게"→"말씀할게"가 되어 존대가 남았다. 아래 HONORIFIC_WORDS 머리의 말씀 항목이 "말했·말할게"로 먼저 바꾼다 (2026-11-17 19:43 시험 S14 #2 "말씀드렸지?")
HUMBLE_NOUNS = ("부탁", "인사", "감사", "사과", "축하", "약속")
HONORIFIC_WORDS = [
    # 말씀(드리다): "말씀드렸지?"→"말했지?" (먼저 바꾼다)
    ("말씀드립니다", "말해"), ("말씀드릴게", "말할게"), ("말씀드릴까", "말할까"), ("말씀드릴래", "말할래"), ("말씀드렸", "말했"), ("말씀드리", "말하"), ("말씀드려", "말해"), ("말씀드릴", "말할"),
    ("말씀하", "말하"), ("말씀해", "말해"), ("말씀", "말"),
    *[(n + a, n + b) for n in HUMBLE_NOUNS for a, b in (("드립니다", "해"), ("드릴게", "할게"), ("드릴까", "할까"), ("드릴래", "할래"), ("드려", "해"), ("드렸", "했"), ("드릴", "할"))],
    ("계시면", "있으면"), ("계시는", "있는"), ("계신", "있는"), ("계셔서", "있어서"), ("계셔", "있어"), ("계셨", "있었"),
    # -셨- (과거): 닦아 주셨어→닦아 줬어
    ("들이셨", "들였"), ("하셨", "했"), ("오셨", "왔"), ("가셨", "갔"), ("보셨", "봤"), ("주셨", "줬"), ("되셨", "됐"), ("쓰셨", "썼"),
    ("이셨", "였"), ("나셨", "났"), ("사셨", "샀"), ("타셨", "탔"), ("자셨", "잤"), ("서셨", "섰"), ("내셨", "냈"), ("드셨", "먹었"),
    # -실 (미래·의문): 가실래→갈래, 줄 수 있나
    ("가실", "갈"), ("오실", "올"), ("하실", "할"), ("보실", "볼"), ("주실", "줄"), ("되실", "될"), ("쓰실", "쓸"), ("드실", "먹을"), ("이실", "일"),
    # -신 (관형): 닦아 주신 덕분에→닦아 준 덕분에
    ("들이신", "들인"), ("하신", "한"), ("오신", "온"), ("가신", "간"), ("보신", "본"), ("주신", "준"), ("되신", "된"), ("쓰신", "쓴"),
    ("이신", "인"), ("드신", "먹은"), ("으신", "은"),
    # 도와드릴게·해드려 같은 겸양
    ("주셔", "줘"), ("하셔", "해"), ("오셔", "와"), ("가셔", "가"), ("보셔", "봐"), ("안녕하십네다", "안녕"),
    ("드릴게", "줄게"), ("드릴까", "줄까"), ("드려", "줘"),
    # "보여드릴 수 있어서"(2026-11-16 시험 S14 #3)처럼 남은 겸양 '드리다'
    ("드릴래", "줄래"), ("드렸", "줬"), ("드릴", "줄"),
    # 2026-11-17 19:43 시험(존대·반말 혼합이 여전했다): 남은 겸양·높임 낱말을 반말로. "모시고"는 '시'를 떼면 "모고"가 되므로 낱말로 먼저 바꾼다
    ("여쭤", "물어"), ("여쭙", "묻"), ("여쭈", "묻"), ("모시고", "데리고"), ("모셔다", "데려다"), ("모셔", "데려"),
    ("뵙고", "보고"), ("뵙게", "보게"), ("뵈어", "봐"), ("뵈었", "봤"), ("뵀", "봤"),
    ("계시지", "있지"), ("계시군", "있구나"), ("계시네", "있네"), ("계시", "있"),
    ("주무세요", "자"), ("주무시", "자"), ("주무셔", "자"),
    ("오십시오", "와"), ("주십시오", "줘"), ("하십시오", "해"), ("보십시오", "봐"), ("가십시오", "가"), ("앉으십시오", "앉아"),
    ("그러세요", "그래"), ("그러셨", "그랬"), ("그러시군", "그렇구나"), ("성함", "이름"), ("연세", "나이"),
]
# 2026-11-12 11:15 시험: "어서 왔어"(어서 + 과거), "닦아달라고 부탁해"(부탁을 전해 들은 말투로 옮김)처럼 문맥에 어색한 말이 나왔다.
# 고칠 수 있는 것은 고치고(아래), 못 고친 것은 공통 금칙어 awkward 묶음이 거른다. C# NpcLineChecks.FixAwkward 와 같다
QUOTED_ASK_RE = re.compile(r"([가-힣]*[아어여해])\s?달라고\s?(?:부탁|요청|말)(?:드리고\s?싶(?:어요|어|습니다)|하고\s?싶(?:어요|어)|드립니다|드려요|드릴게요|드릴게|드려|해요|했어요|했어|했다|했네|합니다|할게요|할게|하다|해)?")
# "어서 왔어?"를 "어서 와"로 고치면서 물음표를 남기면 "어서 와?"가 된다 (2026-11-18 23:45 시험 S06 #4 "어서 와? 또 왔네."). 인사라 "!"로 바꾼다
ARRIVE_RE = re.compile(r"어서,?\s?(?:왔(?:어요|어|네요|네|구나|지)|와(?![가-힣]))(\?+)?")


# "편하게 구경해 줘"는 손님에게 구경을 부탁하는 말이라 어색하다(2026-11-16 시험 S05 #2·#3). 권하는 말로 "구경해"라고 한다
OFFER_ASK_RE = re.compile("(구경해|구경하고 가|둘러봐|쉬었다 가|놀다 가)\\s?줘" + WORD_END)


def fix_awkward(line):
    """'닦아달라고 부탁해'→'닦아 줘', '어서 왔어'→'어서 와', '구경해 줘'→'구경해'."""
    line = QUOTED_ASK_RE.sub(lambda m: m.group(1) + " 줘", line)
    line = OFFER_ASK_RE.sub(lambda m: m.group(1), line)
    return ARRIVE_RE.sub(lambda m: "어서 와!" if m.group(1) else "어서 와", line)


# 앞이 한글이고 '마시다'·'도시'가 아닐 때, 뒤가 이런 어미인 '시'를 뗀다 (주시면→주면, 하시는→하는, 보시고→보고)
# '~다니·~더니·~면서·~거나·~든지·~느라'도 뗀다: "나를 부르시다니 반가워"가 높임이 남은 채 나갔다(2026-11-16 시험 S01 #1)
HONORIFIC_SI_RE = re.compile("(?<=[가-힣])(?<![마도다모])시(?=면|며|고|는|던|지|네|겠|니|거|길|려|도록|어서|나|잖|더라|다니|더니|면서|거나|든지|느라)")
# '닦아 주면 좋겠어'는 부탁을 돌려 말해 어색하다. 바로 '닦아 줘'로 말한다 (2026-11-12 시험: "혹시 한 번 더 닦아주면 좋겠어")
ASK_RE = re.compile("(?<=[아어여해])( ?)주면 좋겠(?:어요|어|다)(?=[\\s.!?…~,]|$)")


# '만드셨'·'만드신'은 '만들었'·'만든'이다. '드셨→먹었'이 먼저 바꾸면 "만먹었는지"가 된다 (2026-11-13 시험: 밤바다 S14)
MAKE_RE = re.compile("만드(셨|셔|신|실)")
MAKE_WORDS = {"셨": "만들었", "셔": "만들어", "신": "만든", "실": "만들"}
# '으셨'은 앞 글자 모음에 따라 '았'(쏟으셨→쏟았, 받으셨→받았) 또는 '었'(먹으셨→먹었). 늘 '었'으로 바꾸면 "쏟었군"이 된다 (2026-11-13 시험: 밤바다 S14)
EUN_SYEOT_RE = re.compile("([가-힣])으셨")
# '드셨·드신·드실'(먹다)은 앞이 한글이 아닐 때만 ('만드셨'·'들이드셨'을 건드리지 않는다)
EAT_HONORIFICS = {"드셨", "드신", "드실"}

# 르 불규칙(부르셔서→불러서, 부르셨→불렀, 부르신→부른)·우 불규칙(깨우셨→깨웠)·'으셔'(찾으셔→찾아)의 높임. 2026-11-16 시험: 탭 상황을 "너를 불렀다"로 바꾼 뒤
# "부르셔서 반가워"가 S22 #4·S23 #1·#4·S24 #3 에서 어미 섞임('셔')으로 걸려 다시 만들다 기본 대사로 넘어갔다. 고쳐서 살린다. C# NpcLineChecks 의 같은 이름과 같다
EU_SYEO_RE = re.compile("([가-힣])으셔")
RU_RE = re.compile("([가-힣])르(셨|셔|신|실)")
WOO_RE = re.compile("([가-힣])우(셨|셔|신|실)")
WOO_WORDS = {"셨": "웠", "셔": "워", "신": "운", "실": "울"}


def _ru_sub(m):
    if m.group(0) == "어르신":
        return m.group(0)
    stem, end = m.group(1), m.group(2)
    if end in ("신", "실"):
        return stem + ("른" if end == "신" else "를")
    # 받침 없는 글자는 ㄹ 받침을 붙인다(부→불), 이미 받침이 있으면 그대로(들르셔→들러)
    stem = stem if (ord(stem) - 0xAC00) % 28 else chr(ord(stem) + 8)
    return stem + ("렀" if end == "셨" else "러")


def takes_a(ch):
    """앞 글자의 모음이 ㅏ·ㅗ 인가 (쏟→쏟았, 먹→먹었). C# NpcLineChecks.TakesA 와 같다."""
    code = ord(ch) - 0xAC00
    return 0 <= code < 11172 and (code // 28) % 21 in (0, 8)


def drop_honorific(line):
    line = MAKE_RE.sub(lambda m: MAKE_WORDS[m.group(1)], line)
    line = EUN_SYEOT_RE.sub(lambda m: m.group(1) + ("았" if takes_a(m.group(1)) else "었"), line)
    line = EU_SYEO_RE.sub(lambda m: m.group(1) + ("아" if takes_a(m.group(1)) else "어"), line)
    line = RU_RE.sub(_ru_sub, line)
    line = WOO_RE.sub(lambda m: m.group(1) + WOO_WORDS[m.group(2)], line)
    for a, b in HONORIFIC_WORDS:
        if a in EAT_HONORIFICS:
            line = re.sub("(?<![가-힣])" + re.escape(a) + ("(?=[\\s.!?…~,]|$)" if a.endswith("신") else ""), b, line)
        elif a.endswith("신"):
            # '-신'(주신·하신)은 낱말 끝일 때만 — '안녕하신가'·'주신다'의 '신'을 건드리지 않는다
            line = re.sub(re.escape(a) + "(?=[\\s.!?…~,]|$)", b, line)
        else:
            line = line.replace(a, b)
    line = HONORIFIC_SI_RE.sub("", line)
    return ASK_RE.sub(lambda m: m.group(1) + "줘", line)


IMNIDA_RE = re.compile("([가-힣])입니다" + WORD_END)


PAST_SEUPNIDA_RE = re.compile("([가-힣])습니다" + WORD_END)


def past_seupnida(line):
    """'기다렸습니다'·'좋았습니다'처럼 받침 ㅆ 뒤의 '~습니다'를 '~어'로 (기다렸어·좋았어). 글자 표(았었였왔…)에 없는 것까지 잡는다. C# NpcLineChecks.PastSeupnida 와 같다."""
    return PAST_SEUPNIDA_RE.sub(lambda m: m.group(1) + "어" if 0xAC00 <= ord(m.group(1)) <= 0xD7A3 and (ord(m.group(1)) - 0xAC00) % 28 == 20 else m.group(0), line)


def imnida_to_banmal(line):
    """'~입니다'를 반말로: 받침 없는 낱말 뒤는 '야'(친구입니다→친구야), 받침 있는 낱말 뒤는 '이야'(책입니다→책이야). C# NpcLineChecks.ImnidaToBanmal 과 같다."""
    return IMNIDA_RE.sub(lambda m: m.group(1) + ("이야" if (ord(m.group(1)) - 0xAC00) % 28 else "야"), line)


def _apply_speech(line, label):
    """작은 모델이 흔히 틀리는 어미를 그 도깨비 말투(endingHint.label: 반말 · 하오체 · 하게체)로 고친다. 고칠 것이 없으면 그대로.
    반말: 낱말 끝 '~요'를 뗀다(고마워요→고마워), 주세요→줘, 저는→나는. 주인님·주인·손님 호칭은 지운다. 하오체: 모시겠습니다→모시겠소,
    합니다→하오, 안녕하십니까→안녕하시오, 세요→시오. 하게체: 감사합니다→감사하네, 습니까→는가, 세요→시게, 고마워요→고맙네.
    고치지 못한 것(하오체의 '~어요' 등)은 어미 검사(speech_mismatch)가 거른다."""
    if not line or label not in SPEECH_WORD_RES:
        return line
    for pattern, word in SPEECH_WORD_RES[label]:
        line = pattern.sub(lambda m, w=word: w, line)
    if label == "반말":
        line = fix_awkward(drop_honorific(imnida_to_banmal(past_seupnida(line))))
        line = BANMAL_PRONOUN_RE.sub(lambda m: m.group(1) + BANMAL_PRONOUNS[m.group(2)], line)
        keep = lambda stem: stem + "요" in YO_NOUNS or stem.endswith(("세", "시"))
        return WORD_YO_RE.sub(lambda m: m.group(0) if keep(m.group(1)) else m.group(1), line)
    return FORMAL_RE.sub(lambda m: formal(m, FORMAL_ENDINGS[label]), line)


GUEST_PARTICLES = {"이라서": "라서", "이라고": "라고", "이라는": "라는", "이야": "야", "이다": "다", "께서": "가", "께": "한테", "이랑": "랑", "이": "가", "은": "는", "을": "를", "과": "와", "아": "야"}
GUEST_RE = re.compile("손님(?:(이라서|이라고|이라는|이야|이다|께서|께|이랑|이|은|을|과|아)(?![가-힣]))?")


def guest_to_friend(line):
    """'손님'이 대사에 남으면 출력 직전에 '친구'로 바꾼다. 붙은 조사도 맞춘다(손님이→친구가, 손님은→친구는, 손님을→친구를, 손님께서→친구가, 손님께→친구한테). C# NpcLineChecks.GuestToFriend 와 같다."""
    if not line or "손님" not in line:
        return line
    return GUEST_RE.sub(lambda m: "친구" + GUEST_PARTICLES.get(m.group(1) or "", ""), line)


# 한 대사 안에서 같은 말을 되풀이하지 않는다 (2026-11-12 10:52 시험: "고마워, 관리해줘서 고마워", "고마워! 관리해줘서 고마워~").
# 짧은 토막(여섯 글자 이하)이 다른 토막 안에 그대로 있으면 그 토막을 지운다. 지울 수 없는 반복은 출력 검사(inner_repeat)가 거른다.
# C# NpcLineChecks.DropRepeatedLead / InnerRepeat 와 같다
SEGMENT_RE = re.compile(r"[^,.!?…~]+[,.!?…~]*\s*")
REPEAT_EXEMPT = {"나는", "내가", "나도", "나랑", "너는", "네가", "이건", "그건", "저기", "여기", "거기"}


# 최근 대사를 프롬프트에 실어 "이와 다르게 말한다"고 해 봤지만 작은 모델이 그 대사를 그대로 베꼈다(2026-11-12 11:02 시험: SLM 대사 85% → 25%,
# 첫 시도 통과 77% → 21%, 시도 평균 1.23 → 1.79, 응답 중앙값 2.6초 → 4.0초). 최근 대사·금지할 말을 프롬프트에 늘어놓지 않는다.
# 반복은 곁들일 거리(flavors)를 돌려 싣는 것과 출력 검사(Repeated)로만 막는다
def drop_repeated_lead(line):
    segments = SEGMENT_RE.findall(line or "")
    if len(segments) < 2:
        return line
    cores = [PUNCT_RE.sub("", x) for x in segments]
    kept = []
    for i, core in enumerate(cores):
        repeated = 2 <= len(core) <= 6 and any(
            j != i and core in cores[j] and (len(cores[j]) > len(core) or j > i) for j in range(len(cores)))
        if not repeated:
            kept.append(segments[i])
    result = "".join(kept).strip()
    if not result:
        return line
    return result[:-1] + "." if result.endswith(",") else result


# 문장 끝 부호와 닫는 부호만 남은 꼬리, 쉼표로 가른 마디(clause). 문장은 sentence_ends(출력 검사와 같은 문장 끝 규칙)로 가른다
SENTENCE_TAIL_RE = re.compile("[.!?~。…]*[\"'”’)\\]」』]*\\s*$")
CLAUSE_RUN_RE = re.compile(r"[^,]+,*\s*")
INNER_MARKS_RE = re.compile(r"[…~.]*\s*")  # 앞 낱말을 지울 때 함께 지우는 문장 안 부호 ("고마워… 정말 고마워" → "정말 고마워")
# 되풀이한 마디·낱말 사이에 이것만 있으면 같은 말을 되풀이한 것이다 ("또 왔네, 다시 왔네", "고마워 정말 고마워"). 주어·대상·다른 풀이말이 있으면
# 다른 말이라("나는 좋아, 너는 안 좋아") 지우지 않고 출력 검사 inner_repeat 에 맡긴다 (CodeRabbit 리뷰)
RESTATE_FILLERS = {
    "또", "다시", "정말", "진짜", "참", "너무", "아주", "정말로", "진짜로", "늘", "항상", "오늘도", "와", "오", "아", "음", "우와", "어서",
    "많이", "꼭", "더", "좀", "역시", "엄청", "되게", "무척", "매우", "완전", "그래", "응", "어", "자", "참말",
}


def drop_repeated_word(line):
    """한 문장 안에서 두 글자 이상 같은 낱말로 같은 말을 되풀이하면 하나만 남긴다 ("또 왔네, 다시 왔네" → "다시 왔네"). 거르면 다시 만드는 데 1.5~2초가 들어 고쳐 쓴다.
    쉼표로 가른 서로 다른 마디에 있으면 짧은 마디를 통째로 지운다(길이가 같으면 뒤 마디). 낱말만 지우면 "또, 다시 왔네"처럼 마디가 망가진다.
    뒤 마디를 지우면 그 마디의 문장 끝 부호를 남긴다 ("닦아줘서 고마워, 정말 고마워!" → "닦아줘서 고마워!"). 한 마디 안에 있으면 앞의 낱말만 지운다
    ("고마워 정말 고마워." → "정말 고마워."). 같은 말일 때만 지운다: 지울 마디(또는 두 낱말 사이)에 되풀이한 낱말 말고는 '또·다시·정말' 같은 꾸밈(RESTATE_FILLERS)만 있고,
    두 마디의 부정(안·못·않·없)이 같아야 한다. "나는 좋아, 너는 안 좋아"·"좋아, 정말 안 좋아"는 두 말이 달라 그대로 두고 inner_repeat 가 거른다.
    문장(sentence_ends)이 다르면 건드리지 않고, REPEAT_EXEMPT 낱말과 한 글자 낱말은 되풀이로 보지 않는다. C# NpcLineChecks.DropRepeatedWord 와 같다."""
    if not line:
        return line
    out = []
    start = 0
    for end in sentence_ends(line) + [len(line)]:
        if end <= start:
            continue
        stop = end
        while stop < len(line) and line[stop].isspace():  # 문장 뒤의 띄어쓰기도 그 문장에 붙여 둔다
            stop += 1
        out.append(_drop_repeated_word_in_sentence(line[start:stop]))
        start = stop
    out.append(line[start:])
    return "".join(out)


def _drop_repeated_word_in_sentence(sentence):
    for _ in range(16):  # 한 번에 되풀이 하나를 지운다. 되풀이가 여럿이면 다시 본다
        tail = SENTENCE_TAIL_RE.search(sentence).group(0)
        body = _drop_one_repeat(sentence[:len(sentence) - len(tail)])
        if body is None:
            return sentence
        sentence = body + tail
    return sentence


def _only_fillers(text, repeated):
    return all(w == repeated or w in RESTATE_FILLERS for w in re.findall("[가-힣]+", text))


def _negated(text):
    return any(w == "안" or w.startswith("못") or "않" in w or "없" in w for w in re.findall("[가-힣]+", text))


def _drop_one_repeat(body):
    """같은 말의 되풀이 하나를 지운 body, 지울 것이 없으면 None."""
    words = [w for w in re.finditer("[가-힣]+", body) if len(w.group(0)) >= 2 and w.group(0) not in REPEAT_EXEMPT]
    clauses = list(CLAUSE_RUN_RE.finditer(body))

    def clause_at(index):
        return next((i for i, c in enumerate(clauses) if c.start() <= index < c.end()), -1)

    for j in range(1, len(words)):
        first = next((w for w in words[:j] if w.group(0) == words[j].group(0)), None)
        if first is None:
            continue
        second = words[j]
        first_clause, second_clause = clause_at(first.start()), clause_at(second.start())
        if first_clause < 0 or second_clause < 0:
            continue
        if first_clause == second_clause:
            if not _only_fillers(body[first.end():second.start()], None):
                continue
            end = first.end() + INNER_MARKS_RE.match(body, first.end()).end() - first.end()
            return body[:first.start()] + body[end:]
        first_text, second_text = clauses[first_clause].group(0), clauses[second_clause].group(0)
        drop = first_clause if len(PUNCT_RE.sub("", first_text)) < len(PUNCT_RE.sub("", second_text)) else second_clause
        dropped = clauses[drop].group(0)
        kept = second_text if drop == first_clause else first_text
        if not _only_fillers(dropped, first.group(0)) or _negated(dropped) != _negated(kept):
            continue
        result = body[:clauses[drop].start()] + body[clauses[drop].end():]
        if drop == len(clauses) - 1:
            # 마지막 마디를 지우면 앞 마디의 쉼표가 문장 끝에 남는다 → 지우고 지운 마디의 끝 부호를 붙인다
            result = result.rstrip().rstrip(",").rstrip()
        return result
    return None


def inner_repeat(line):
    """한 대사 안에서 두 번 나온 낱말(세 글자 이상은 앞 두 글자가 같으면 같은 말로 본다). 없으면 None."""
    seen = set()
    for word in re.findall("[가-힣]+", line or ""):
        if len(word) < 2 or word in REPEAT_EXEMPT:
            continue
        key = word if len(word) == 2 else word[:2]
        if key in seen:
            return word
        seen.add(key)
    return None


def strip_nim(line, nickname=None):
    """호칭에 '님'을 붙이지 않는다: 도깨비는 모두 반말이다. 닉네임("도윤님"→"도윤", "도윤님께"→"도윤한테")과 '발굴자'·'친구'("발굴자님", "친구님")에 붙은 '님'을 뗀다.
    거르지 않고 고쳐서 호칭이 대사에 나오게 한다. C# NpcLineChecks.StripNim 과 같다."""
    if not line:
        return line
    if nickname:
        line = line.replace(nickname + "님께", nickname + "한테").replace(nickname + "님", nickname)
    return line.replace("발굴자님께", "발굴자한테").replace("발굴자님", "발굴자").replace("친구님께", "친구한테").replace("친구님", "친구")


# 기록이 없는 주인 장면(대화 기억도 마지막 관리 사실도 없음)에서 "왔구나, 오랜만이야!"·"또 왔구나"는 지어낸 말이다. 탭 인사 16건 중 4건이 이것으로 거르고 다시 만들다
# 기본 대사로 넘어갔다(2026-11-17 시험 S01 #3·#4, S22 #3, S23 #1). 거르지 않고 고친다: "오랜만이야"→"반가워", "오랜만에 왔구나"→"왔구나", "또 왔구나"→"왔구나".
# 상황 문장에 그 낱말이 있으면 근거가 있어 고치지 않는다(no_record_applies). C# NpcLineChecks.FixNoRecord 와 같다
# "오랜만이야"·"오랜만에 보네"를 바꿔 넣는 말. 늘 "반가워" 하나로 바꾸면 모델이 다르게 쓴 인사들이 같은 대사로 모여("어서 왔구나, 오랜만에 보네" → "어서 와, 반가워")
# 최근 대사와 같다고 걸리고 다시 만들었다 (2026-11-18 22:16 시험: 말발굽 탭 인사 16번 중 12번이 '오랜만', S23 #1·#2·#4·S24 #3 반복).
# 검사는 최근 대사와 겹치지 않는 것을 고른다(pick_no_record_variant). C# NpcLineChecks.NoRecordGreetings 와 같다
NO_RECORD_GREETINGS = ("반가워", "기다렸어", "보고 싶었어", "기뻐")
LONG_ABSENCE_RE = re.compile("오(?:랜|래간)만")


def fix_absence_clause(line, replacement):
    """'오랜만'이 서술어·마디인 꼴을 고친다 (fix_no_record·fix_long_absence 가 함께 쓴다): "오랜만이야·오랜만이네요·오랜만이구나·오랜만인걸"과 말끝의 "(정말) 오랜만!"은
    replacement 로 바꾸고, "오랜만이라(서)·오랜만인데·오랜만이니까"로 시작하는 마디는 마디째 지운다. 뒤에 글자가 붙은 꼴("오랜만이라고")은 둔다.
    2026-11-24 기기 시험: "오랜만이야"만 고쳐, 문장 안에 남은 다른 꼴을 출력 검사가 기록과 어긋남으로 걸렀다. C# NpcLineChecks.FixAbsenceClause 와 같다."""
    if not line:
        return line
    line = line.replace("오랫만", "오랜만")  # 철자 고치기(fix_typos)는 이보다 뒤에 돈다
    line = re.sub("오(?:랜|래간)만(?:이(?:네요|구나|군요|군|잖아)|인걸)", replacement, line)
    line = re.sub("오(?:랜|래간)만이(?:야|네|다|에요)", replacement, line)
    # 마디 뒤에 이어 주는 말("그런지·그래서·그런가·그런데")이 오면 함께 지운다 — "오랜만이라 그런지 반가워!"에서 '그런지'만 남지 않게
    line = re.sub("오(?:랜|래간)만(?:이(?:라서|라|어서|니까|던데|라도)|인데)(?![가-힣]),? ?(?:(?:그런지|그래서|그런가|그런데)(?![가-힣]),? ?)?", "", line)
    return re.sub("(?:(?:정말|진짜|참|너무|되게) )?오(?:랜|래간)만(?=[!?.~…]|$)", replacement, line)


def fix_no_record(line, variant=0):
    greeting = NO_RECORD_GREETINGS[variant % len(NO_RECORD_GREETINGS)]
    line = fix_absence_clause(line or "", greeting)
    # '오랜만에'는 뒤가 혼자 문장이 되는 움직임말(왔·와·찾아·오·들렀·돌아…)일 때만 지운다. "오랜만에 보네"를 지우면 목적어 없는 "보네"가 남는다(2026-11-17 21:45 시험 S23 #1 "어서 와! 보네, 도윤아").
    # "오랜만에 보네"는 지우지 않고 뜻이 같은 말로 바꾼다 (22:07 시험: 남겨 두자 기록과 어긋남으로 거르고 다시 만들어 시도가 늘었다)
    # "오랜만에 보자"도 같다 (22:16 시험 S22 #3: 못 고쳐 기록과 어긋남으로 걸렸다)
    line = re.sub("오(?:랜|래간)만에 ?(?:보는구나|보네|보는걸|보는군|보자(?![가-힣])|봤네|봤구나|봐(?![가-힣])|만났네|만났구나|마주쳤네)", greeting, line)
    line = re.sub("오(?:랜|래간)만에 ?보니", "만나니", line)
    line = re.sub("오(?:랜|래간)만에 ?봐서", "만나서", line)
    line = re.sub("오(?:랜|래간)만에 ?(?=(?:왔|와|오|찾아|들렀|돌아|방문|놀러|만나|만났|마주))", "", line)
    # 문장 안의 그 밖의 '오랜만에'도 꾸밈말이라 지워도 말이 된다 ("드디어 오랜만에 하늘이 나를 찾았네" → "드디어 하늘이 나를 찾았네") (2026-11-24).
    # 목적어 없이 '보다'만 남는 꼴("오랜만에 보러")은 그대로 두어 출력 검사가 거른다.
    # 뒤에 띄어쓰기·쉼표·끝이 올 때만 지운다 — "오랜만에야 왔구나"의 '오랜만에'를 지우면 "야 왔구나" 토막이 남는다
    line = re.sub("(?:(?:정말|진짜|참|너무|되게) )?오(?:랜|래간)만에(?=[ ,]|$)(?!,? ?[보봐봤]),? ?", "", line)
    return re.sub("(?<![가-힣])(?:또|다시) ?(?=왔)", "", line)


def fix_long_absence(line, visit_days):
    """최근 방문이 7일 안에 있는데 "오랜만에 와 줘서"라고 한 말을 "또 와 줘서"로 고친다 (단골에게 "오랜만"이라고 해서 거의 모든 시도가 걸렸다, 2026-11-14 시험 S13).
    방문 기록이 없거나 7일 넘게 비었으면 그대로 둔다. C# NpcLineChecks.FixLongAbsence 와 같다."""
    if not line or not visit_days:
        return line
    if visit_days.strip() == "오늘":
        # 처음 온 친구: "오랜만이야"는 "반가워", "오랜만에 와 줘서"는 "와 줘서" (지난 만남을 지어내지 않는다). "오랜만에 보니"는 "만나니" — 지우면 "보니"만 남는다
        line = re.sub("오(?:랜|래간)만에 ?보니", "만나니", line)
        line = fix_absence_clause(line, "반가워")
        return re.sub("오(?:랜|래간)만에 ?", "", line)
    gaps = [d for _, d in day_refs(visit_days, True) if d > 0]
    if not gaps or min(gaps) >= 7:
        return line
    # 이미 "또 왔구나!"라고 했으면 "또 왔구나! 반가워, 또 왔네!"처럼 겹치지 않게 "반가워"로 바꾼다 (2026-11-16 시험 S25 #1)
    again = "또 왔네" if not re.search("(?<![가-힣])또(?![가-힣])", line) else "반가워"
    # "또 왔구나! 오랜만에 보니 반가워"의 '오랜만에'를 지우면 "또 왔구나! 보니 반가워"처럼 '보니'만 남는다 (2026-11-18 --live-store) → "다시 보니"
    line = re.sub("오(?:랜|래간)만에 ?보니", "다시 보니", line)
    line = fix_absence_clause(line, again)
    if re.search("(?<![가-힣])또(?![가-힣])", line):
        line = re.sub("오(?:랜|래간)만에 ?", "", line)  # 이미 "또 왔네"라고 했으면 "또 왔네, 또 와서"처럼 겹치지 않게 지운다 (2026-11-17 22:27 시험 S06 #1)
    else:
        line = re.sub("오(?:랜|래간|랜 |래간 )만에", "또", line)
    return line.replace("또 또", "또")


TRAILING_INTERJECTION_RE = re.compile(r"(?:[\s,]*(?:히힛|히히|히잉|헤헤|킁킁|어흠|허허|에헴|호호|앗싸|야호|오예)[!?.~…]*)+\s*$")


def strip_trailing_interjection(line):
    """대사 끝의 의성어·헛기침("…줘서 히힛!", "고마워! 어흠.")을 뗀다. 문장으로 끝내야지 '히힛!' 같은 말로 끝내지 않는다.
    앞에 말이 없는 대사("히힛.")나 앞에 오는 의성어("히힛! 놀자")는 그대로 둔다. 뗀 뒤 끝에 문장 부호가 없으면 마침표를 붙인다.
    C# NpcLineChecks.StripTrailingInterjection 과 같다."""
    if not line:
        return line
    m = TRAILING_INTERJECTION_RE.search(line)
    if not m:
        return line
    rest = line[:m.start()].rstrip(" ,")
    if not re.search("[가-힣]", rest):
        return line
    if re.search("[.!?…~]$", rest):
        return rest
    # 조사로 끝나면("기분이 히히!"→"기분이") 뒤가 잘린 말이라 마침표를 붙이지 않는다 — incomplete_ending 이 거른다
    last_word = rest.split(" ")[-1]
    return rest if len(last_word) >= 2 and re.search("(이|가|도|에|로|와|과|랑|을|를|은|는)$", last_word) else rest + "."


# 묻는 말이 아닌데 '?'로 끝낸 것을 '.'로 고친다 ("오늘 아침부터 바쁘네?"·"나를 부르네?" — 2026-11-16 22:33 시험 S01 #3·#4). 감탄·서술 끝맺음(~네·~군·~구나·~더라) 뒤의 '?'만 건드리고,
# 묻는 말("왔어?"·"볼래?"·"있지?")은 그대로 둔다. C# NpcLineChecks.FixStatementQuestion 과 같다
STATEMENT_QUESTION_RE = re.compile("(?<=[가-힣])(네|군|구나|로구나|더라)\\?")


def fix_statement_question(line):
    return STATEMENT_QUESTION_RE.sub(lambda m: m.group(1) + ".", line or "")


# "닦아줘 줘"처럼 같은 끝말을 띄어 되풀이한 것은 하나만 남긴다 (2026-11-16 시험 S12 #1). C# NpcLineChecks.CollapseDoubleVerb 와 같다
DOUBLE_VERB_RE = re.compile(r"(\S+(줘|봐|해))\s+\2(?![가-힣])")


def collapse_double_verb(line):
    return DOUBLE_VERB_RE.sub(r"\1", line or "")


# 플레이어를 부를 때는 친밀도에 따라 '발굴자'나 닉네임 둘 중 하나로만 부른다. 모델이 "친구야"·"손님"·틀린 쪽 호칭으로 부르면 거르지 않고 맞는 쪽으로 바꾼다
# (거르면 다시 만드는 1.5~2초가 든다). 부르는 말(뒤에 쉼표·느낌표·물음표가 오는 것)만 바꾸고 "친구가 왔네"·"내 친구야"처럼 문장 안의 말은 건드리지 않는다.
# 아·야는 닉네임으로 바꿀 때만 받침에 맞게 붙인다. C# NpcLineChecks.UnifyPlayerCall 과 같다
# '온'은 "처음 온 친구야!"처럼 부르는 말이 아니라 서술하는 말이다 ("처음 온 발굴자!"로 바뀌었다, 2026-11-16 시험 S05 #1)
CALL_PREFIX_SKIP = ("내", "우리", "오랜", "새", "좋은", "단골", "온")


def _has_batchim(ch):
    return "가" <= ch <= "힣" and (ord(ch) - 0xAC00) % 28 != 0


def unify_player_call(line, call_name, nickname=None):
    if not line or not call_name:
        return line
    words = [STRANGER, "친구", "손님"] + ([nickname] if nickname else [])
    rx = re.compile("(?<![가-힣])(" + "|".join(re.escape(w) for w in words) + ")(아|야)?(?=\\s*[,!?~…])")

    def sub(m):
        if m.group(1) == call_name:
            return m.group(0)
        if line[:m.start()].rstrip().endswith(CALL_PREFIX_SKIP):
            return m.group(0)
        if call_name == STRANGER or not m.group(2):
            return call_name
        return call_name + ("아" if _has_batchim(call_name[-1]) else "야")

    return rx.sub(sub, line)


# 플레이어를 가리키는 '너·네가·너를·너한테·네 덕분에' 같은 2인칭은 쓰지 않고, 친밀도 0~2단계면 '발굴자', 3단계 이상이면 닉네임(call_name)으로 말한다 (2026-11-17).
# 조사는 받침에 맞춘다: 네가→발굴자가 / 도윤이, 너는→발굴자는 / 도윤은, 너를→발굴자를 / 도윤을, 너랑→발굴자랑 / 도윤이랑, 네 덕분에→발굴자 덕분에.
# '너무'·'너머'처럼 낱말의 일부인 것과 대답 "네."("네?", 문장 첫머리 "네 알겠어")는 건드리지 않는다. 자기 것을 '네'라고 한 말은 앞서 fix_own_possessive 가 '내'로 고쳤다.
# call_name 이 없으면(None) 바꾸지 않는다. C# NpcLineChecks.PlayerPronounsToName 과 같다
PLAYER_PRONOUN_RE = re.compile(
    "(?<![가-힣])(?:"
    "(?P<ge>네게)(?![가-힣])"
    "|(?P<so>당신|그대|[너네니])(?P<sp>가|는|은|를|을|도|한테서|한테|에게서|에게|이랑|이|랑|과|와|처럼|보다|까지|부터|만|밖에|라서|라고|의)(?![가-힣])"
    "|(?P<neun>넌)(?![가-힣])|(?P<reul>널)(?![가-힣])"
    "|(?P<bare>너(?=[\\s,.!?…~]|$)|[네니](?=\\s+[가-힣]))"
    ")")
PLAYER_PARTICLES = {  # 조사: (받침 없는 이름 뒤, 받침 있는 이름 뒤)
    "가": ("가", "이"), "이": ("가", "이"), "는": ("는", "은"), "은": ("는", "은"), "를": ("를", "을"), "을": ("를", "을"), "이랑": ("랑", "이랑"), "랑": ("랑", "이랑"),
    "과": ("와", "과"), "와": ("와", "과"), "라서": ("라서", "이라서"), "라고": ("라고", "이라고"),
}


# 문장 첫머리의 "네" 뒤에 이런 낱말이 오면 대답이 아니라 '너의'다
POSSESSED_WORDS = ("덕분", "손길", "생각", "마음", "말", "도움", "관심", "정성", "얼굴", "이름", "목소리")


# 플레이어에게 플레이어를 3인칭으로 말하지 않는다 (2026-11-17 22:27 시험 뒤: "오랜만에 도윤을 찾아줘서"처럼 '너를'을 이름으로 바꾸자 이름이 대사 안에 남 얘기처럼 들어갔다).
# 주어·목적어·여격(너가·너를·너는·너한테·네게)은 말이 이어질 때 이름으로 바꾸지 않고 지운다(한국어는 주어·목적어를 줄여 말한다: "너를 찾아줘서 고마워"→"찾아줘서 고마워").
# 줄일 수 없는 꼴(너랑·너처럼·너도…)과 부르는 말("고마워, 너!")은 예전처럼 이름으로 바꾼다. "네 덕분에"는 "덕분에"로. C# NpcLineChecks.PlayerPronounsToName 과 같다
PLAYER_DROP_PARTICLES = ("가", "이", "는", "은", "를", "을", "한테서", "한테", "에게서", "에게", "의")

# 꾸밈말(관형형) 뒤의 플레이어 말은 지우지 않는다. "처음 온 너를 만나니 기쁘다"에서 '너를'을 지우면 꾸밈말만 남아 "처음 온 만나니 기쁘다"가 됐다
# (2026-11-18 20:22 시험 S05 #1). 지우지 않은 말은 이름으로 바뀌어("처음 온 발굴자를") 출력 검사 player_third_person 이 거르고 다시 만든다.
# 꾸밈말: 받침 ㄴ·ㄹ로 끝나는 앞 낱말("온"·"만난"·"반가운"·"보고 싶던"·"올"). 조사('나는'·'에선'…)와 줄인 말('난'·'널'·'그건'), ㄴ·ㄹ로 끝나는 부사·때 낱말은 뺀다 ('만난'의 '난'은 꾸밈말).
# '-은·-는'은 끝 글자로는 조사('나는')와 꾸밈말('잘 아는 너를')을 가를 수 없다 → 앞말이 대명사·때 낱말·흔한 사람 낱말(TOPIC_STEMS)일 때만 조사로 본다 (CodeRabbit 리뷰).
# C# NpcLineChecks.AfterModifier 와 같다
NON_MODIFIER_LAST = set("을를엔선만란론")
TOPIC_STEMS = {
    "나", "너", "저", "우리", "그", "이", "그것", "이것", "저것", "그거", "이거", "오늘", "내일", "지금", "요즘", "이번", "이제", "사실", "실",
    "여기", "거기", "저기", "처음", "다음", "정말", "진짜", "친구", "도깨비", "발굴자", "주인", "손님", "사람",
}
# ㄴ·ㄹ 받침으로 끝나는 부사·때 낱말과 줄인 말 ("사실 너를 기다렸어"는 예전처럼 '너를'을 지운다 — CodeRabbit 리뷰)
NON_MODIFIER_WORDS = {"오늘", "내일", "정말", "참말", "늘", "얼른", "한번", "이번", "우선", "일단", "잘", "그만",
                      "사실", "제일", "매일", "종일", "잠깐", "물론", "조만간",
                      "난", "넌", "날", "널", "건", "걸", "그건", "이건", "저건", "그걸", "이걸", "땐", "그땐", "이젠"}
TRAILING_HANGUL_WORD_RE = re.compile(r"([가-힣]+)\s+$")
# "너를 위한 거야"·"도윤을 위해 준비했어"도 지우면 "위한 거야"만 남는다
FOR_WHOM_RE = re.compile(r"\s+위[해한하]")


def _after_modifier(line, index):
    """line 의 index 자리 바로 앞 낱말이 꾸밈말(관형형)인지 ("처음 온 너를"의 '너를' 자리 → True, "오늘 너를" → False)."""
    m = TRAILING_HANGUL_WORD_RE.search(line[:index]) if line and index > 0 else None
    if not m:
        return False
    word = m.group(1)
    final = (ord(word[-1]) - 0xAC00) % 28
    if final not in (4, 8) or word[-1] in NON_MODIFIER_LAST or word in NON_MODIFIER_WORDS:
        return False
    return not (word[-1] in "은는" and word[:-1] in TOPIC_STEMS)


def player_pronouns_to_name(line, call_name):
    if not line or not call_name:
        return line
    batchim = _has_batchim(call_name[-1])
    dropped = []

    def particle(p):
        pair = PLAYER_PARTICLES.get(p)
        return pair[1 if batchim else 0] if pair else p

    def can_drop(m):
        # 뒤에 이어지는 말이 있어야 지워도 문장이 남는다. 꾸밈말 뒤("처음 온 너를")는 지우면 꾸밈말만 남는다
        following = line[m.end():]
        return re.match(r"\s+[가-힣]", following) is not None and not FOR_WHOM_RE.match(following) and not _after_modifier(line, m.start())

    def sub(m):
        if m.group("ge"):
            if can_drop(m):
                dropped.append(1)
                return ""
            return call_name + "한테"
        if m.group("so"):
            sp = m.group("sp")
            if sp in PLAYER_DROP_PARTICLES and can_drop(m):
                dropped.append(1)
                return ""
            return call_name + particle(sp)
        if m.group("neun"):
            if can_drop(m):
                dropped.append(1)
                return ""
            return call_name + particle("는")
        if m.group("reul"):
            if can_drop(m):
                dropped.append(1)
                return ""
            return call_name + particle("를")
        word = m.group("bare")
        if word != "너":
            before = line[:m.start()].rstrip()
            if (not before or before[-1] in ".!?…~") and not line[m.end():].lstrip().startswith(POSSESSED_WORDS):
                return word  # 문장 첫머리의 "네 알겠어"는 대답이다. "네 덕분에"·"네 손길이"는 '너의'라서 바꾼다
            if line[m.end():].lstrip().startswith(POSSESSED_WORDS):
                dropped.append(1)
                return ""  # "네 덕분에"·"네 생각이" → "덕분에"·"생각이" (상대의 것을 말하며 상대 이름을 부르지 않는다)
        return call_name

    out = PLAYER_PRONOUN_RE.sub(sub, line)
    return re.sub(r"\s{2,}", " ", out).strip() if dropped else out


def drop_player_reference(line, call_name):
    """대사에 플레이어 이름이 주어·목적어·여격으로 들어가 남 얘기처럼 들리면 그 말을 지운다 ("오랜만에 도윤을 찾아줘서"→"오랜만에 찾아줘서", "발굴자가 날 관리해줘서"→"날 관리해줘서").
    부르는 말("도윤아"·"도윤이,"·"고마워 도윤!")은 두고, 이어지는 말이 있는 경우만 지운다. 꾸밈말 뒤("처음 온 도윤을")는 지우지 않는다(_after_modifier).
    지우면 3글자도 안 남으면 그대로 둔다(검사가 거른다). C# NpcLineChecks.DropPlayerReference 와 같다."""
    if not line or not call_name:
        return line
    c = re.escape(call_name)
    # 꾸밈말 뒤("처음 온 도윤을 만나니")는 지우면 꾸밈말만 남는다 → 두고 player_third_person 이 거른다
    out = re.sub("(?<![가-힣])" + c + "(?:을|를|이|가|은|는|한테서|한테|에게서|에게)\\s+(?=[가-힣])(?!위[해한하])",
                 lambda m: m.group(0) if _after_modifier(line, m.start()) else "", line)
    out = re.sub("(?<![가-힣])" + c + "\\s+(?=덕분)", "", out)
    return out if len(PUNCT_RE.sub("", out)) >= 3 else line


# 주어('이·가')는 뒤에 말이 이어질 때만 본다 — "도윤이,"·"도윤이야"처럼 부르는 말은 통과한다 (꾸밈말 뒤라 지우지 않고 남긴 주어를 거른다, CodeRabbit 리뷰)
PLAYER_THIRD_RE = "(?:(?:을|를|은|는|한테서|한테|에게서|에게|의)(?![가-힣])|(?:이|가)(?=\\s+[가-힣]))"


def player_third_person(line, call_name):
    """고친 뒤에도 남은, 플레이어 이름이 남 얘기처럼 쓰인 말 ("도윤을", "발굴자에게", "처음 온 발굴자가 반가워"). 없으면 None. C# NpcLineChecks.PlayerThirdPerson 과 같다."""
    if not line or not call_name:
        return None
    found = re.search("(?<![가-힣])" + re.escape(call_name) + PLAYER_THIRD_RE, line)
    return found.group(0) if found else None


# "덕분에 관리해 줘서 고마워"는 고마움을 두 번 말해 어색하고 금칙어(`덕분에 … 줘서`)에도 걸린다. 첫 낱말 "덕분에"를 받은 모델이 이어 쓰는 말이라(2026-11-17 시험 S08 #4)
# "관리해 줘서 고마워"로 줄인다. C# NpcLineChecks.FixThanksOverlap 과 같다
THANKS_OVERLAP_RE = re.compile(r"덕분에\s*(관리해|닦아)\s?(줘서)")


# 관리는 늘 플레이어가 한다. 도깨비가 자기를 관리한 쪽으로 말한 주어("개운하게 도깨비가 관리해줘서 고마워", "내가 닦아 줘서")는 지운다 — 주어를 빼면 받은 쪽에서 고마워하는 말이 된다 (2026-11-19). C# NpcLineChecks.FixCareGiver 와 같다
SELF_CARE_GIVER_RE = re.compile(r"(?<![가-힣])(?:도깨비(?:가|는|께서)|내가|나는|난)\s?(?=(?:관리|닦|챙겨|챙기|돌봐|돌보|깨워|씻겨)[가-힣]*\s?(?:줘서|주어서|줬|준|주니))")
# 실화 이야기는 늘 도깨비가 들려주고 플레이어는 듣는다. "자개 반짝임 얘기해줘서 고마워"처럼 플레이어가 이야기한 것으로 말하면 "얘기 들어줘서"로 바꾼다 (2026-11-19). C# NpcLineChecks.FixStoryTeller 와 같다
# 고마워하는 꼴("~해 줘서·주어서·주니")만 본다 — "내가 이야기를 들려줬잖아"는 도깨비가 들려준 것을 바르게 말했다 (CodeRabbit 리뷰)
PLAYER_TOLD_RE = re.compile(r"(얘기|이야기)\s?(?:를\s?)?해(\s?)(줘서|주어서|주니)")
PLAYER_TOLD_TO_RE = re.compile(r"들려(\s?)(줘서|주어서|주니)")
TELLER_IS_SELF_RE = re.compile("(?<![가-힣])(?:내가|나는|난)(?![가-힣])")


def fix_care_giver(line):
    return SELF_CARE_GIVER_RE.sub("", line) if line else line


def fix_story_teller(line):
    if not line:
        return line
    if TELLER_IS_SELF_RE.search(line):
        return line  # 도깨비가 들려준 것을 자기 입으로 말했다
    line = PLAYER_TOLD_RE.sub(r"\1 들어\2\3", line)
    return PLAYER_TOLD_TO_RE.sub(r"들어\1\2", line)


def fix_thanks_overlap(line):
    return THANKS_OVERLAP_RE.sub(lambda m: m.group(1) + " " + m.group(2), line or "")


# 모델이 읽는 글(상황·기억·상대 줄)의 '주인'을 호칭(발굴자·닉네임)으로 바꾼다. 2026-11-17 21:09 시험: "지금 상대: 주인"·"주인이 안부를 물었다"를 읽은 모델이 하인처럼 높임을 쓰고
# ("주인님을 뵙게 되어 반갑습니다", 말투다듬음 58%) "주인을 마주하니"처럼 상대를 남 얘기하듯 말했다. 조사는 받침에 맞춘다(주인이→발굴자가·도윤이). 결과를 다시 검사할 때
# 상황 문장 옮기기도 이 바뀐 글과 견준다. C# NpcLineChecks.OwnerToCall 과 같다
OWNER_WORD_RE = re.compile("(?<![가-힣])주인(?:님)?(께서|께|에게|한테|이랑|이|가|은|는|을|를|의|도|과|와|랑)?(?![가-힣])")
OWNER_PARTICLES = {"께서": ("가", "이"), "이": ("가", "이"), "가": ("가", "이"), "은": ("는", "은"), "는": ("는", "은"), "을": ("를", "을"), "를": ("를", "을"),
                   "이랑": ("랑", "이랑"), "랑": ("랑", "이랑"), "과": ("와", "과"), "와": ("와", "과")}


def owner_to_call(text, call_name):
    if not text or not call_name:
        return text
    batchim = _has_batchim(call_name[-1])

    def sub(m):
        particle = m.group(1)
        if not particle:
            return call_name
        if particle in OWNER_PARTICLES:
            return call_name + OWNER_PARTICLES[particle][1 if batchim else 0]
        return call_name + ("에게" if particle == "께" else particle)

    return OWNER_WORD_RE.sub(sub, text)


# 흔한 오탈자는 다시 만들지 않고 고친다 ("즐기길 바래"→"즐기길 바라", 2026-11-17 21:09 시험 S05 #1). 그 밖의 오타는 출력 검사(typo)가 거른다. C# NpcLineChecks.FixTypos 와 같다
TYPO_FIXES = (("몇일", "며칠"), ("됬", "됐"), ("어떻해", "어떡해"), ("웬지", "왠지"), ("금새", "금세"), ("오랫만", "오랜만"), ("바램", "바람"), ("왠만", "웬만"), ("어의없", "어이없"), ("설겆이", "설거지"), ("않되", "안 되"), ("반갑아", "반가워"))
BARAE_RE = re.compile("(길|기를|기|도록)\\s?바래" + WORD_END)


def fix_typos(line):
    for wrong, right in TYPO_FIXES:
        line = (line or "").replace(wrong, right)
    return BARAE_RE.sub(lambda m: m.group(1) + " 바라", line)


def fix_call_honorific(line, call_name):
    """호칭에 붙은 높임 조사를 반말 조사로: "발굴자께서"→"발굴자가", "도윤님께서"→"도윤이", "…께"→"…한테". 2026-11-17 21:27 시험 64건 중 2건이 어미 섞임('께서')으로 다시 만들어 5초대가 됐다.
    C# NpcLineChecks.FixCallHonorific 와 같다."""
    if not line or not call_name:
        return line
    batchim = _has_batchim(call_name[-1])
    c = re.escape(call_name)
    line = re.sub(c + "(?:님)?께서", call_name + ("이" if batchim else "가"), line)
    return re.sub(c + "(?:님)?께(?![가-힣])", call_name + "한테", line)


def normalize_speech(line, label, name=None, own_things=None, nickname=None, visit_days=None, call_name=None, no_record=False, no_record_variant=0):
    """호칭·자기 이름(name)을 지운 뒤 말투 어미를 고치고, 고치다 새로 생긴 이름("향산이예요"→"향산이야")을 한 번 더 지운다.
    own_things(도깨비 자신의 것)를 '네 자개'처럼 '네'라고 부른 말은 '내'로 고친다. 게임의 NpcLineChecks.NormalizeSpeech 와 같다."""
    line = fix_call_honorific(line, call_name)  # '도윤님께서'를 strip_nim 이 '도윤한테서'로 만들기 전에
    line = strip_nim(line, nickname)
    line = fix_no_record(line, no_record_variant) if no_record else line
    line = fix_long_absence(line, visit_days)
    line = strip_address(guest_to_friend(fix_body_state(fix_own_possessive(line, own_things))), name)
    # 플레이어에게 플레이어를 남 얘기하듯 말하지 않는다 (너를·도윤을 → 지움). 지운 뒤 "오랜만에 찾아줘서"가 드러나므로 기록 고침을 한 번 더 한다
    line = drop_player_reference(player_pronouns_to_name(line, call_name), call_name)
    line = fix_no_record(line, no_record_variant) if no_record else line
    line = fix_long_absence(line, visit_days)
    if not line:
        return line
    # "덕분에, 관리해 줘서 고마워."처럼 첫 낱말 뒤에 쉼표를 찍으면 어색하다 (첫 낱말을 정해 준 뒤 생겼다)
    line = collapse_double_verb(fix_starter_adverb(STARTER_COMMA_RE.sub(r"\1 ", strip_trailing_interjection(drop_repeated_word(drop_repeated_lead(guest_to_friend(strip_address(fix_care_giver(fix_story_teller(fix_thanks_overlap(fix_typos(_apply_speech(line, label))))), name)))))), call_name))
    line = drop_player_reference(player_pronouns_to_name(unify_player_call(fix_statement_question(line), call_name, nickname), call_name), call_name)
    # 말줄임을 합치면 '?'가 어미 바로 뒤로 온다 ("기분이 좋구나...?"→"기분이 좋구나?"). 이때 평서 어미의 '?'를 한 번 더 고친다 (CodeRabbit 리뷰)
    return fix_statement_question(collapse_punctuation(line))


# 연달아 붙은 문장 부호. 호칭·자기 이름을 지운 자리("반가워. 주인님!"→"반가워.!")나 '?'를 '.'로 고친 자리("왔구나?!"→"왔구나.!")에 생긴다 (2026-11-18 시험)
PUNCTUATION_RUN_RE = re.compile("[.,!?~…]{2,}")


def collapse_punctuation(line):
    """연달아 붙은 문장 부호를 하나만 남긴다 ("반가워.!"→"반가워.", "하늘아.~"→"하늘아."). 앞의 끝 부호를 남기고, 쉼표와 끝 부호가 붙었으면 끝 부호를 남긴다("고마워,!"→"고마워!").
    말줄임('…', 점 둘 이상)으로 시작하면 '…' 하나로 바꾼다("그래..."→"그래…", "고마워,..."→"고마워…"). 단 말줄임 뒤에 문장 끝 부호(?·!·~)가 있으면 그 부호를 남긴다
    ("별일 없었어...?"→"별일 없었어?"). '…'는 문장 끝이 아니라서, '…'로 바꾸면 토큰 한도에서 끊긴 대사가 끝난 문장을 잃고 길이X 로 걸린다(CodeRabbit 리뷰).
    C# NpcLineChecks.CollapsePunctuation 과 같다."""
    def one(m):
        marks = m.group(0).replace(",", "")
        if not marks:
            return ","
        if marks[0] == "…" or (marks[0] == "." and len(marks) > 1 and marks[1] in ".…"):
            strong = [c for c in marks if c in "?!~"]
            return strong[0] if strong else "…"
        return marks[0]
    return PUNCTUATION_RUN_RE.sub(one, line or "")


STATE_WORDS = ("흐려", "흐릿", "뿌예", "뿌옜", "봉인")


def mixed_ask_facts(line):
    """흐려진 안부에서 상태("흐려졌어")와 며칠 전("9일 전")을 함께 말했는지. 사실은 하나만 말한다 (ask_variant). C# NpcLineChecks.MixedAskFacts 와 같다."""
    core = PUNCT_RE.sub("", line or "")
    return any(word in core for word in STATE_WORDS) and bool(day_refs(line))


def fix_today_also(line):
    """'오늘도'는 되풀이를 뜻한다. 부탁("…16일 전이라서, 오늘도 향로를 닦아 줘")·기록 없는 인사("오늘도 찾아와 줘서")에는 '오늘은'이 맞다 (2026-11-17 21:27 시험).
    C# NpcLineChecks.FixTodayAlso 와 같다."""
    return (line or "").replace("오늘도", "오늘은")


def keep_one_sentence(line, ask=False):
    """대사를 상황에 맞는 한 문장으로 줄인다 (2026-11-17 20:39 시험: 두 문장을 쓰며 길어져 반복·시간 초과·어색한 이음이 늘었다).
    ask(흐려진 안부)이면 닦아 달라는 말이 든 문장을 남긴다("자꾸 흐려져서 몸이 무거워. 깨끗해질 수 있게 닦아 줄래?" → "깨끗해질 수 있게 닦아 줄래?").
    그 문장이 너무 짧으면(부호를 뺀 글자 8 미만) 첫 문장과 함께 둔다("많이 흐려졌어. 닦아줘."). 부탁하는 문장이 없으면 그대로 둔다(부탁 없음으로 거른다).
    부탁이 아니면 고마움('고마'·'감사')이 든 문장, 없으면 첫 문장을 남기되 그것이 5글자 미만이면 이웃 문장까지 둔다("고마워. 몸이 선명해졌어."). C# NpcLineChecks.KeepOneSentence 와 같다."""
    sentences = sentences_of(line)
    if len(sentences) <= 1:
        return line
    core = lambda text: len(PUNCT_RE.sub("", text))
    if ask:
        for i, sentence in enumerate(sentences):
            if has_ask(sentence):
                # "마지막 관리가 9일 전이었어. 닦아 줄래?"처럼 며칠 전 사실이 앞 문장에 있으면 그 문장과 부탁 문장 둘을 남긴다 (사실을 잃지 않게)
                fact = next((s for s in sentences[:i] if day_refs(s)), None)
                if fact and not day_refs(sentence):
                    return fact + " " + sentence
                return sentence if core(sentence) >= 8 or i == 0 else sentences[0] + " " + sentence
        return line
    # 고마움이 든 문장이 있으면 그것을 남긴다 ("몸이 반짝반짝해져서 기뻐! 주인이 관리해 줘서 고마워!"에서 감사를 잃지 않게). 없으면 첫 문장
    pick = next((i for i, sentence in enumerate(sentences) if "고마" in sentence or "감사" in sentence), 0)
    chosen = sentences[pick]
    if core(chosen) >= 5:
        return chosen
    return sentences[0] + " " + chosen if pick > 0 else chosen + " " + sentences[1]


def reversed_call(line, call_name):
    """플레이어를 목적어로 '부르는' 말 ("발굴자를 불러서 반가워", "도윤을 부르니") — 주인이 도깨비를 부른 상황에서 도깨비가 플레이어를 부른 것처럼 뒤바뀐 말. 없으면 None.
    C# NpcLineChecks.ReversedCall 과 같다."""
    if not line or not call_name:
        return None
    found = re.search(re.escape(call_name) + r"(?:을|를)\s?(?:불러|부르|불렀|부른|부를|만나러|보러|찾으러)", line)
    return found.group(0) if found else None


SELF_TALK_ACTIONS = ("thank_calm", "thank_joyful", "answer_status", "recall_talk")
DISCOVERY_ENDING_RE = re.compile("(?:는구나|로구나|구나|군)(?:[.!?…~]|$)")


def discovery_ending(line, action):
    """자기 이야기(감사·안부·기억)의 문장이 '~구나·~는구나·~군'(남의 일을 알아챈 어미)으로 끝나면 그 문장. 인사("왔구나")는 상대를 보고 하는 말이라 괜찮다.
    "덕분에 기쁨이 넘쳐서 폴짝폴짝 뛰는구나!"처럼 자기를 남 얘기하듯 말한 것을 거른다 (2026-11-17 21:45 시험 S08 #4). C# NpcLineChecks.DiscoveryEnding 과 같다."""
    if action not in SELF_TALK_ACTIONS:
        return None
    for sentence in sentences_of(line or ""):
        if DISCOVERY_ENDING_RE.search(sentence):
            return sentence
    return None


def ending_check(line, hint):
    """문장마다 그 도깨비의 어미로 끝나는지 (어미일치). 어미 규칙이 없거나 대사가 없으면 None."""
    if not hint or not line:
        return None
    return speech_mismatch(line, hint) is None


def shared_run(a, b):
    """두 글이 함께 가진 가장 긴 구절. 띄어쓰기·문장부호는 빼고 본다."""
    a, b = PUNCT_RE.sub("", a), PUNCT_RE.sub("", b)
    best_len, best_end = 0, 0
    prev = [0] * (len(b) + 1)
    for i in range(1, len(a) + 1):
        cur = [0] * (len(b) + 1)
        for j in range(1, len(b) + 1):
            if a[i - 1] == b[j - 1]:
                cur[j] = prev[j - 1] + 1
                if cur[j] > best_len:
                    best_len, best_end = cur[j], i
        prev = cur
    return a[best_end - best_len:best_end]


def overlap(line, other):
    """같은 대사로 볼 만큼 겹치는 구절. 띄어쓰기·문장부호를 빼고 똑같거나, COPY_MIN 글자 넘게 같은 구절이 있으면 그 구절."""
    if not line or not other:
        return ""
    if PUNCT_RE.sub("", line) == PUNCT_RE.sub("", other):
        return PUNCT_RE.sub("", line)
    run = shared_run(line, other)
    return run if len(run) >= COPY_MIN else ""


def copied_example(line, examples):
    """페르소나 예시를 옮긴 구절. 통째로 옮긴 것뿐 아니라 "어흠, 오늘 밤 순찰도 안전하게 마쳤습니다"처럼 예시의 긴 구절을
    가져다 쓴 것도 잡는다. 없으면 ''."""
    for e in examples:
        hit = overlap(line, e)
        if hit:
            return hit
    return ""


def pair_overlap(a, b):
    """이웃한 두 글자 묶음의 겹침 (Dice 계수, 0~1). 띄어쓰기·문장부호를 뺀 글에 쓴다."""
    if len(a) < 2 or len(b) < 2:
        return 1.0 if a == b else 0.0
    counts = Counter(b[i:i + 2] for i in range(len(b) - 1))
    common = 0
    for i in range(len(a) - 1):
        pair = a[i:i + 2]
        if counts[pair] > 0:
            common += 1
            counts[pair] -= 1
    return 2 * common / (len(a) - 1 + len(b) - 1)


def similar(a, b):
    """같은 대사로 볼 만큼 겹치면 그 이유, 아니면 ''. 게임의 NpcLineChecks.Similar 와 같다.

    띄어쓰기·문장부호를 빼고 ① 똑같거나 ② 글자 쌍이 SIMILAR_PAIRS 이상 겹치거나 ③ 두 대사가 함께 가진 가장 긴 구절이 긴 쪽의 CONTAINED_SHARE 이상이거나
    ④ 앞 OPENING_CHARS 글자가 SIMILAR_OPENING 이상 겹치면 같은 대사다. "이틀 전에 닦아 주셔서 고마워요."처럼 꼭
    들어가야 하는 짧은 구절만 같은 것은 같은 대사로 보지 않는다 — 8글자 같은 구절을 기준으로 하면 "안부를 물어주셔서 정말
    고마워요"와 "이틀 전에 닦아 주셔서 정말 고마워요"도 같다고 봤다.
    """
    ca, cb = PUNCT_RE.sub("", a or ""), PUNCT_RE.sub("", b or "")
    if not ca or not cb:
        return ""
    if ca == cb:
        return "같은 대사"
    pairs = pair_overlap(ca, cb)
    if pairs >= SIMILAR_PAIRS:
        return f"글자 쌍 {round(pairs * 100)}% 겹침"
    shared = shared_run(ca, cb)
    if len(shared) >= CONTAINED_MIN and len(shared) >= CONTAINED_SHARE * max(len(ca), len(cb)):
        return f"'{shared}' 가 그대로 들어 있음"
    opening = min(OPENING_CHARS, len(ca), len(cb))
    if opening >= OPENING_MIN and pair_overlap(ca[:opening], cb[:opening]) >= SIMILAR_OPENING:
        return f"시작 '{ca[:opening]}' 이 같음"
    return ""


def find_similar(line, recent):
    """최근 대사 중 같은 대사가 있으면 (그 대사, 이유), 없으면 None."""
    for other in recent or []:
        why = similar(line, other)
        if why:
            return other, why
    return None


# 인사 행동의 대사는 부호를 뺀 글자가 이 수보다 적으면(안녕!, 안녕.) 인사말뿐이라 거른다. C# NpcOutputValidator 의 TooPlain 과 같다
PLAIN_CHECKED_ACTIONS = ("greet", "wander_greet", "thank_calm", "thank_joyful")
MIN_GREETING_CORE = 4


# 방문 횟수와 어긋나는 말. 7번 온 단골에게 "처음 온 친구라서 반가워", 처음 온 친구에게 "또 왔구나"라고 했다(2026-11-12 11:45 시험, S13).
# C# NpcLineChecks.HistoryContradiction 과 같다. 부호·띄어쓰기를 뺀 대사에서 찾는다
REGULAR_VISITS = 2  # 이 횟수 이상 온 손님은 단골 (C# RuleBasedReactionGenerator.RegularVisitorDays)
REGULAR_ONLY_WORDS = ("처음",)
# '오랜만'·'오래간만'은 기록이 없는 주인 장면에도 걸린다 (아래 no_record_contradiction)
FIRST_ONLY_WORDS = ("또왔", "다시왔", "자주왔", "오랜만", "오래간만", "단골", "다시만나", "또만나", "또보네", "다시보네", "또보는", "또봐서", "또뵈")


def history_contradiction(line, visits):
    """손님 방문 횟수(visits)와 어긋나는 낱말. 단골(2번 이상)이면 '처음', 첫 방문이면 '또·다시·오랜만·단골'. 없으면 None."""
    if visits is None:
        return None
    core = PUNCT_RE.sub("", line or "")
    for word in (REGULAR_ONLY_WORDS if visits >= REGULAR_VISITS else FIRST_ONLY_WORDS):
        if word in core:
            return word
    return None


DAY_REF_RE = re.compile(r"(?<![0-9])([0-9]+)\s?일\s?전|어제|그저께|그제|엊그제|이틀\s?전|사흘\s?전|나흘\s?전|닷새\s?전|엿새\s?전|이레\s?전|일주일\s?전|열흘\s?전|오늘")
DAY_WORDS = {"오늘": 0, "어제": 1, "그저께": 2, "그제": 2, "엊그제": 2, "이틀전": 2, "사흘전": 3, "나흘전": 4, "닷새전": 5, "엿새전": 6,
             "이레전": 7, "일주일전": 7, "열흘전": 10}


def day_refs(text, include_today=False):
    """글에 나온 '며칠 전' 표현의 (표현, 날수). 오늘=0, 어제=1, 이틀 전=2, N일 전=N. include_today 가 False 면 '오늘'은 뺀다("오늘 밤"은 날짜가 아니다).
    C# NpcLineChecks.DayRefs 와 같다."""
    found = []
    for m in DAY_REF_RE.finditer(text or ""):
        days = int(m.group(1)) if m.group(1) else DAY_WORDS.get(re.sub(r"\s", "", m.group(0)), -1)
        if days >= 0 and (include_today or days > 0):
            found.append((m.group(0), days))
    return found


def recorded_days(scene):
    """자료에 적힌 실제 날수: 마지막 관리, 방문한 날, 대화 기억, 상황 문장의 "N일 전". C# NpcOutputValidator.RecordedDays 와 같다."""
    days = set()
    last = fact_of(scene, "last_care_days_ago")
    if last is not None and last.lstrip("-").isdigit():
        days.add(int(last))
    days |= {d for _, d in day_refs(fact_of(scene, "visit_days"), True)}
    days |= {d for _, d in day_refs(scene.get("event"), True)}
    days |= {m["daysAgo"] for m in scene.get("memory", [])}
    return days


def day_ref_contradiction(line, allowed):
    """대사의 "어제"·"이틀 전"·"5일 전"이 자료의 실제 기록에 없으면 그 표현. C# NpcLineChecks.DayRefContradiction 과 같다."""
    for text, days in day_refs(line):
        if days not in allowed:
            return text
    return None


# 시간대 낱말: 앞이 한글이면 낱말의 일부("지난밤", "한낮")라 뺀다. 뒤는 '이야·에·도·까지·처럼'이나 문장 부호·끝일 때만 ('낮잠', '밤바다', '낮은'은 아님)
TIME_WORD_RE = re.compile(r"(?<![가-힣])(새벽|아침|오전|저녁|낮|밤)(?=이야|이네|이라|이니|이지|이었|이구나|이다|에|엔|도|까지|부터|처럼|[\s.!?…~,]|$)")
ALLOWED_TIME_WORDS = {"새벽": {"새벽", "밤"}, "아침": {"아침", "새벽", "오전"}, "오전": {"오전", "아침", "낮"}, "낮": {"낮"}, "저녁": {"저녁"}, "밤": {"밤", "저녁"}}


def time_of_day(hour):
    """서버 시각의 시간대: 새벽(0~5시) · 아침(5~9) · 오전(9~12) · 낮(12~17) · 저녁(17~21) · 밤(21~24). C# NpcContextBuilder.TimeOfDay 와 같다."""
    return "새벽" if hour < 5 else "아침" if hour < 9 else "오전" if hour < 12 else "낮" if hour < 17 else "저녁" if hour < 21 else "밤"


def time_contradiction(line, tod):
    """지금 시간대와 어긋나는 시간대 낱말. 낮인데 "좋은 밤", 자료에 시간대가 없는데 "오늘 밤". 없으면 None. C# NpcLineChecks.TimeContradiction 과 같다."""
    allowed = ALLOWED_TIME_WORDS.get((tod or "").strip(), set())
    for m in TIME_WORD_RE.finditer(line or ""):
        if m.group(1) not in allowed:
            return m.group(1)
    return None


def address_used(line, scene):
    """대사에 실제로 나온 호칭: '닉네임' · '발굴자' · '둘 다' · '없음'. 호칭은 꼭 넣지 않아도 되므로 '없음'도 정상이다."""
    core = PUNCT_RE.sub("", line or "")
    nick = PUNCT_RE.sub("", scene.get("nickname") or "")
    has_nick = bool(nick) and nick in core
    has_stranger = STRANGER in core
    if has_nick and has_stranger:
        return "둘 다"
    return "닉네임" if has_nick else "발굴자" if has_stranger else "없음"


def address_contradiction(line, scene):
    """호칭이 규칙과 다르면 그 말. 친밀하면 닉네임으로 부르고 '발굴자'는 쓰지 않으며, 아니면 '발굴자'로 부르고 닉네임은 쓰지 않는다.
    둘 다 꼭 들어갈 필요는 없다. C# NpcLineChecks.AddressContradiction 과 같다."""
    core = PUNCT_RE.sub("", line or "")
    nickname = scene.get("nickname") or ""
    use_nickname = intimacy(scene) >= NICKNAME_LEVEL and bool(nickname)
    if use_nickname and STRANGER in core:
        return STRANGER
    nick = PUNCT_RE.sub("", nickname)
    if not use_nickname and nick and nick in core:
        return nickname
    return None


PERSONA_COPY_MIN = 8  # 대사가 페르소나 설명문과 이만큼(글자) 넘게 같은 구절을 이으면 설명문을 옮긴 것 (C# NpcLineChecks.PersonaCopyMin)


def persona_copy(line, persona):
    """대사가 페르소나의 설명 칸(성격·상대를 대할 때·관리를 받으면·가치관·감정 표현·말투)이나 예시 대사를 옮겼으면 겹친 구절.
    프롬프트에 실린 설명을 작은 모델이 대사로 읊었다(2026-11-13: "가장 친한 동료처럼 반긴다"). C# NpcLineChecks.CopiedFromPersona 와 같다."""
    core = PUNCT_RE.sub("", line or "")
    texts = [persona.get(key) or "" for key in ("toOwner", "toVisitor", "onCare", "values", "emotionStyle", "traits", "speechStyle")]
    for other in (PUNCT_RE.sub("", t) for t in texts + list(persona.get("examples") or [])):
        if len(core) < PERSONA_COPY_MIN or len(other) < PERSONA_COPY_MIN:
            continue
        shared = shared_run(core, other)
        if len(shared) >= PERSONA_COPY_MIN:
            return shared
    return None


# 2026-11-18 20:22 시험 S14 #1: "오, 자개가 정말 정성스럽게 만들어졌네."가 통과했다 — '정성스럽게'·'만들어'와 입음꼴 끝맺음('~어졌네')을 몰랐다
CRAFT_RE = re.compile("공들|정성껏|정성스럽|정성 ?들|빚[어은]|새[긴겨]|칠한|세공|깎[은아]|만든|만들어|다듬[어은]|꾸[며민]")
# 구경하는 끝맺음: '~거네'·'~거구나', 남이 만든 것처럼 말하는 입음꼴('만들어졌네'·'새겨져 있네'·'다듬어졌어'·'완성됐네')
SPECTATOR_RE = re.compile("거네|거구나|거로구나|것이네|것이로구나|졌|져 ?있|되었|됐|되어 ?있|돼 ?있")
# 자기 것을 말하는 풀이말은 쉼표나 다른 주어('그림은'·'내가')에서 끝난다. "자개가 반짝이고, 그림은 정성스럽게 완성됐어."의 '완성됐어'는 그림 이야기다 (CodeRabbit 리뷰)
SUBJECT_TOKEN_RE = re.compile("[가-힣]+[이가은는]")
ADVERB_YI = {"많이", "같이", "깊이", "높이", "깨끗이", "반듯이", "따뜻이", "일찍이", "굳이", "고이", "가까이", "틈틈이"}  # '-이'로 끝나는 부사 (주어가 아니다)


# 설명문 규칙(forbidden.json narration)이 주어로 보는 낱말과 끝 부호
NARRATION_SUBJECT_RE = re.compile("^(?:도깨비|친구|주인|손님|사람|상대|발굴자|아이)(?:이|가|은|는|도)")
NARRATION_MARKS_RE = re.compile("[.!?…~,]+$")
NARRATION_LABEL = "상황 설명문·몸짓 설명:"


def narration_kept(raw_match, delivered_core):
    """다듬기 전 대사에서 걸린 설명문(공백을 지운 꼴)이 내보낼 대사(공백을 지운 꼴)에 남아 있는지. 주어('발굴자가')는 다듬기가 지웠을 수 있어 빼고 견준다
    ("발굴자가그림을그리고있다." → "그림을그리고있다"). 남은 말이 네 글자보다 짧으면 보지 않는다. C# NpcLineChecks.NarrationKept 와 같다."""
    if not raw_match or not delivered_core:
        return False
    rest = NARRATION_MARKS_RE.sub("", NARRATION_SUBJECT_RE.sub("", raw_match))
    return len(rest) >= 4 and rest in delivered_core


def _own_predicate(rest):
    """자기 것 뒤에 이어진 말에서, 그것을 말하는 풀이말만 (문장 끝·쉼표·다른 주어 앞까지). C# NpcLineChecks.OwnPredicate 와 같다."""
    stop = min((rest.find(c) for c in ".!?…," if c in rest), default=-1)
    if stop >= 0:
        rest = rest[:stop]
    at = 0
    for token in rest.split(" "):
        if len(token) >= 2 and SUBJECT_TOKEN_RE.fullmatch(token) and token not in ADVERB_YI and not CRAFT_RE.search(token):
            return rest[:at]
        at += len(token) + 1
    return rest


# 남의 솜씨를 보고 하는 칭찬 ("대단하네", "멋지구나"). 자기 이야기에서 만든 솜씨 낱말과 함께 나오고 '내'가 없으면 자기 것을 남의 솜씨처럼 말한 것이다:
# "정말? 그렇게 공들인 거라면 대단하네." (2026-11-18 23:45 시험 S14 #3 — 자기 것의 이름이 없어 subject_contradiction 이 놓쳤다). C# NpcLineChecks.CraftPraise 와 같다
CRAFT_PRAISE_RE = re.compile("(?:대단|훌륭|굉장|근사)(?:하네|하구나|하다|하군|해)|(?:멋지|놀랍)(?:네|구나|다|군)|멋있(?:네|구나|다|군|어)|멋져|놀라워|잘 ?만들었(?:네|구나|다)")
# 상대가 해 준 관리를 칭찬한 것("정성껏 닦아 줘서 고마워, 정말 멋져!")은 자기 솜씨 칭찬이 아니다 (CodeRabbit 리뷰)
CARE_THANKS_RE = re.compile("(?:닦|관리|챙|돌봐|돌보|씻|깨워)[가-힣 ]{0,4}(?:줘서|주어서|줬|준|주니|받아서|받으니)")
SELF_MARKER_RE = re.compile("(?<![가-힣])(?:내|나|나의|내가|나는|난|우리)(?![가-힣])")


def craft_praise(line, action):
    """자기 이야기(관리 감사·안부·지난 대화)에서 자기 솜씨를 남의 솜씨 보듯 칭찬한 문장이면 그 칭찬 말, 아니면 None."""
    if not line or action not in SELF_TALK_ACTIONS:
        return None
    for sentence in re.split(r"(?<=[.!?…~])\s+", line):
        praise = CRAFT_PRAISE_RE.search(sentence)
        if praise and CRAFT_RE.search(sentence) and not SELF_MARKER_RE.search(sentence) and not CARE_THANKS_RE.search(sentence):
            return praise.group(0)
    return None


def subject_contradiction(line, own_things):
    """주체가 어긋난 대사의 그 낱말, 아니면 None. 도깨비가 자기 것(ownThings: 자개·옻칠…)을 '내 ~' 없이 남의 솜씨를 구경하듯 말하는 것을 잡는다
    ("오, 자개가 정말 공들여 만든 거네 덕분에 빛나네." — 2026-11-16 시험 S14). 자기 것이 주어(이·가·은·는)이고, 같은 문장에 만든 솜씨 낱말
    (공들·정성껏·정성스럽게·빚어·새겨·만들어…)과 구경하는 끝맺음(~거네·~거구나, 입음꼴 ~어졌네·~됐네)이 함께 있고, 앞에 '내'·'나의'·'우리'가 없을 때만 건다
    ("자개가 정성스럽게 만들어졌네." — 2026-11-18 시험 S14). '몸'·'모습'은 거의 모든 문장에 나오는 낱말이라 뺀다. C# NpcLineChecks.SubjectContradiction 과 같다."""
    for thing in own_things or []:
        if not thing or thing in ("몸", "모습"):
            continue
        start = 0
        while True:
            at = line.find(thing, start)
            if at < 0:
                break
            start = at + len(thing)
            if start >= len(line) or line[start] not in "이가은는":
                continue
            before = line[:at].rstrip()
            if before.endswith(("내", "나의", "우리", "제")):
                continue
            rest = _own_predicate(line[start + 1:])
            if CRAFT_RE.search(rest) and SPECTATOR_RE.search(rest):
                return thing
    return None


CRAFT_NE_RE = re.compile("(었|았|였|했)네([.!?…~]|$)")


# 지난 대화를 "~던 일"로 끝내면 말이 끝나지 않는다 → "~던 일이 기억나." (앞의 "기억난다,"는 겹치므로 뗀다). C# NpcLineChecks.FixRecallFragment 와 같다
RECALL_FRAGMENT_END_RE = re.compile("던 일([.!…~]*)$")
# 낱말이 끝날 때만 지운다 — "기억나는 건 함께 웃었던 일"의 '기억나'를 지우면 "는 건 …"이 남았다 (CodeRabbit 리뷰)
RECALL_LEAD_RE = re.compile(r"^(?:기억난다|기억나|생각난다|생각나)(?=[,.!\s]|$)[,.!]?\s*")
RECALL_SUBJECT_RE = re.compile("(?:기억|생각)나는 (?:건|게|것은)")


def fix_recall_fragment(line):
    if not line or not RECALL_FRAGMENT_END_RE.search(line):
        return line
    line = RECALL_LEAD_RE.sub("", line)
    # "기억나는 건 함께 웃었던 일"처럼 이미 '기억나는 건'으로 시작했으면 "…던 일이야."로 끝맺는다 ("…던 일이 기억나"를 붙이면 '기억나'가 겹친다)
    ending = "던 일이야" if RECALL_SUBJECT_RE.search(line) else "던 일이 기억나"
    return RECALL_FRAGMENT_END_RE.sub(lambda m: ending + (m.group(1) or "."), line)


def fix_bare_own_thing(line, own_things):
    """자기 솜씨를 말하면서 자기 것을 '내' 없이 맨 이름으로 말하면("기억난다, 자개 공들여 만들었다고 자랑했던 일") 남의 자개처럼 들린다 → '내 자개'. 조사가 없어 주체 검사도 놓쳤다 (2026-11-19 --live-store). C# NpcLineChecks.FixBareOwnThing 과 같다. '몸'·'모습'은 상대의 것일 수 있어 고치지 않는다."""
    if not line or not own_things or not CRAFT_RE.search(line) or SELF_MARKER_RE.search(line):
        return line  # 이미 '내·나·우리'로 자기 것이라고 했으면 그대로
    # 가장 앞의 자기 것 하나에만 붙인다. 주어(이·가·은·는)로 쓴 것은 붙이지 않고 주체 검사(SubjectContradiction)가 거르게 둔다. '빛·반짝임'은 속성이라 뺀다("빛이 비쳐"를 "내 빛이"로 바꾸지 않는다)
    things = [t for t in own_things if t and t not in ("몸", "모습", "빛", "반짝임")]
    found = [m for t in things for m in [re.search("(?<![가-힣])" + re.escape(t) + "(?=(?:을|를|도|의|과|와|랑|만|에|처럼)?(?![가-힣]))", line)] if m]
    if not found:
        return line
    first = min(found, key=lambda m: m.start())
    return line[:first.start()] + "내 " + line[first.start():]


def fix_craft_spectator(line):
    """자기 솜씨를 말하는 문장이 구경하듯 '~었네'로 끝나면 '~었어'로 고친다 ("내 자개 공들여 만들었네" → "…만들었어"). 자기 이야기에는 알아챈 어미를 쓰지 않는다.
    C# NpcLineChecks.FixCraftSpectator 와 같다."""
    parts = []
    for sentence in sentences_of(line or ""):
        parts.append(CRAFT_NE_RE.sub(lambda m: m.group(1) + "어" + m.group(2), sentence) if CRAFT_RE.search(sentence) else sentence)
    return " ".join(parts) if parts else line


def fix_own_possessive(line, own_things):
    """자기 것(ownThings: 자개·옻칠·몸…)을 '네'라고 부르면 '내'로 고친다 ("네 자개는 반짝여" → "내 자개는 반짝여", "너의"→"나의").
    주체를 바꾸지 않는다 — 밤바다가 자기 자개를 "네 자개"라고 했다(2026-11-13). 상대의 것("네 덕분에")은 고치지 않는다.
    C# NpcLineChecks.FixOwnPossessive 와 같다."""
    things = [t for t in (own_things or []) if t]
    if not line or not things:
        return line
    rx = re.compile("(?<![가-힣])(네|니|너의)(\\s?)(" + "|".join(re.escape(t) for t in things) + ")")
    line = rx.sub(lambda m: ("나의" if m.group(1) == "너의" else "내") + m.group(2) + m.group(3), line)
    # "그 자개 공들여 만들었네"·"이 자개는…"처럼 '그·이·저'로 말하면 자개 주인을 플레이어로 가정한 말처럼 들린다(2026-11-17 22:27 시험 S14). 도깨비 자신의 것은 '내'로 말한다.
    # '몸'·'모습'은 상대의 것을 가리킬 수도 있어("그 모습이 보기 좋아") 고치지 않는다
    demo = [t for t in things if t not in ("몸", "모습")]
    if demo:
        line = re.sub("(?<![가-힣])(?:그|이|저)(\\s?)(" + "|".join(re.escape(t) for t in demo) + ")", lambda m: "내" + m.group(1) + m.group(2), line)
        # 앞에 아무것도 없이 자기 것을 말하며 솜씨를 이야기하면("기억난다, 자개 공들여 만들었다고 자랑했던 일") 자개 주인이 남처럼 들린다 (2026-11-19). 같은 마디에 만든 솜씨 낱말이 있으면 '내'를 붙인다. 주어(자개가·자개는)로 쓴 것은 붙이지 않는다 — 남의 솜씨처럼 말한 "자개가 정성스럽게 만들어졌네"는 고치지 않고 주체 검사가 거른다
        bare = re.compile("(?<![가-힣])(?<!내 )(?<!나의 )(?<!우리 )(?<!제 )(" + "|".join(re.escape(t) for t in demo) + ")(?![이가은는](?![가-힣]))"
                          "(?=[^,.!?…~]*?(?:" + CRAFT_RE.pattern + "))")
        line = bare.sub(lambda m: "내 " + m.group(1), line, count=1)
    return line


# 프롬프트의 사실 칸 이름 '몸 상태'를 옮긴 "몸 상태가 선명해졌어"는 자기 몸을 남이 재듯 말한 것이다(2026-11-16 시험 S04 #4). "몸이 선명해졌어"로 고친다.
# C# NpcLineChecks.FixBodyState 와 같다
BODY_STATE_RE = re.compile("몸\\s?상태(가|는|를|도)?")
BODY_STATE_PARTICLES = {"가": "이", "는": "은", "를": "을", "도": "도"}


def fix_body_state(line):
    return BODY_STATE_RE.sub(lambda m: "몸" + BODY_STATE_PARTICLES.get(m.group(1) or "", ""), line or "")


def _syllable(ch):
    return 0xAC00 <= ord(ch) <= 0xD7A3


def _medial(ch):
    return ((ord(ch) - 0xAC00) // 28) % 21


def _final(ch):
    return (ord(ch) - 0xAC00) % 28


LONE_JAMO_RE = re.compile("[ㄱ-ㅎㅏ-ㅣ]")
DOUBLE_PARTICLES = ("을를", "를을", "를를", "을을", "은는", "는은", "에에서", "에서에서")
# 자주 틀리는 철자 (바른 말: 됐·며칠·어떡해·왠지·금세·오랜만·바람·웬만·어이없). '만먹'은 '만드셨'의 높임을 잘못 고쳐 생긴 것이다
MISSPELLED = ("됬", "몇일", "어떻해", "웬지", "금새", "오랫만", "바램", "왠만", "어의없", "설겆이", "않되", "만먹", "엇어", "엇다", "엇군", "엇네")


def typo(line):
    """대사의 오타. 있으면 그 글, 없으면 None. ① 자음·모음만 남은 글(ㅋㅋ) ② 모음 어긋남: 'ㅏ·ㅗ' 뒤에 '었'(쏟었군→쏟았군), 그 밖 모음 뒤에 '았'
    ③ 'ㄹ께·ㄹ꺼·ㄹ껄'(할께→할게) ④ 겹조사(을를·은는) ⑤ 자주 틀리는 철자. 2026-11-13 시험: "정성을 쏟으셨군요"의 높임을 뺀 자리에 "쏟었군",
    "만드셨는지"가 "만먹었는지"로 나왔다. C# NpcLineChecks.Typo 와 같다."""
    if not line:
        return None
    jamo = LONE_JAMO_RE.search(line)
    if jamo:
        return f"자음·모음만 남음 '{jamo.group(0)}'"
    for i in range(1, len(line)):
        c, p = line[i], line[i - 1]
        if not _syllable(p):
            continue
        if c == "었" and _final(p) != 20 and takes_a(p):
            return p + "었"
        if c == "았" and _final(p) != 20 and not takes_a(p):
            return p + "았"
        if c in "께꺼껄" and _final(p) == 8:
            return p + c
    # 띄어쓰기를 지우지 않고 본다 ("줄기는 은이나"의 '는 은'은 겹조사가 아니다)
    for pair in DOUBLE_PARTICLES + MISSPELLED:
        if pair in line:
            return pair
    return None


DANGLING_CONNECTIVE_RE = re.compile("(서|며|면서|려고|도록|다가|지만|으나|니까|면)$")  # '~고'는 "됐고."처럼 문장을 끝내는 말로도 쓴다
DANGLING_PARTICLE_RE = re.compile("(은|는|을|를|의)$")
QUOTATIVE_RE = re.compile("(다고|라고|냐고|자고|다니까|라니까|자니까|냐니까)$")
COMPLETE_WORDS = {"여기서", "거기서", "저기서", "어디서", "어서", "그리고", "그러니까", "그러면", "그럼", "아니면", "이러면", "그래서"}


def incomplete_ending(line, address=None):
    """마지막 문장이 끝맺지 못했으면 그 낱말, 아니면 None. 이어 주는 말(~서·~고·~며·~니까·~면…)이나 조사(~은·~는·~을·~를·~의)로 끝난 대사다
    ("꼬리까지 깨끗하게 관리해 줘서"). 끝의 '히힛!' 같은 의성어를 뗀 뒤에 보므로 그 앞이 끊긴 말이면 거른다. 인용("~다고")과 "여기서"·"어서" 같은
    낱말은 끝맺은 것으로 본다. C# NpcLineChecks.IncompleteEnding 과 같다."""
    sentences = sentences_of(line)
    if not sentences:
        return None
    word = VOCATIVE_RE.sub("", TRAILING_RE.sub("", sentences[-1])).split(" ")[-1]
    if len(word) < 2 or word in COMPLETE_WORDS or QUOTATIVE_RE.search(word):
        return None
    if address and word in (address, address + "아", address + "야"):
        return None  # "어서 와, 다은."처럼 닉네임으로 끝나는 부르는 말의 끝 글자('은')는 조사가 아니다 (CodeRabbit 리뷰)
    if DANGLING_CONNECTIVE_RE.search(word) or DANGLING_PARTICLE_RE.search(word):
        return word
    # 문장 부호 없이 '기분이'·'내가'로 끝나면 뒤가 잘린 것이다 (끝의 "히히!"를 떼고 남은 말, 2026-11-13 시험: "…예뻐 보여서 기분이")
    terminated = bool(re.search("[.!?…~。]$", sentences[-1].rstrip()))
    return word if not terminated and re.search("(이|가|도|에|로|와|과|랑)$", word) else None


# 흐려지거나 재봉인된 안부에서는 닦아 달라는 부탁을 꼭 한다 ("조금 흐려졌어. 마지막 관리는 9일 전이었어."로 끝나면 부탁이 빠진 것, 2026-11-14 시험 S03 #3)
ASK_WORDS_RE = re.compile("닦(?!아 ?(줘서|준))|깨워(?! ?(줘서|준))|깨우|부탁|도와|손길|돌봐|씻|문질|관리해 ?(줘|줄|주|달)|관리 ?좀")


# 부탁 낱말(닦·깨워…)만 있으면 "스스로 닦아야 해"(지어낸 설정)도 부탁으로 봤다 (2026-11-17 21:45 시험 S12 #4). 부탁하는 꼴(~줘·~줄래·~달라·~좋겠·~필요…)이 함께 있어야 한다
# "닦아 줄까?"·"닦아 주겠어"는 도깨비가 닦아 주겠다는 제안이라 부탁이 아니다 (CodeRabbit 리뷰)
ASK_REQUEST_RE = re.compile("줘(?!서)|줄래|주면|줄 수|주길|주렴|주라|달라|부탁|필요|바라|좋겠")


# 흐려진 안부에서 모델이 쓰는 부분은 "(말머리) 닦아 줄래?" 꼴이어야 한다 (2026-11-17 22:27 시험). 자유롭게 쓰게 두자 "조심조심 들어와, 내 마음을 좀 닦아줄래?"·"괜찮으면 도와줄래?"·
# "이왕이면 닦아줄게, 몸이 많이 흐려 보여서 걱정이야."·"마지막 관리가 16일 전이었어. 조심조심 찾아와 줘서 고마워, 잠깐 깨워줄 수 있을까?"(길이 초과로 잘려 부탁이 사라짐)처럼 어색하거나
# 지어낸 말이 새어 들었다. 사실 문장은 코드가 말하므로, 모델의 부분이 이 꼴이 아니면 코드가 부탁 문장을 채운다(ask_fallback). C# NpcLineChecks.IsCleanAsk 와 같다
CLEAN_ASK_RE = re.compile(
    r"^(?:(?:살살|괜찮으면|시간 되면|조심조심|이왕이면|혹시|천천히|조금|잠깐만|잠깐|한 ?번|좀|꼭|부디|가끔|오늘은)[ ,]*){0,3}"
    r"(?:(?:내 몸을|내 몸|나를|날|몸을|나) ?)?(?:닦아|깨워) ?(?:줘|줄래|줄 ?수 ?있어|줄 ?수 ?있을까|주면 ?좋겠어|주라|주렴)[.?!~]*$")


def is_clean_ask(part):
    return bool(part) and CLEAN_ASK_RE.match(part.strip()) is not None


def has_ask(line):
    """대사에 부탁(닦아·관리·깨워·부탁·도와…)이 부탁하는 꼴로 들어 있는지. C# NpcLineChecks.HasAsk 와 같다."""
    return bool(line) and ASK_WORDS_RE.search(line) is not None and ASK_REQUEST_RE.search(line) is not None


def needs_ask(scene):
    """안부 답하기에서 선명도가 흐려짐·재봉인이면 부탁이 있어야 한다. C# NpcLineChecks.NeedsAsk 와 같다."""
    return (any(c["action"] == "answer_status" for c in scene["candidates"])
            and fact_of(scene, "clarity") in UNCARED_CLARITY)


# 첫 낱말 '개운하게·깨끗하게'(정화 대사, STARTERS clear)는 뒤의 움직임말을 꾸민다 ("개운하게 관리해 줘서 고마워"). 모델이 뒤를 다른 말로 이으면
# "개운하게 덕분에 기분이 좋네"·"개운하게 돌아온 발굴자, 오랜만에 관리해줘서 고마워"처럼 어색하다 (2026-11-18 --live-store 미리 만든 정화 대사). C# NpcLineChecks.FixStarterAdverb 와 같다
# 가로 공백만 본다 — 줄바꿈("개운하게\n덕분에…")은 그대로 두어 줄바꿈 검사가 거르게 한다 (CodeRabbit 리뷰)
STARTER_ADVERB_RE = re.compile(r"^(개운|깨끗)하게[ \t]+([가-힣]+)([ \t]*)")
STARTER_VOCATIVE_RE = re.compile(r"^(개운|깨끗)하게[ \t]+[가-힣]+[ \t]+([가-힣]+)[ \t]*,[ \t]*")
# '~하게'가 꾸밀 수 있는 움직임말. 첫 마디(쉼표·문장 끝 앞) 안에 하나라도 있으면 자연스럽다 ("개운하게 오랜만에 나를 관리해줘서")
ADVERB_FOLLOWERS = ("관리해", "관리받", "닦", "씻", "해", "돌봐", "돌보", "챙겨", "챙기", "만들", "다듬", "정리", "손질", "가꿔", "가꾸", "보살", "치워", "털어",
                    "빛나", "빛날", "반짝", "맑아", "느껴", "깨어", "깨워", "일어", "되", "됐", "돼", "지내", "쉬")


def fix_starter_adverb(line, call_name=None):
    """첫 낱말 '개운하게·깨끗하게'가 꾸밀 움직임말 없이 이어진 대사를 고친다. 뒤가 움직임말이면 그대로("개운하게 관리해 줘서 고마워!").
    뒤가 "돌아온 발굴자,"처럼 꾸밈말 + 부르는 말이면 '개운하게 돌아온'을 지우고(부르는 말이 호칭이 아니면 쉼표까지), 그 밖에는 "개운해,"로 바꾼다("개운해, 덕분에 기분이 좋네")."""
    m = STARTER_ADVERB_RE.match(line or "")
    if not m:
        return line
    nxt = m.group(2)
    clause = re.split("[,.!?…~]", line[m.start(2):], maxsplit=1)[0]
    if any(word.startswith(ADVERB_FOLLOWERS) for word in re.findall("[가-힣]+", clause)):
        return line
    final = (ord(nxt[-1]) - 0xAC00) % 28
    vocative = STARTER_VOCATIVE_RE.match(line)
    if vocative and final in (4, 8) and nxt[-1] not in "은는":
        called, rest = vocative.group(2), line[vocative.end():]
        if rest:
            return called + ", " + rest if call_name and called == call_name else rest
    return m.group(1) + "해, " + line[m.start(2):]


STARTER_COMMA_RE = re.compile(r"^(덕분에|이제|훨씬|한결|역시|요즘|조금|점점|많이|어느새|자꾸|몸이|기분이|개운하게|깨끗하게|마지막|벌써|드디어|어서|살살|괜찮으면|시간 되면|조심조심|혹시|이왕이면),[ \t]*")  # 가로 공백만 (줄바꿈은 검사가 거른다, CodeRabbit 리뷰)


def long_absence_contradiction(line, visit_days):
    """방문 기록에 최근 방문(오늘 말고)이 7일 안에 있으면 "오랜만"·"오래간만"은 어긋난다 (3일 전에도 온 친구에게 "오랜만이야", 2026-11-13 시험 S06).
    visit_days 는 visit_days 사실("오늘, 3일 전, 9일 전"). C# NpcLineChecks.LongAbsenceContradiction 과 같다."""
    gaps = [d for _, d in day_refs(visit_days, True) if d > 0]
    if not gaps or min(gaps) >= 7:
        return None
    core = PUNCT_RE.sub("", line or "")
    return next((w for w in ("오랜만", "오래간만", "오랫만") if w in core), None)


def no_record_contradiction(line, situation):
    """기록이 없는데 있는 척하는 낱말. 주인 장면에서 대화 기억도 마지막 관리 사실도 없으면 '오랜만'·'또 왔'·'다시 만나' 같은 말은 지어낸 것이다
    (2026-11-13 실기기: 말발굽 탭 인사 S01 에서 "오랜만이야"를 두 번). 상황 문장에 그 낱말이 있으면 근거가 있어 걸지 않는다.
    손님 장면은 방문 횟수로 보는 history_contradiction 이 맡는다. C# NpcLineChecks.NoRecordContradiction 과 같다. 없으면 None."""
    core = PUNCT_RE.sub("", line or "")
    basis = PUNCT_RE.sub("", situation or "")
    for word in FIRST_ONLY_WORDS:
        if word in core and word not in basis:
            return word
    return None


def no_record_applies(scene):
    """기록이 없는 주인 장면이라 "오랜만"·"또 왔"을 고쳐야 하는지: 손님 장면이 아니고, 대화 기억·마지막 관리·방문 사실이 없고, 상황 문장에도 그 낱말이 없을 때.
    C# NpcLineChecks.NoRecordApplies 와 같다."""
    if scene.get("input") == "visit" or has_record(scene):
        return False
    basis = PUNCT_RE.sub("", scene.get("event") or "")
    return not any(word in basis for word in FIRST_ONLY_WORDS)


def has_record(scene):
    """주인 장면에 대화 기억이나 마지막 관리 사실이 있는지. C# NpcOutputValidator 의 hasRecord 와 같다."""
    return bool(scene.get("memory")) or fact_of(scene, "last_care_days_ago") is not None or fact_of(scene, "visit_days") is not None


# 대사가 상황 문장을 그대로 옮겼는지 보는 기준 글자 수 (C# NpcLineChecks.SituationCopyMin). 실기기에서 "주인이 너를 탭했다. 화면이 너에게
# 가까이 다가왔다." 같은 상황 설명문이 대사에 섞여 나왔다(2026-11-13). 안부의 기억 요약("암행어사 이야기를")처럼 꼭 쓸 낱말은 이보다 짧다
SITUATION_COPY_MIN = 10


def situation_copy(line, situation, call_name=None):
    """상황 문장과 SITUATION_COPY_MIN 글자 넘게 같은 구절이 이어지면 그 구절, 아니면 None. 띄어쓰기·문장 부호는 뺀다. C# NpcLineChecks.CopiedFromSituation 과 같다.
    call_name 을 주면 상황 문장의 '너를'도 호칭으로 바꾼 것과 견준다 — 대사의 '너를'은 말투 다듬기가 호칭으로 바꾼 뒤라 그대로는 겹침을 놓친다."""
    a = PUNCT_RE.sub("", line or "")
    for text in ((situation, player_pronouns_to_name(situation, call_name), owner_to_call(situation, call_name),
                  player_pronouns_to_name(owner_to_call(situation, call_name), call_name)) if call_name else (situation,)):
        b = PUNCT_RE.sub("", text or "")
        if len(a) < SITUATION_COPY_MIN or len(b) < SITUATION_COPY_MIN:
            continue
        shared = shared_run(a, b)
        if len(shared) >= SITUATION_COPY_MIN:
            return shared
    return None


def scene_visits(scene):
    """손님 장면의 최근 30일 방문 횟수. 손님 장면이 아니거나 없으면 None."""
    if not scene or scene.get("input") != "visit":
        return None
    for fact in scene.get("facts", []):
        if fact.get("key") == "visit_count_30d":
            try:
                return int(fact["value"])
            except (TypeError, ValueError):
                return None
    return None


# 인사(탭·손님 맞이)에 붙은 지어낸 사정·캐묻기 (2026-11-17 22:27 시험): "뭘 찾으러 왔어?"·"무슨 일인지 궁금하네"·"오늘은 좀 일찍 왔구나"·"향로랑 친구들 보고 싶었어?"는
# 도깨비가 모르는 사정을 지어낸 것이다. 인사는 반가움만 말하면 되므로, 이런 마디는 지우고 반가움 마디만 남긴다. 다 지워 남는 말이 없으면 거른다. C# NpcLineChecks.FixGreetingTail·GreetingProbe 와 같다
GREETING_ACTIONS = ("greet", "wander_greet", "wander_recall")
# '찾으러'는 "뭘 찾으러 왔어?"만 거른다. "날 찾으러 왔구나"·"나를 찾으러 왔네"는 반가움이라 둔다 (2026-11-25). C# NpcLineChecks.Probe 와 같다
PROBE_RE = re.compile("무슨|뭘|뭐 |뭐하|왜|궁금|길래|일찍|늦게|(?<!날 )(?<!나를 )(?<!날)(?<!나를)찾으러|하러|급하게|보고 ?싶었|오늘은 좀|처음 온 친구야|어떤 일")
CLAUSE_END = ",，.!?…~"


def clauses_of(line):
    """쉼표·문장 부호에서 마디로 나눈다 (부호는 마디 끝에 붙는다)."""
    out, cur, i = [], "", 0
    while i < len(line):
        cur += line[i]
        i += 1
        if cur[-1] in CLAUSE_END:
            while i < len(line) and (line[i] in CLAUSE_END or line[i] == " "):
                if line[i] != " ":
                    cur += line[i]
                i += 1
            out.append(cur.strip())
            cur = ""
    if cur.strip():
        out.append(cur.strip())
    return out


def fix_greeting_tail(line):
    parts = clauses_of(line or "")
    keep = [p for p in parts if find_probe(p) is None]
    if keep and len(keep) < len(parts) and len(PUNCT_RE.sub("", "".join(keep))) >= 3:
        text = re.sub("[,，]+$", "", " ".join(keep))
        line = text if re.search("[.!?…~]$", text) else text + "."
    if line and re.search(r"\?+$", line) and find_probe(line) is None and re.search("반가|환영|어서|안녕|왔", line):
        line = re.sub(r"\?+$", "!", line)  # "안녕, 반가워?"는 묻는 말이 아니라 인사다
    return line


def greeting_probe(line, action):
    """인사 행동의 대사에 지어낸 사정·캐묻기가 남았으면 그 말 (남은 것이 없으면 None)."""
    if action not in GREETING_ACTIONS or not line:
        return None
    return find_probe(line)


def find_probe(text):
    """지어낸 사정·캐묻기 말 (없으면 None). 가로 띄어쓰기가 여럿 이어지면 하나로 보고 찾는다 ("나를  찾으러"도 반가움). C# NpcLineChecks.FindProbe 와 같다."""
    if not text:
        return None
    found = PROBE_RE.search(re.sub(r"[ \t]+", " ", text))
    return found.group(0) if found else None


def game_problem(row, persona, recent, scene=None, variation=0, max_len=None, reevaluate=None):
    """게임의 출력 검사(NpcOutputValidator, 페르소나를 받은 것)에 걸리는 이유. 통과하면 None. 검사 순서도 같다.
    recent 는 그 도깨비가 최근에 띄운 대사다 (게임의 ReactionRequest.RecentLines). variation 은 그 요청의 번호(게임의 ReactionRequest.Variation)다.
    흐려진 안부의 부탁 부분이 최근 대사와 같으면 row["line"] 을 겹치지 않는 코드의 부탁 문장으로 바꾸고(row["ask_filled"]) 통과시킨다 — C# 출력 검사도 대사를 바꿔 돌려준다."""
    if not row.get("json_ok"):
        return "JSON 아님"
    if not row.get("action_ok"):
        return f"후보 밖 행동 '{row['action']}'"
    if not row.get("target_ok"):
        return f"대상 틀림 '{row['target']}'"
    line = row.get("line", "")
    if not line:
        return "빈 대사"
    if not row.get("newline_ok"):
        return "줄바꿈"
    if not row.get("length_ok"):
        return "길이 초과"
    if not row.get("bad_ok"):
        return f"금칙어 {row['bad_hits']}"
    if not row.get("foreign_ok", True):
        return f"영문·가나·한자가 섞임 '{line}'"
    if scene is not None and needs_ask(scene) and not has_ask(line):
        return f"닦아 달라는 부탁이 없다 '{line}'"
    if scene is not None and needs_ask(scene) and mixed_ask_facts(line):
        return f"상태와 며칠 전을 함께 말함 '{line}'"
    discovered = discovery_ending(line, row.get("action"))
    if discovered:
        return f"자기 이야기에 발견 어미(~구나) '{discovered}'"
    probe = greeting_probe(line, row.get("action"))
    if probe:
        return f"인사에 지어낸 사정·캐묻기 '{probe}' ← '{line}'"
    wrong_spelling = typo(line)
    if wrong_spelling:
        return f"오타 '{wrong_spelling}'"
    dangling = incomplete_ending(line, address_name(scene) if scene is not None else None)
    if dangling:
        return f"문장이 끝나지 않음 '{dangling}'"
    if scene is not None and scene.get("input") == "visit":
        contradiction = history_contradiction(line, scene_visits(scene)) or long_absence_contradiction(line, fact_of(scene, "visit_days"))
    elif scene is not None and not has_record(scene):
        # 주인 장면: 대화 기억도 마지막 관리 사실도 없는데 '오랜만'이라고 하면 지어낸 말이다
        contradiction = no_record_contradiction(line, scene.get("event"))
    else:
        contradiction = None
    if not contradiction and scene is not None:
        # "이틀 전"·"어제"·"5일 전"은 자료(마지막 관리·방문한 날·기억·상황 문장)의 실제 기록에 있는 날일 때만 말한다
        contradiction = day_ref_contradiction(line, recorded_days(scene))
    if contradiction:
        return f"기록과 어긋남 '{contradiction}'"
    copied = situation_copy(line, scene.get("event"), address_name(scene)) if scene is not None else None
    if copied:
        return f"상황 문장을 옮김 '{copied}'"
    if scene is not None:
        wrong_time = time_contradiction(line, fact_of(scene, "time_of_day"))
        if wrong_time:
            return f"지금 시간대와 어긋남 '{wrong_time}' (지금 시간대: {fact_of(scene, 'time_of_day') or '알 수 없음'})"
        wrong_address = address_contradiction(line, scene)
        if wrong_address:
            return f"호칭이 규칙과 다름 '{wrong_address}' (부를 이름: {address_name(scene)})"
        reversed_word = reversed_call(line, address_name(scene))
        if reversed_word:
            return f"호칭 주체가 뒤바뀜 '{reversed_word}'"
        third = player_third_person(line, address_name(scene))
        if third:
            return f"플레이어를 남 얘기하듯 부름 '{third}' (부를 때는 '{address_name(scene)}아'처럼 부른다)"
    persona_copied = persona_copy(line, persona)
    if persona_copied:
        return f"페르소나 설명문을 옮김 '{persona_copied}'"
    if self_name_hit(line, persona["name"]):
        return f"자기 이름 '{persona['name']}'"
    wrong_subject = subject_contradiction(line, persona.get("ownThings"))
    if wrong_subject:
        return f"주체가 어긋남 '{wrong_subject}' (자기 것은 '내 {wrong_subject}'처럼 말한다)"
    praised = craft_praise(line, row.get("action"))
    if praised:
        return f"자기 솜씨를 남의 것처럼 칭찬 '{praised}' (자기 것은 '내 ~'라고 말한다)"
    wrong = speech_mismatch(line, persona.get("endingHint"))
    if wrong:
        return f"어미 섞임 '{wrong}'"
    if row.get("action") in PLAIN_CHECKED_ACTIONS and len(PUNCT_RE.sub("", line)) < MIN_GREETING_CORE:
        return f"인사말뿐 '{line}'"
    repeated_word = inner_repeat(line)
    if repeated_word:
        return f"한 대사 안의 반복 '{repeated_word}'"
    # 흐려진 안부는 앞 문장까지 포함한 전체 대사로 견준다 (앞 문장이 같으면 부탁 부분끼리, 다르면 전체끼리). C# NpcOutputValidator 와 같다
    same = find_repeat(line, recent)
    if same and scene is not None and needs_ask(scene):
        # 흐려진 안부의 부탁 부분이 최근 대사와 같으면 거르지 않고, 코드의 부탁 문장 중 겹치지 않는 것으로 바꾼다 (2026-11-25). C# NpcOutputValidator 와 같다.
        # 바꾼 대사는 모델이 쓴 것처럼 다시 평가해(reevaluate) 모든 검사를 거친 뒤 통과한 첫 것만 쓰고, 대사에 매긴 칸(길이·호칭·…)도 그 대사의 것으로 바꾼다.
        # 모델 출력(raw)·토큰 지표는 그대로 둔다. reevaluate 가 없으면 바꾸지 않는다
        shape, rest = ask_prefix_shape(line)
        if shape is not None and reevaluate is not None:
            prefix = line[:len(line) - len(rest)].strip()
            for ask in ask_fallback_candidates(scene, variation, prefix, recent, max_len):
                replaced = dict(row)
                replaced.update(reevaluate(ask))
                if game_problem(replaced, persona, recent, scene, variation, max_len, None) is None:
                    row.update(replaced)
                    row["ask_filled"] = True
                    return None
    if same:
        return f"반복 '{same[0]}' ({same[1]})"
    return None


def ask_fallback_avoiding(scene, variation, prefix, recent, max_len=None):
    """코드의 부탁 문장 중 앞 문장을 붙인 전체 대사가 최근 대사와 겹치지 않고 max_len 안에 드는 첫 것 (요청 번호에서부터 돌린다). 다 겹치거나 길면 None.
    C# PromptAssembler.AskFallbackAvoiding 과 같다 (max_len 은 출력 검사의 길이 제한 = 대사 글자 수 + LENGTH_TOLERANCE)."""
    candidates = ask_fallback_candidates(scene, variation, prefix, recent, max_len)
    return candidates[0] if candidates else None


def ask_fallback_candidates(scene, variation, prefix, recent, max_len=None):
    """ask_fallback_avoiding 이 고를 수 있는 부탁 문장 전부 (고르는 차례대로). C# PromptAssembler.AskFallbackCandidates 와 같다."""
    verb = "깨워" if fact_of(scene, "clarity") in ("재봉인", "RESEALED") else "닦아"
    start = (variation + scene["relicId"]) % len(ASK_FALLBACKS)
    out = []
    for k in range(len(ASK_FALLBACKS)):
        ask = ASK_FALLBACKS[(start + k) % len(ASK_FALLBACKS)].format(v=verb)
        full = prefix + " " + ask
        if (max_len is None or len(full) <= max_len) and find_repeat(full, recent) is None:
            out.append(ask)
    return out


def speech_fixed(said, action, scene, persona, no_record_variant=0):
    """말투 다듬기 → 한 문장 줄이기 → 인사 끝·'오늘도'·자기 솜씨 고치기. C# NpcOutputValidator.SpeechFixed 와 같다."""
    label = (persona.get("endingHint") or {}).get("label")
    line = normalize_speech(said, label, persona["name"], persona.get("ownThings"), scene.get("nickname"), fact_of(scene, "visit_days"), address_name(scene),
                            no_record_applies(scene), no_record_variant)
    # 상황에 맞는 한 문장으로 줄인다 (지난 대화 꺼내기는 기억의 내용이 뒤 문장에 있을 수 있어 건드리지 않는다)
    if action != "recall_talk":
        line = keep_one_sentence(line, needs_ask(scene))
        if needs_ask(scene) or no_record_applies(scene):
            line = fix_today_also(line)
        if action in GREETING_ACTIONS:
            line = fix_greeting_tail(line)
    if action in SELF_TALK_ACTIONS:
        line = fix_bare_own_thing(fix_craft_spectator(line), persona.get("ownThings"))
    if action == "recall_talk":
        line = fix_recall_fragment(line)
    return line


def pick_no_record_variant(said, action, scene, persona, recent, limit=None, truncated=False):
    """기록 없는 '오랜만'을 바꿔 넣은 말 때문에 최근 대사와 같아지면 다른 말(NO_RECORD_GREETINGS)을 고른다. 고칠 '오랜만'이 없거나 다 겹치면 0. C# NpcOutputValidator 와 같다.
    견주는 것은 길이에 맞춰 줄인 뒤의 대사(마지막 반복 검사와 같은 글)이고, 인사에 지어낸 사정이 남는 말은 건너뛴다 (CodeRabbit 리뷰)."""
    if not recent or not no_record_applies(scene) or not LONG_ABSENCE_RE.search(said or ""):
        return 0

    def comparable(variant):
        line = speech_fixed(said, action, scene, persona, variant)
        fitted = fit_sentences(line, limit + LENGTH_TOLERANCE, truncated) if limit is not None and line else None
        return fitted if fitted is not None else line

    if find_similar(comparable(0), recent) is None:
        return 0
    for variant in range(1, len(NO_RECORD_GREETINGS)):
        line = speech_fixed(said, action, scene, persona, variant)
        fitted = fit_sentences(line, limit + LENGTH_TOLERANCE, truncated) if limit is not None and line else line
        if fitted is None:
            continue  # 길이에 맞지 않는 말은 고르면 뒤에서 길이X 로 걸린다 (CodeRabbit 리뷰)
        if greeting_probe(fitted, action) is None and find_similar(fitted, recent) is None:
            return variant
    return 0


def evaluate(obj, scene, persona, limit, forbidden_groups, truncated=False, normalize=True, variation=0, recent=None):
    if obj is None:
        return {"json_ok": False, "action": "", "target": "", "line": ""}
    action = str(obj.get("action", "")).strip()
    target = str(obj.get("target", "")).strip()
    # 행동·대상이 하나뿐인 장면은 모델이 그 칸을 적지 않는다(output_shape). 비어 있으면 후보로 채운다. C# NpcOutputValidator 와 같다
    only_actions = list(dict.fromkeys(c["action"] for c in scene["candidates"]))
    if not action and len(only_actions) == 1:
        action = only_actions[0]
    if not target:
        only_targets = list(dict.fromkeys(t for c in scene["candidates"] if c["action"] == action for t in c["targets"]))
        if len(only_targets) == 1:
            target = only_targets[0]
    said = clean_line(obj.get("line", ""))
    # 게임처럼 검사 전에 흔한 어미를 그 도깨비 말투로 고친다 (--no-normalize 면 고치지 않는다)
    full_line = speech_fixed(said, action, scene, persona, pick_no_record_variant(said, action, scene, persona, recent, limit, truncated)) if normalize else said
    prefix = ask_prefix(scene, variation)
    ask_filled = False
    if prefix and full_line:
        if not is_clean_ask(full_line):
            full_line = ask_fallback(scene, variation)  # 모델의 부분에 부탁이 없거나 사실이 둘이면 코드가 부탁을 채운다
            ask_filled = True
        full_line = prefix + " " + full_line  # 코드가 정한 사실 문장 + 모델의 부탁 문장
    # 게임처럼 길면 앞 문장만 남긴다. 첫 문장부터 넘치면 그대로 두고 길이X 로 적는다
    fitted = fit_sentences(full_line, limit + LENGTH_TOLERANCE, truncated) if full_line else None
    line = fitted if fitted is not None else full_line
    pairs = {(c["action"], t) for c in scene["candidates"] for t in c["targets"]}
    expected = scene.get("expected")
    # 장면 금지어에 자기 이름을 더한다 ("반갑게요, 향산이" 같은 자기 이름 부르기). 띄어쓰기는 빼고 찾는다
    forbidden = [SPACE_RE.sub("", w) for w in scene.get("forbidden", []) + [persona["name"], name_stem(persona["name"])]]
    keywords = scene.get("mustMention")
    examples = persona.get("examples")
    hits = forbidden_hits(line, forbidden_groups)
    if normalize:
        # 말투 다듬기가 '네가'를 지우면 "네가 관리를 안 해줘서"의 탓하는 말이 문장으로 안 보인다 — 다듬기 전 대사로도 '지나친 퉁명스러움·무시' 묶음을 본다 (CodeRabbit 리뷰)
        hits += [h for h in forbidden_hits(said, forbidden_groups) if h.startswith("지나친 퉁명스러움·무시:") and h not in hits]
        # 설명문도 같다: "발굴자가 그림을 그리고 있다."에서 '발굴자가'를 지우면 주어가 없어 안 걸린다. 다듬기 전 대사에서 걸린 설명문이 내보낼 대사에 남아 있을 때만 거른다 (CodeRabbit 리뷰)
        line_core = SPACE_RE.sub("", line or "")
        hits += [h for h in forbidden_hits(said, forbidden_groups)
                 if h.startswith(NARRATION_LABEL) and h not in hits and narration_kept(h[len(NARRATION_LABEL):], line_core)]
    copied = copied_example(line, examples) if examples and line else ""
    return {
        "json_ok": True,
        "action": action,
        "target": target,
        "line": line,
        "action_ok": action in {c["action"] for c in scene["candidates"]},
        "target_ok": (action, target) in pairs,
        "expected_ok": action in expected if expected else None,
        "forbidden_ok": not any(w in SPACE_RE.sub("", line) for w in forbidden),
        "mention_ok": any(k in line for k in keywords) if keywords else None,
        "bad_ok": not hits,
        "bad_hits": ", ".join(hits),
        "original_ok": not copied if examples and line else None,
        "copy_hit": "예시:" + copied if copied else "",
        "full_line": full_line if line != full_line else "",
        "speech_fixed": said if said != full_line else "",
        "ask_filled": ask_filled,
        "cut": fitted is not None and fitted != full_line,
        "truncated": truncated,
        "length": len(line),
        "length_ok": fitted is not None and 0 < len(line) <= limit + LENGTH_TOLERANCE,
        # 자르기 전 대사로 본다. 앞 문장만 남기면 뒤의 줄바꿈이 사라져, 게임이 거르는 대사를 통과시켰다 (게임은 자르기 전에 본다)
        "newline_ok": "\n" not in full_line and "\r" not in full_line,
        "foreign_ok": FOREIGN_RE.search(line) is None,
        "ending_ok": ending_check(line, persona.get("endingHint")),
    }


# ---------- 실행 ----------

def run_case(args, model, variant, scene, persona, content, seed, variation=0):
    system, user, schema, situation = build_prompts(args, variant, scene, persona, content, variation)
    raw, metrics = chat(args, model, system, user, schema, seed)
    obj, truncated = parse_json(raw)
    pure = is_pure_json(raw)
    if variant != "two_step" or obj is None:
        return obj, raw, metrics, pure, truncated

    action = str(obj.get("action", ""))
    label = target_label(str(obj.get("target", "")), scene, content["labels"])
    system2, user2 = build_line_prompt(args, scene, persona, situation, meaning(content, action), label)
    raw2, metrics2 = chat(args, model, system2, user2, SCHEMA_LINE, seed)
    obj2, truncated2 = parse_json(raw2)
    merged = dict(obj, line=(obj2 or {}).get("line", ""))
    return merged, f"{raw} || {raw2}", add_metrics(metrics, metrics2), pure and is_pure_json(raw2), truncated2


def warm_up(args, model):
    """첫 호출의 모델 로드 시간이 측정에 섞이지 않게 한 번 불러 둔다."""
    _, m = chat(args, model, "짧게 답한다.", "안녕", None)
    print(f"  준비 완료 — 첫 호출 {m['wall_ms']:.0f}ms (모델 로드 포함)")


def choose_system_mode(args, model):
    """이 모델에 시스템 메시지를 따로 보낼지, 사용자 메시지 앞에 붙일지 정한다 (--system-in-user)."""
    if args.system_mode == "on":
        args.system_in_user.add(model)
        print("  시스템 메시지를 사용자 메시지 앞에 붙여 보낸다 (--system-in-user on)")
        return
    if args.system_mode == "off" or args.backend != "lmstudio":
        return
    dropped, short, long = drops_system(args, model)
    if dropped:
        args.system_in_user.add(model)
        print(f"  시스템 메시지가 빠진다 (입력 토큰: 짧은 시스템 {short}, 긴 시스템 {long}) "
              "→ 사용자 메시지 앞에 붙여 보낸다")
    elif short == 0:
        print("  입력 토큰 수를 알 수 없어 시스템 메시지가 들어가는지 확인하지 못했다. 따로 보낸다")


def make_row(args, content, run_at, model, variant, scene, persona, rep, obj, raw, metrics, pure, error, truncated=False, variation=0, recent=None):
    row = {
        "run_at": run_at, "model": model, "variant": variant, "format": args.format,
        "scene": scene["id"], "spirit": persona["name"], "mode": scene["mode"],
        "input": content["inputs"][scene["input"]], "repeat": rep,
        "intimacy": intimacy(scene), "address_expected": address_name(scene),
        "raw": raw.replace("\r", " ").replace("\n", "\\n"), "error": error, "note": scene.get("note", ""),
        "pure_json": pure, "_hint_label": (persona.get("endingHint") or {}).get("label", ""),
        "system_merged": model in getattr(args, "system_in_user", ()),
    }
    row.update(evaluate(obj, scene, persona, max_chars(args, scene), content["forbidden"], truncated,
                        getattr(args, "normalize", True), variation, recent))
    row["animation"] = content["actions"].get(row["action"], {}).get("animation", "")
    if metrics:
        row.update(metric_fields(metrics))
    return row


def metric_fields(metrics):
    gen_s = metrics["gen_ms"] / 1000
    return {
        "wall_ms": round(metrics["wall_ms"]),
        "load_ms": round(metrics["load_ms"]),
        "prompt_tokens": metrics["prompt_tokens"],
        "gen_tokens": metrics["gen_tokens"],
        "reasoning_tokens": metrics.get("reasoning_tokens", 0),
        "tok_per_s": round(metrics["gen_tokens"] / gen_s, 1) if gen_s > 0 else "",
    }


# 게임의 RuleBasedReactionGenerator 와 같은 기준
RECENT_CARE_DAYS = 3
REGULAR_VISITOR_DAYS = 2
FADED_CLARITIES = {"FADED", "RESEALED", "흐려짐", "재봉인"}


def fact_of(scene, key):
    return next((str(f["value"]) for f in scene["facts"] if f["key"] == key), None)


def pick_fallback(content, scene, recent):
    """기본 대사로 대신할 때 게임이 띄울 대사 (RuleBasedReactionGenerator 를 흉내 낸다). 기본 대사 파일이 없으면 None.
    후보 고르기(손님 방문 횟수 → preferredActions → 첫 후보)·안부 변형·최근 대사 피하기·바로 앞 대사 피하기가 같다. 고르기는 무작위다."""
    data = content.get("default_lines")
    if not data:
        return None
    spirit = next((s for s in data["spirits"] if s["relicId"] == scene["relicId"]), data["fallback"])
    ids = [c["action"] for c in scene["candidates"]]
    action = None
    if scene["input"] == "visit":
        visits = fact_of(scene, "visit_count_30d")
        want = "wander_recall" if visits and visits.isdigit() and int(visits) >= REGULAR_VISITOR_DAYS else "wander_greet"
        action = want if want in ids else None
    action = action or next((a for a in spirit.get("preferredActions", []) if a in ids), ids[0])
    kind = ""
    if action == "answer_status":
        clarity, days = fact_of(scene, "clarity"), fact_of(scene, "last_care_days_ago")
        kind = ("faded" if clarity and clarity.strip() in FADED_CLARITIES
                else "recent_care" if days and days.lstrip("-").isdigit() and int(days) <= RECENT_CARE_DAYS else "default")

    def find(lines_of, variant_name):
        return next((p["lines"] for p in lines_of.get("pools", [])
                     if p["actionId"] == action and p["variant"] == variant_name), None)
    lines = find(spirit, kind) or find(spirit, "") or find(data["fallback"], kind) or find(data["fallback"], "")
    if not lines:
        return None
    key = f"{scene['relicId']}:{action}:{kind}"
    last = content["last_fallback"].get(key)
    # 최근 대사와 같은 것을 먼저 빼고, 그 안에서 바로 앞 대사를 피한다 (게임의 Pick 과 같은 순서)
    pool = [x for x in lines if find_similar(x, recent) is None] or lines
    pool = [x for x in pool if x != last] or pool
    picked = random.choice(pool)
    content["last_fallback"][key] = picked
    return picked


def run_enforced(args, content, model, variant, scene, persona, rep, seed, recent):
    """게임처럼 만든다: 출력 검사(game_problem)에 걸리면 곁들일 거리를 바꿔 다시 만들고(--attempts 번까지),
    그래도 걸리면 게임은 기본 대사로 대신한다(최종 = 기본 대사). 모델 호출 오류는 다시 하지 않는다 (게임도 바로 기본 대사).
    돌려주는 행은 마지막 시도의 것이고, 응답ms·토큰은 모든 시도를 더한 값이다 (플레이어가 기다리는 시간)."""
    run_at = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    rejected, total = [], None
    attempts = max(1, args.attempts)
    for attempt in range(1, attempts + 1):
        try:
            obj, raw, metrics, pure, truncated = run_case(args, model, variant, scene, persona, content, seed,
                                                          variation=rep - 1 + attempt - 1)
            error = ""
        except (BackendError, urllib.error.URLError, TimeoutError) as e:
            obj, raw, metrics, pure, truncated, error = None, "", None, False, False, str(e)
        if metrics:
            total = add_metrics(total, metrics) if total else metrics
        row = make_row(args, content, run_at, model, variant, scene, persona, rep, obj, raw, metrics, pure, error,
                       truncated, rep - 1 + attempt - 1, recent)
        variation = rep - 1 + attempt - 1
        # 흐려진 안부의 부탁을 바꿔 넣을 때, 그 대사를 모델이 쓴 것처럼 다시 평가한다 (대사에 매긴 칸만 — 모델 출력·토큰 지표는 그대로)
        reevaluate = (lambda ask, _row=row, _variation=variation: evaluate({"action": _row.get("action", ""), "target": _row.get("target", ""), "line": ask},
                                                                          scene, persona, max_chars(args, scene), content["forbidden"], False,
                                                                          getattr(args, "normalize", True), _variation, recent))
        problem = f"오류 {error[:60]}" if error else game_problem(row, persona, recent, scene, variation,
                                                                                  max_chars(args, scene) + LENGTH_TOLERANCE, reevaluate)
        if problem:
            rejected.append(f"{attempt}차 {problem}")
        if error or problem is None or attempt == attempts:
            break
        # 느린 기기를 흉내 낸다: 한 번 만드는 데 오래 걸렸으면 다시 만들지 않고 바로 기본 대사로 (--retry-within)
        if args.retry_within and metrics and metrics.get("wall_ms", 0) > args.retry_within * 1000:
            break
    row["attempts"] = attempt
    row["final"] = "SLM" if problem is None else "기본 대사"
    row["rejected"] = " / ".join(rejected)
    # 기본 대사로 대신했으면 게임이 띄울 기본 대사를 고른다. 대사 칸은 마지막 시도의 모델 대사 그대로 두고, 띄운 것은 따로 적는다
    row["fallback_line"] = (pick_fallback(content, scene, recent) or "") if problem else ""
    row["shown_line"] = row["fallback_line"] if problem else row.get("line", "")
    row["address_used"] = address_used(row["shown_line"], scene)
    row["address_ok"] = address_contradiction(row["shown_line"], scene) is None if row["shown_line"] else None
    if total and attempt > 1:
        row.update(metric_fields(total))
    # 반복안됨: 화면에 띄운 대사(SLM 이거나 기본 대사)가 그 도깨비의 최근 대사와 다른지. 최근 대사가 없으면 판단하지 않는다
    shown = row["shown_line"]
    same = find_similar(shown, recent) if shown and recent else None
    row["fresh_ok"] = None if not (shown and recent) else same is None
    if same:
        row["copy_hit"] = " / ".join(x for x in (row.get("copy_hit", ""), f"반복:{same[0]}") if x)
    return row


def cell(row, key):
    if key == "ending":
        ok = row.get("ending_ok")
        if ok is None:
            return ""
        return f"{row['_hint_label']} {'일치' if ok else '불일치'}"
    value = row.get(key, "")
    if isinstance(value, bool):
        return "O" if value else "X"
    return value


def summarize(rows):
    groups = defaultdict(list)
    for r in rows:
        groups[(r["model"], r["variant"], r["format"])].append(r)
    summary = []
    for (model, variant, fmt), rs in groups.items():
        n = len(rs)

        def rate(key):
            return f"{sum(1 for r in rs if r.get(key) is True) / n * 100:.0f}%"

        def share(key):
            judged = [r for r in rs if r.get(key) is not None]
            return f"{sum(1 for r in judged if r[key]) / len(judged) * 100:.0f}%" if judged else ""

        walls = [r["wall_ms"] for r in rs if isinstance(r.get("wall_ms"), (int, float))]
        speeds = [r["tok_per_s"] for r in rs if isinstance(r.get("tok_per_s"), (int, float))]
        summary.append({
            "모델": model, "방식": variant, "형식제약": "O" if fmt else "X", "건수": n,
            "JSON": rate("json_ok"), "행동OK": rate("action_ok"), "대상OK": rate("target_ok"),
            "기대행동": share("expected_ok"), "금지어없음": share("forbidden_ok"), "필수어포함": share("mention_ok"),
            "공통금칙어없음": share("bad_ok"),
            "예시안베낌": share("original_ok"),
            "반복안됨": share("fresh_ok"),
            "호칭OK": share("address_ok"),
            "SLM대사": f"{sum(1 for r in rs if r.get('final') == 'SLM') / n * 100:.0f}%",
            "첫시도통과": f"{sum(1 for r in rs if r.get('final') == 'SLM' and r.get('attempts') == 1) / n * 100:.0f}%",
            "시도(평균)": round(statistics.mean(r.get("attempts", 1) for r in rs), 2),
            "길이OK": rate("length_ok"), "문장자름": rate("cut"), "토큰한도끊김": rate("truncated"),
            # 마지막 시도의 대사에서 어미를 말투에 맞게 고친 비율
            "말투다듬음": f"{sum(1 for r in rs if r.get('speech_fixed')) / n * 100:.0f}%",
            "외국어없음": rate("foreign_ok"),
            "어미일치(참고)": share("ending_ok"),
            "응답ms(중앙값)": round(statistics.median(walls)) if walls else "",
            "초당토큰(평균)": round(statistics.mean(speeds), 1) if speeds else "",
            "생각토큰(평균)": round(statistics.mean(r.get("reasoning_tokens", 0) for r in rs), 1),
            "시스템합침": "O" if any(r.get("system_merged") for r in rs) else "X",
        })
    return summary


def action_distribution(rows, content):
    """장면마다 어떤 행동을 몇 번 골랐는지. 반복(--repeat)을 늘릴수록 성향이 잘 보인다."""
    by_id = {s["id"]: s for s in content["scenes"]}
    groups = defaultdict(list)
    for r in rows:
        groups[(r["model"], r["variant"], r["format"], r["scene"])].append(r)
    out = []
    for (model, variant, fmt, scene_id), rs in groups.items():
        scene = by_id[scene_id]
        counts = Counter(r["action"] if r["json_ok"] else "(실패)" for r in rs)
        out.append({
            "모델": model, "방식": variant, "형식제약": "O" if fmt else "X",
            "장면": scene_id, "도깨비": rs[0]["spirit"], "모드": scene["mode"], "선택지": rs[0]["input"],
            "기대행동": ", ".join(scene.get("expected", [])),
            "고른행동": ", ".join(f"{a} {n}" for a, n in counts.most_common()),
            "기대일치": f"{sum(1 for r in rs if r.get('expected_ok'))}/{len(rs)}",
            "장면메모": scene.get("note", ""),
        })
    return out


def write_dicts(path, rows):
    with open(path, "w", newline="", encoding="utf-8-sig") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def print_summary(summary):
    keys = list(summary[0])
    print("\n" + " | ".join(keys))
    for s in summary:
        print(" | ".join(str(s[k]) for k in keys))


def dry_run(args, content, scenes):
    for variant in args.variants:
        for scene in scenes:
            persona = content["personas"][scene["relicId"]]
            system, user, schema, situation = build_prompts(args, variant, scene, persona, content)
            print(f"===== {variant} / {scene['id']} ({persona['name']}) =====")
            print("[시스템]\n" + system + "\n\n[사용자]\n" + user)
            if args.format:
                print("\n[형식 제약]\n" + json.dumps(schema, ensure_ascii=False))
            if variant == "two_step":
                system2, user2 = build_line_prompt(args, scene, persona, situation, "<고른 행동>", "<고른 대상>")
                print("\n--- 둘째 단계 ---\n[시스템]\n" + system2 + "\n\n[사용자]\n" + user2)
            print()


def parse_args():
    p = argparse.ArgumentParser(description="잠깨비 도깨비 프롬프트 시험기 (Ollama / LM Studio)")
    p.add_argument("--backend", choices=list(BACKENDS), default="ollama", help="모델 서버. 기본 ollama")
    p.add_argument("--models", default="", help="쉼표로 구분한 모델 이름. 이름은 --list-models 로 확인")
    p.add_argument("--list-models", action="store_true", help="서버에서 쓸 수 있는 모델 이름을 보여 주고 끝낸다")
    p.add_argument("--variants", default=",".join(VARIANTS), help="돌릴 방식. 기본은 전부")
    p.add_argument("--scenes", default="", help="돌릴 장면 ID. 예: S01,S05. 기본은 전부")
    p.add_argument("--repeat", type=int, default=1, help="장면마다 반복 횟수")
    p.add_argument("--retry-within", type=float, default=0,
                   help="한 번 만드는 데 이 초보다 오래 걸렸으면 검사에 걸려도 다시 만들지 않는다 (게임의 SlmReactionGenerator.RetryOnlyWithin 은 1.5초). 0 이면 따지지 않는다")
    p.add_argument("--attempts", type=int, default=2,
                   help="출력 검사(자기 이름·어미 섞임·반복 등)에 걸리면 곁들일 거리를 바꿔 다시 만드는 횟수(처음 포함). "
                        "게임의 SlmReactionGenerator.MaxAttempts 와 같은 기본 2. 1 이면 다시 만들지 않는다")
    p.add_argument("--no-normalize", dest="normalize", action="store_false",
                   help="검사 전에 흔한 어미를 도깨비 말투로 고치는 단계(normalize_speech)를 끈다. 고치기 전과 비교할 때 쓴다")
    p.add_argument("--no-format", dest="format", action="store_false",
                   help="JSON 스키마 형식 제약을 끈다. 대신 프롬프트에 출력 형식 설명을 싣는다")
    p.add_argument("--temperature", type=float, default=0.7)
    p.add_argument("--seed", type=int, default=None, help="고정하면 반복마다 seed+반복번호를 쓴다")
    # JSON 뼈대 약 20토큰 + 40자 대사 약 25토큰. 넘치면 끊긴 대사를 끝난 문장까지만 쓴다
    p.add_argument("--num-predict", type=int, default=40, help="한 번에 생성할 최대 토큰 (게임의 LlmUnityOptions·LmStudioOptions.MaxTokens 와 같은 40)")
    p.add_argument("--line-chars", type=int, default=16, help="프롬프트에 적는 대사 길이 (장면의 lineChars 가 우선)")
    p.add_argument("--max-chars", type=int, default=40, help="길이OK 판정 기준 (장면의 maxChars 가 우선)")
    p.add_argument("--max-sentences", type=int, default=0,
                   help="프롬프트에 적는 문장 수 (장면의 maxSentences 가 우선). 기본 0 = 적지 않는다")
    p.add_argument("--think-off", action="store_true", help="Ollama 에서 생각 모드를 끈다 (LM Studio 는 기본으로 끈다)")
    p.add_argument("--allow-think", action="store_true", help="LM Studio 에서 생각 모드를 허용한다. 비교용")
    p.add_argument("--system-in-user", dest="system_mode", choices=["auto", "on", "off"], default="auto",
                   help="시스템 메시지를 사용자 메시지 앞에 붙여 보낸다. auto(기본)는 LM Studio 에서 모델마다 시스템 메시지가 "
                        "빠지는지 입력 토큰 수로 보고, 빠지면 붙인다 (EXAONE 4.0)")
    p.add_argument("--no-warmup", dest="warmup", action="store_false")
    p.add_argument("--host", default=None, help="서버 주소. 기본은 Ollama 11434, LM Studio 1234 포트")
    p.add_argument("--timeout", type=int, default=300)
    p.add_argument("--out", default=str(BASE / "results"), help="결과 CSV 폴더")
    p.add_argument("--dry-run", action="store_true", help="모델을 부르지 않고 프롬프트만 출력")
    args = p.parse_args()
    args.variants = [v.strip() for v in args.variants.split(",") if v.strip()]
    unknown = [v for v in args.variants if v not in VARIANTS]
    if unknown:
        p.error(f"모르는 방식: {', '.join(unknown)} (가능: {', '.join(VARIANTS)})")
    args.models = [m.strip() for m in args.models.split(",") if m.strip()]
    if not args.models and not (args.dry_run or args.list_models):
        p.error("--models 가 필요하다 (이름은 --list-models, 프롬프트만 보려면 --dry-run)")
    args.host = (args.host or BACKENDS[args.backend]["host"]).rstrip("/")
    args.system_in_user = set()  # 시스템 메시지를 사용자 메시지 앞에 붙여 보낼 모델 (main 이 채운다)
    return args


def connection_failed(args, e):
    name = BACKENDS[args.backend]["name"]
    hint = "Developer 탭에서 서버를 켰는지" if args.backend == "lmstudio" else "Ollama 가 실행 중인지"
    sys.exit(f"{name} 서버({args.host})에 연결하지 못했다: {e.reason}. {hint} 확인한다.")


def main():
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8", errors="replace")
    args = parse_args()
    content = load_content()
    problems = validate(content)
    if problems:
        print("데이터 오류:\n" + "\n".join(f"- {p}" for p in problems))
        sys.exit(1)
    scenes = content["scenes"]
    if args.scenes:
        wanted = {s.strip() for s in args.scenes.split(",")}
        scenes = [s for s in scenes if s["id"] in wanted]
    if args.list_models:
        try:
            names = list_models(args)
        except urllib.error.URLError as e:
            connection_failed(args, e)
        print("\n".join(names) if names else "모델이 없다. 먼저 모델을 내려받는다.")
        return

    fixed = [s["id"] for s in scenes if is_fixed(s)]
    if fixed:
        print(f"정해진 대사를 쓰는 장면(이야기 듣기)과 대사가 없는 장면(끌려갈 때)은 AI 를 부르지 않아 건너뛴다: {', '.join(fixed)}")
        scenes = [s for s in scenes if not is_fixed(s)]

    if args.dry_run:
        dry_run(args, content, scenes)
        return

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    result_path = out_dir / f"prompt_test_{stamp}.csv"
    summary_path = out_dir / f"prompt_test_{stamp}_summary.csv"
    actions_path = out_dir / f"prompt_test_{stamp}_actions.csv"

    total = len(args.models) * len(args.variants) * len(scenes) * args.repeat
    done = 0
    rows = []
    recent = defaultdict(list)
    # utf-8-sig: 엑셀에서 열어도 한글이 깨지지 않는다
    with open(result_path, "w", newline="", encoding="utf-8-sig") as f:
        writer = csv.writer(f)
        writer.writerow([header for _, header in FIELDS])
        for model in args.models:
            print(f"[{model}]")
            try:
                if args.warmup:
                    warm_up(args, model)
                choose_system_mode(args, model)
            except urllib.error.URLError as e:
                connection_failed(args, e)
            except BackendError as e:
                print(f"  건너뜀 — {e}")
                done += len(args.variants) * len(scenes) * args.repeat
                continue
            for variant in args.variants:
                for scene in scenes:
                    persona = content["personas"][scene["relicId"]]
                    for rep in range(1, args.repeat + 1):
                        done += 1
                        seed = None if args.seed is None else args.seed + rep - 1
                        # 게임의 RecentLines 처럼 그 시나리오(같은 도깨비의 같은 선택지)가 이 모델·방식에서 최근에 띄운 대사 10개
                        key = (model, variant, scene["relicId"], scene["input"])
                        row = run_enforced(args, content, model, variant, scene, persona, rep, seed, recent[key])
                        # 게임처럼 화면에 띄운 대사는 기본 대사여도 기억한다 (NpcRequestManager.RecentLinesOf)
                        if row["shown_line"]:
                            recent[key] = (recent[key] + [row["shown_line"]])[-RECENT_LINES:]
                        rows.append(row)
                        writer.writerow([cell(row, key) for key, _ in FIELDS])
                        f.flush()
                        if row["json_ok"]:
                            line = row["line"].replace("\n", " ")
                            shown = f"{row['action']}({row['animation'] or '후보 밖'}) {line}"
                            if row["attempts"] > 1 or row["final"] != "SLM":
                                shown += f"  [{row['attempts']}번 시도, {row['final']}: {row['rejected'][:50]}]"
                        elif row.get("error"):
                            shown = f"실패 {row['error'][:60]}"
                        elif not row["raw"] and row.get("reasoning_tokens"):
                            shown = f"대사 없음 — 생각에 {row['reasoning_tokens']}토큰을 쓰다 출력 한도에 걸림"
                        else:
                            shown = f"JSON 아님: {row['raw'][:40]}"
                        print(f"  [{done}/{total}] {variant} {scene['id']} #{rep} → {shown}")

    print(f"\n결과: {result_path}")
    if rows:
        summary = summarize(rows)
        write_dicts(summary_path, summary)
        write_dicts(actions_path, action_distribution(rows, content))
        print_summary(summary)
        print(f"요약: {summary_path}\n장면별 행동: {actions_path}")


if __name__ == "__main__":
    main()
