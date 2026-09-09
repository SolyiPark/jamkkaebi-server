package com.example.jamkkaebi.auth.dto.request;

import com.example.jamkkaebi.auth.domain.RefreshToken;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;

/**
 * Access Token 재발급 요청.
 *
 * @param refreshToken 발급받은 Refresh Token
 * @param deviceLabel  어느 기기인지 표시할 선택 값
 */
public record TokenReissueRequest(
        @NotBlank String refreshToken,

        // 회전으로 새로 저장되는 행에 그대로 실린다. 길이는 교환 요청과 같은 기준이다.
        @Size(max = RefreshToken.DEVICE_LABEL_MAX_LENGTH)
        String deviceLabel
) {
}
