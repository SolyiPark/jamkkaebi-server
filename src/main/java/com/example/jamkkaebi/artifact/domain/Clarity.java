package com.example.jamkkaebi.artifact.domain;

/**
 * 선명도. <b>저장하지 않고</b> 마지막 정화 시각에서 계산한다.
 *
 */
public enum Clarity {

    CLEAR,    // 선명 — 7일 이내
    FADED,    // 흐려짐 — 7일 이상
    RESEALED; // 재봉인 — 14일 이상

    // 선명도를 복구하기 위해 재플레이 해야 하는 페이즈
    public Phase restoreFrom() {
        return switch (this) {
            case CLEAR -> Phase.GUARD;
            case FADED -> Phase.RECALL;
            case RESEALED -> Phase.UNSEAL;
        };
    }
}
