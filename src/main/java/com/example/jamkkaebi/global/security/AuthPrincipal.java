package com.example.jamkkaebi.global.security;

/**
 * 인증된 사용자를 나타내는 principal.
 *
 * <p>토큰에 들어 있는 것이 친구 코드뿐이라 여기에도 친구 코드만 담는다. 내부 PK 가 필요한 서비스는
 * 친구 코드로 사용자를 조회한다 — 유니크 인덱스 조회 한 번이면 되고, 그 대가로 내부 순번이 토큰에
 * 실려 나가지 않는다.
 *
 * @param friendCode 인증된 사용자의 친구 코드
 */
public record AuthPrincipal(String friendCode) {
}
