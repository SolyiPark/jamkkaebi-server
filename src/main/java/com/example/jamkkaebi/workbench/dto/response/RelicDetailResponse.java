package com.example.jamkkaebi.workbench.dto.response;

import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.Phase;
import com.example.jamkkaebi.relic.domain.RelicStatus;
import com.example.jamkkaebi.relic.dto.response.RelicCard;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.time.LocalDateTime;

/**
 * 내 유물·도깨비 상세. 작업대의 유물 상세, 도깨비 상세, 전시관에서 도깨비를 터치했을 때가 모두 이 응답을 쓴다.
 *
 * <p>진행 단계에 해당하지 않는 필드는 {@code null} 로 두어 직렬화에서 빠진다.
 *
 * @param currentPhase     최초 정화 진행도. 이미 완료했으면 {@code null}
 * @param careCount        관리 플레이 성공 횟수. 완료 전이면 {@code null}
 * @param clarity          지금 선명도. 완료 전이면 {@code null}
 * @param lastPurifiedAt   마지막으로 정화에 성공한 시각("마지막 정화로부터 n일째"). 완료 전이면 {@code null}
 * @param clarityChangesAt 이대로 두면 선명도가 한 칸 떨어지는 시각(선명 → 흐려짐, 흐려짐 → 재봉인).
 *                         완료 전이거나 이미 재봉인됐으면 {@code null}
 * @param resealAt         이대로 두면 재봉인되는 시각. 완료 전이거나 이미 재봉인됐으면 {@code null}
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record RelicDetailResponse(
        RelicCard relic,
        RelicStatus status,
        Phase currentPhase,
        Integer careCount,
        Clarity clarity,
        LocalDateTime lastPurifiedAt,
        LocalDateTime clarityChangesAt,
        LocalDateTime resealAt,
        boolean displayed,
        boolean hidden,
        NextPlayView nextPlay
) {
}
