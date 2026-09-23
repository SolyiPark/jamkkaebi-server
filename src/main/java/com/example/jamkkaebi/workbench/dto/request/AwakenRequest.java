package com.example.jamkkaebi.workbench.dto.request;

import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import jakarta.validation.constraints.NotNull;

/**
 * 도깨비 강화 확정.
 *
 * @param targetStage 확정하려는 단계. <b>연타나 재시도로 두 단계가 오르는 사고를 막는 조건값</b>이다
 */
public record AwakenRequest(

        @NotNull(message = "필수 값입니다.")
        AwakeningStage targetStage
) {
}
