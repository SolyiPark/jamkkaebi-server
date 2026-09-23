package com.example.jamkkaebi.artifact.domain;

/**
 * 도깨비 각성 단계. 선명도가 떨어져도 내려가지 않는다.
 *
 * <p>게이지가 가득 차도 <b>자동으로 승급하지 않는다</b> — 작업대에서 사용자가 강화 버튼을 눌러야
 * 확정된다.
 */
public enum AwakeningStage {

    AWAKENED,       // 각성 — 정령 수호 최초 완료 시
    AWAKENED_PLUS,  // 각성+ — 어려움 난이도 해금
    FULLY_AWAKENED; // 완전각성

    public boolean isMax() {
        return this == FULLY_AWAKENED;
    }

    // 다음 단계. 완전각성이면 {@code null}
    public AwakeningStage next() {
        return isMax() ? null : values()[ordinal() + 1];
    }

    public boolean reached(AwakeningStage required) {
        return required == null || ordinal() >= required.ordinal();
    }
}
