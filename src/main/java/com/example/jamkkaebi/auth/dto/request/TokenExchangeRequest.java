package com.example.jamkkaebi.auth.dto.request;

import com.example.jamkkaebi.auth.domain.RefreshToken;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;

/**
 * 인계 코드 교환 요청.
 *
 * @param handoff     딥링크 {@code jamkkaebi://auth?code=} 로 받은 일회용 코드
 * @param verifier    로그인 시작 때 해시만 보냈던 <b>원문</b>. 이 값이 있어야 코드가 내 것임이 증명된다.
 * @param deviceLabel 어느 기기에서 로그인했는지 표시할 선택 값
 */
public record TokenExchangeRequest(
        @NotBlank String handoff,

        /*
         * 하한을 두는 이유 — verifier 는 딥링크를 가로챈 쪽이 60초 안에 맞혀서는 안 되는 값이다.
         * 짧으면 그 전제가 깨진다. PKCE(RFC 7636)의 code_verifier 기준(43~128자)에 맞춰 잡되,
         * 상한은 해시가 아니라 원문이 오가는 값이라 본문 크기를 제한하는 의미로 둔다.
         */
        @NotBlank
        @Size(min = VERIFIER_MIN_LENGTH, max = VERIFIER_MAX_LENGTH)
        String verifier,

        // 그대로 refresh_tokens.device_label 에 저장된다. 길이를 막지 않으면 저장 단계에서 터져
        // 로그인 전체가 실패한다 — 표시용 값 하나 때문에.
        @Size(max = RefreshToken.DEVICE_LABEL_MAX_LENGTH)
        String deviceLabel
) {

    public static final int VERIFIER_MIN_LENGTH = 32;
    public static final int VERIFIER_MAX_LENGTH = 128;
}
