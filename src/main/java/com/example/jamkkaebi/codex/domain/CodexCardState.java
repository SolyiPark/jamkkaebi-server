package com.example.jamkkaebi.codex.domain;

import com.example.jamkkaebi.relic.domain.UserRelic;

/**
 * 도감 카드 한 장의 상태.
 *
 * <p><b>저장하지 않는다.</b> 유물을 보유했는지와 완료했는지에서 매번 읽는다.
 *
 * <p>한 번 완료한 유물은 재봉인 되어도 {@link #FULL_COLOR} 로 남는다. 선명도와 각성 단계는
 * 카드 상태가 아니라 작은 배지로 따로 보여 준다.
 */
public enum CodexCardState {

    LOCKED,     // 미획득
    SILHOUETTE, // 작업대 진행 중
    FULL_COLOR; // 정령 수호까지 완료

    /**
     * @param owned 이 사용자의 진행 상태. 보유하지 않았으면 {@code null}
     */
    public static CodexCardState of(UserRelic owned) {
        if (owned == null) {
            return LOCKED;
        }
        return owned.isCompleted() ? FULL_COLOR : SILHOUETTE;
    }
}
