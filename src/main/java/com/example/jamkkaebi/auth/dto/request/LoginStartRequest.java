package com.example.jamkkaebi.auth.dto.request;

import com.example.jamkkaebi.auth.domain.AuthProvider;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;

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
        @NotBlank String verifierHash
) {
}
