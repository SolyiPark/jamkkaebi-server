package com.example.jamkkaebi.auth.dto.request;

import jakarta.validation.constraints.NotBlank;

/**
 * 인계 코드 교환 요청.
 *
 * @param handoff     딥링크 {@code jamkkaebi://auth?code=} 로 받은 일회용 코드
 * @param verifier    로그인 시작 때 해시만 보냈던 <b>원문</b>. 이 값이 있어야 코드가 내 것임이 증명된다.
 * @param deviceLabel 어느 기기에서 로그인했는지 표시할 선택 값
 */
public record TokenExchangeRequest(
        @NotBlank String handoff,
        @NotBlank String verifier,
        String deviceLabel
) {
}
