package com.example.jamkkaebi.workbench.dto.request;

import com.example.jamkkaebi.artifact.domain.BuildingMaster;
import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotNull;

/**
 * 건물 강화.
 *
 * @param targetLevel 올리려는 레벨. <b>연타나 재시도로 결정이 두 번 빠지는 사고를 막는다</b>
 */
public record BuildingLevelUpRequest(

        @NotNull(message = "필수 값입니다.")
        @Min(value = 2, message = "2 이상이어야 합니다.")
        @Max(value = BuildingMaster.MAX_LEVEL, message = "최대 레벨을 넘을 수 없습니다.")
        Integer targetLevel
) {
}
