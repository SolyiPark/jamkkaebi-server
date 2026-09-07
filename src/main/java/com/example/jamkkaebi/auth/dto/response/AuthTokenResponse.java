package com.example.jamkkaebi.auth.dto.response;

/**
 * 발급된 잠깨비 토큰.
 *
 * <p>사용자 정보로는 <b>친구 코드와 닉네임만</b> 담는다. {@code userId} 와 {@code providerUserId} 는
 * 밖으로 내보내지 않는다.
 *
 * @param accessToken  API 호출용 Access Token (30분)
 * @param refreshToken 재발급용 Refresh Token (회전됨)
 * @param friendCode   외부에 노출하는 유일한 식별자
 * @param nickname     현재 닉네임
 */
public record AuthTokenResponse(
        String accessToken,
        String refreshToken,
        String friendCode,
        String nickname
) {
}
