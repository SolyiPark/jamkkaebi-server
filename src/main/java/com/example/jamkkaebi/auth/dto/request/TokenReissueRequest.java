package com.example.jamkkaebi.auth.dto.request;

import jakarta.validation.constraints.NotBlank;

/**
 * Access Token 재발급 요청.
 *
 * @param refreshToken 발급받은 Refresh Token
 * @param deviceLabel  어느 기기인지 표시할 선택 값
 */
public record TokenReissueRequest(
        @NotBlank String refreshToken,
        String deviceLabel
) {
}
