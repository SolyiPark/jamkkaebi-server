package com.example.jamkkaebi.relic.domain;

/**
 * 선명도. <b>저장하지 않고</b> 선명도 기준 시각에서 계산한다.
 *
 * <p>관리 플레이로 복구 한 단계를 깰 때마다 한 칸 오른다(재봉인 → 흐려짐 → 선명).
 */
public enum Clarity {

    CLEAR,    // 선명 — 기준 시각에서 7일 미만
    FADED,    // 흐려짐 — 7일 이상
    RESEALED; // 재봉인 — 14일 이상

    // 이 선명도에서 관리 플레이로 하는 페이즈. 재봉인은 봉인 해제, 흐려짐은 기억 회복, 선명은 오염 관리
    public Phase restoreFrom() {
        return switch (this) {
            case CLEAR -> Phase.GUARD;
            case FADED -> Phase.RECALL;
            case RESEALED -> Phase.UNSEAL;
        };
    }

    // 복구 한 단계로 오르는 선명도. 재봉인 → 흐려짐 → 선명, 선명은 그대로
    public Clarity stepUp() {
        return this == RESEALED ? FADED : CLEAR;
    }
}
