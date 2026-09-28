package com.example.jamkkaebi.workbench.dto.response;

import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 건물 강화 결과.
 *
 * @param spent     소비한 시대의 결정. 멱등 재호출이면 {@code 0}
 * @param balance   강화 후 남은 이 시대의 결정
 * @param nextLevel 다음 레벨 정보. 최대 레벨이면 {@code null}
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record BuildingLevelUpResponse(
        int level,
        int spent,
        int balance,
        NextLevelView nextLevel
) {
}
