package com.example.jamkkaebi.purification.dto.request;

import com.example.jamkkaebi.artifact.domain.Difficulty;
import jakarta.validation.constraints.NotNull;

/**
 * 정화 세션 시작 요청.
 */
public record PurificationStartRequest(

        @NotNull(message = "필수 값입니다.")
        Integer artifactId,

        Difficulty difficulty
) {
}
