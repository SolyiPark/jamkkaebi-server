package com.example.jamkkaebi.auth.dto.request;

import com.example.jamkkaebi.auth.domain.AuthProvider;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Pattern;

/**
 * 로그인 시작 요청.
 *
 * @param provider     로그인할 소셜 제공자
 * @param verifierHash 클라이언트가 만든 {@code verifier} 의 SHA-256 해시(Base64).
 *                     <b>원문을 보내면 안 된다</b> — 이 값은 브라우저 주소창을 지나므로, 원문이
 *                     흐르면 가로챈 쪽이 그대로 인계 코드를 교환할 수 있다.
 */
public record LoginStartRequest(
        @NotNull AuthProvider provider,

        /*
         * 형식을 여기서 못 박는 이유가 둘이다.
         *
         * 첫째, 이 값은 그대로 oauth_login_sessions.verifier_hash(VARCHAR 64)에 저장되는데, 저장
         * 시점이 인가 요청을 만드는 필터 안이라 공통 오류 응답을 태울 수 없다. 긴 문자열을 받아
         * 주면 로그인 시작은 200 을 주고, 브라우저가 인증을 시작하는 순간 500 이 난다 — 사용자는
         * 원인을 알 수 없는 빈 화면을 본다.
         *
         * 둘째, 형식이 맞지 않는 값은 대부분 클라이언트가 해시 대신 verifier 원문을 보낸 경우다.
         * 그 실수는 인계 구간의 탈취 방어를 통째로 무력화하므로 조용히 통과시키면 안 된다.
         */
        @NotBlank
        @Pattern(regexp = VERIFIER_HASH_PATTERN, message = "SHA-256 해시를 Base64 로 인코딩한 값이어야 합니다.")
        String verifierHash
) {

    /** {@code Base64(SHA-256(verifier))} — 32바이트라 패딩 한 자리를 포함해 언제나 44자다. */
    public static final String VERIFIER_HASH_PATTERN = "^[A-Za-z0-9+/]{43}=$";
}
