package com.example.jamkkaebi.exhibition.dto.request;

import jakarta.validation.constraints.NotNull;

/**
 * 도깨비 숨기기 / 보이기.
 *
 * @param hidden {@code true} 면 숨긴다.
 */
public record SpiritVisibilityRequest(

        @NotNull(message = "필수 값입니다.")
        Boolean hidden
) {
}
