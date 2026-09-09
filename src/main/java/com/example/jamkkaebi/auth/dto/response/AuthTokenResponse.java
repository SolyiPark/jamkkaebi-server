package com.example.jamkkaebi.auth.dto.response;

/**
 * 발급된 잠깨비 토큰.
 *
 * <p>사용자 정보로는 <b>친구 코드와 닉네임만</b> 담는다. {@code userId} 와 {@code providerUserId} 는
 * 밖으로 내보내지 않는다.
 *
 * @param accessToken           API 호출용 Access Token
 * @param refreshToken          재발급용 Refresh Token (회전됨)
 * @param accessTokenExpiresIn  Access Token 이 만료되기까지 남은 시간(초).
 *                              클라이언트가 30분을 상수로 박아 두면 서버가 수명을 조정할 때마다
 *                              앱을 다시 배포해야 하므로 발급할 때 함께 알려 준다.
 * @param friendCode            외부에 노출하는 유일한 식별자
 * @param nickname              현재 닉네임
 * @param newUser               이번 로그인에서 <b>새로 가입한</b> 계정인지.
 *                              참이면 클라이언트가 닉네임 수정 화면을 띄운다 — 소셜 닉네임은
 *                              절단·보정을 거친 값이라 사용자가 고른 이름이 아니기 때문이다.
 *                              재발급 응답에서는 언제나 거짓이다.
 */
public record AuthTokenResponse(
        String accessToken,
        String refreshToken,
        long accessTokenExpiresIn,
        String friendCode,
        String nickname,
        boolean newUser
) {
}
