package com.example.jamkkaebi.relic.domain;

/**
 * 난이도 3단계. 관리 플레이에서 난이도는 <b>복구량</b>을 정한다({@code RestorePolicy}).
 */
public enum Difficulty {
    EASY,   // 쉬움 — 복구 없이 선명도 회복만. 선명할 때만 고를 수 있다
    NORMAL, // 보통 — 한 페이즈 복구. 최초 정화 고정 난이도
    HARD    // 어려움 — 한 판에 전체 복구
}
