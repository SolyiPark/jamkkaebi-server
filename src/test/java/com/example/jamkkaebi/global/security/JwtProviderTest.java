package com.example.jamkkaebi.global.security;

import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

class JwtProviderTest {

    private static final String SECRET = "jamkkaebi-test-secret-key-please-change-me-32bytes";
    private static final String FRIEND_CODE = "A7K2M9QZ";

    private final JwtProvider jwtProvider =
            new JwtProvider(new JwtProperties(SECRET, 1_800_000L, 2_592_000_000L));

    @Test
    @DisplayName("Access Token 의 subject 는 친구 코드다 — 내부 PK 를 싣지 않는다")
    void carriesFriendCodeAsSubject() {
        String token = jwtProvider.createAccessToken(FRIEND_CODE);

        assertThat(jwtProvider.parseFriendCode(token, TokenType.ACCESS)).isEqualTo(FRIEND_CODE);
    }

    @Test
    @DisplayName("Refresh Token 을 Bearer 로 쓰면 거부한다")
    void rejectsRefreshTokenUsedAsAccessToken() {
        String refreshToken = jwtProvider.createRefreshToken(FRIEND_CODE);

        assertThatThrownBy(() -> jwtProvider.parseFriendCode(refreshToken, TokenType.ACCESS))
                .isInstanceOf(BusinessException.class)
                .extracting(exception -> ((BusinessException) exception).getErrorCode())
                .isEqualTo(ErrorCode.INVALID_TOKEN);
    }

    @Test
    @DisplayName("다른 키로 서명된 토큰은 위조로 본다")
    void rejectsTokenSignedWithAnotherKey() {
        JwtProvider attacker = new JwtProvider(
                new JwtProperties("attacker-secret-key-that-is-long-enough-32", 1_800_000L, 1L));
        String forged = attacker.createAccessToken(FRIEND_CODE);

        assertThatThrownBy(() -> jwtProvider.parseFriendCode(forged, TokenType.ACCESS))
                .isInstanceOf(BusinessException.class)
                .extracting(exception -> ((BusinessException) exception).getErrorCode())
                .isEqualTo(ErrorCode.INVALID_TOKEN);
    }

    @Test
    @DisplayName("만료된 토큰은 A004 로 구분해 준다 — 클라이언트가 재발급과 재로그인을 나눈다")
    void reportsExpiryDistinctly() {
        JwtProvider shortLived = new JwtProvider(new JwtProperties(SECRET, -1L, -1L));
        String expired = shortLived.createAccessToken(FRIEND_CODE);

        assertThatThrownBy(() -> jwtProvider.parseFriendCode(expired, TokenType.ACCESS))
                .isInstanceOf(BusinessException.class)
                .extracting(exception -> ((BusinessException) exception).getErrorCode())
                .isEqualTo(ErrorCode.EXPIRED_TOKEN);
    }

    @Test
    @DisplayName("서명 키가 32바이트 미만이면 기동을 실패시킨다")
    void rejectsShortSecret() {
        assertThatThrownBy(() -> new JwtProvider(new JwtProperties("too-short", 1L, 1L)))
                .isInstanceOf(IllegalStateException.class);
    }
}
