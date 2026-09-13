package com.example.jamkkaebi.artifact.domain;

/**
 * 정령 각성 단계. 한 번 오르면 선명도가 떨어져도 내려가지 않는다.
 */
public enum AwakeningStage {
    AWAKENED,       // 각성 — 정령 수호 최초 완료 시
    AWAKENED_PLUS,  // 각성+
    FULLY_AWAKENED  // 완전각성
}
