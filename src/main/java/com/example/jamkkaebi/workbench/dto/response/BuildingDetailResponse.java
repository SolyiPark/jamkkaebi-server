package com.example.jamkkaebi.workbench.dto.response;

import com.example.jamkkaebi.artifact.domain.Era;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.time.LocalDateTime;

/**
 * 건물 상세 — 작업대의 건물 상세와 전시관 건물 터치 패널.
 *
 * @param story     완공 자막 · 실화 나레이션
 * @param balance   보유한 이 시대의 결정
 * @param nextLevel 다음 레벨 정보. 최대 레벨이면 {@code null}
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record BuildingDetailResponse(
        Integer buildingId,
        Era era,
        String name,
        int level,
        int maxLevel,
        String story,
        LocalDateTime acquiredAt,
        boolean displayed,
        int balance,
        NextLevelView nextLevel
) {
}
