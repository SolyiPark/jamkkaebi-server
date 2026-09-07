package com.example.jamkkaebi.global.security;

import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import io.jsonwebtoken.Claims;
import io.jsonwebtoken.ExpiredJwtException;
import io.jsonwebtoken.JwtException;
import io.jsonwebtoken.Jwts;
import io.jsonwebtoken.security.Keys;
import org.springframework.stereotype.Component;

import javax.crypto.SecretKey;
import java.nio.charset.StandardCharsets;
import java.util.Date;
import java.util.UUID;

/**
 * 자체 JWT(Access/Refresh)를 발급하고 검증한다.
 *
 * <p><b>subject 에는 친구 코드를 담는다.</b> JWT payload 는 서명일 뿐 암호화가 아니라 누구나 열어
 * 볼 수 있다. 여기에 {@code userId} 를 넣으면 순번이 그대로 새어 가입 수·가입 순서가 드러나고,
 * {@code providerUserId} 를 넣으면 소셜 회원번호가 노출된다. 밖으로 나가는 식별자는 친구 코드
 * 하나뿐이라는 원칙이 토큰에도 똑같이 적용된다.
 */
@Component
public class JwtProvider {

    private static final String TOKEN_TYPE_CLAIM = "type";
    /** HMAC-SHA256 서명에 필요한 최소 키 길이(바이트). */
    private static final int MIN_SECRET_BYTES = 32;

    private final SecretKey secretKey;
    private final long accessTokenValidityMs;
    private final long refreshTokenValidityMs;

    public JwtProvider(JwtProperties properties) {
        byte[] secret = properties.secret() == null
                ? new byte[0]
                : properties.secret().getBytes(StandardCharsets.UTF_8);
        if (secret.length < MIN_SECRET_BYTES) {
            // 짧은 키는 서명이 사실상 추측 가능하다. 기동 시점에 실패시켜 약한 키로 뜬 서버가
            // 조용히 위조 가능한 토큰을 발급하는 상황을 막는다.
            throw new IllegalStateException(
                    "jwt.secret 은 32바이트(256bit) 이상이어야 합니다. 현재: " + secret.length + "바이트");
        }
        this.secretKey = Keys.hmacShaKeyFor(secret);
        this.accessTokenValidityMs = properties.accessTokenValidityMs();
        this.refreshTokenValidityMs = properties.refreshTokenValidityMs();
    }

    public String createAccessToken(String friendCode) {
        return createToken(friendCode, accessTokenValidityMs, TokenType.ACCESS);
    }

    public String createRefreshToken(String friendCode) {
        return createToken(friendCode, refreshTokenValidityMs, TokenType.REFRESH);
    }

    /**
     * 토큰을 검증하고 subject 에 담긴 친구 코드를 반환한다.
     *
     * <p>{@code type} 클레임이 {@code expectedType} 과 일치하는지도 확인한다 — 수명이 긴 Refresh
     * Token 을 Bearer 로 들고 다니는 것을 막기 위해서다.
     *
     * @throws BusinessException 만료(A004), 서명·형식 오류 또는 용도 불일치(A003)
     */
    public String parseFriendCode(String token, TokenType expectedType) {
        Claims claims = parseClaims(token);
        if (!expectedType.name().equals(claims.get(TOKEN_TYPE_CLAIM, String.class))) {
            throw new BusinessException(ErrorCode.INVALID_TOKEN);
        }
        String friendCode = claims.getSubject();
        if (friendCode == null || friendCode.isBlank()) {
            throw new BusinessException(ErrorCode.INVALID_TOKEN);
        }
        return friendCode;
    }

    public long getRefreshTokenValidityMs() {
        return refreshTokenValidityMs;
    }

    /**
     * 토큰을 만든다. <b>매번 다른 {@code jti}(토큰 고유 id)를 넣는다.</b>
     *
     * <p>없으면 같은 사용자에게 <b>같은 초에</b> 발급한 토큰이 바이트 단위로 동일해진다 —
     * subject·용도·발급시각·만료시각이 모두 같고, 시각의 정밀도가 초 단위이기 때문이다. Refresh
     * Token 은 해시를 유니크 제약으로 저장하므로, 로그인 직후 바로 재발급하거나 두 기기에서 동시에
     * 로그인하면 그 제약에 걸려 발급이 실패한다.
     */
    private String createToken(String friendCode, long validityMs, TokenType type) {
        Date now = new Date();
        return Jwts.builder()
                .id(UUID.randomUUID().toString())
                .subject(friendCode)
                .claim(TOKEN_TYPE_CLAIM, type.name())
                .issuedAt(now)
                .expiration(new Date(now.getTime() + validityMs))
                .signWith(secretKey)
                .compact();
    }

    private Claims parseClaims(String token) {
        try {
            return Jwts.parser()
                    .verifyWith(secretKey)
                    .build()
                    .parseSignedClaims(token)
                    .getPayload();
        } catch (ExpiredJwtException e) {
            throw new BusinessException(ErrorCode.EXPIRED_TOKEN);
        } catch (JwtException | IllegalArgumentException e) {
            throw new BusinessException(ErrorCode.INVALID_TOKEN);
        }
    }
}
