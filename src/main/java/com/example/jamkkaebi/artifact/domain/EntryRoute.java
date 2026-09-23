package com.example.jamkkaebi.artifact.domain;

/**
 * 정화에 들어온 경로. (최초 플레이 또는 재(관리)플레이)
 */
public enum EntryRoute {

    // 최초 정화 — 작업대에서 1→2→3페이즈 순서 고정, 난이도 {@code NORMAL} 고정.
    FIRST,

    // 관리 플레이 — 선명도가 페이즈를 정하고 난이도는 사용자가 고른다.
    CARE;

    // 완벽 정화 보너스 대상인가. (최초 정화에는 적용하지 않는다)
    public boolean allowsPerfectBonus() {
        return this == CARE;
    }

    public boolean selectableDifficulty() {
        return this == CARE;
    }
}
