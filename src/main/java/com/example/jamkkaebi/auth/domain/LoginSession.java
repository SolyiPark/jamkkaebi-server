package com.example.jamkkaebi.auth.domain;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import lombok.AccessLevel;
import lombok.Builder;
import lombok.Getter;
import lombok.NoArgsConstructor;

import java.time.LocalDateTime;

/**
 * 소셜 인증이 진행되는 동안 {@code state} 에 묶어 두는 로그인 세션.
 *
 * <p>클라이언트가 만든 {@code verifier} 의 <b>해시</b>를 들고 있다가, 로그인이 끝나 인계 코드를 만들 때
 * 그 코드에 옮겨 붙인다. 원문이 아니라 해시를 받는 이유는, 이 값이 브라우저 주소창을 지나기
 * 때문이다 — 원문이 흐르면 가로챈 쪽이 그대로 교환에 쓸 수 있다.
 *
 * <p>{@code state} 자체의 위·변조 검증은 Spring Security 가 한다. 이 행은 그 {@code state} 에
 * verifier 해시를 이어 붙이는 역할만 한다.
 */
@Entity
@Table(name = "oauth_login_sessions")
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class LoginSession {

    /** Spring Security 가 발급한 OAuth2 {@code state}. */
    @Id
    @Column(length = 128)
    private String state;

    /** {@code SHA-256(verifier)} 의 Base64 표현. */
    @Column(name = "verifier_hash", nullable = false, length = 64)
    private String verifierHash;

    @Column(name = "expires_at", nullable = false)
    private LocalDateTime expiresAt;

    @Builder
    private LoginSession(String state, String verifierHash, LocalDateTime expiresAt) {
        this.state = state;
        this.verifierHash = verifierHash;
        this.expiresAt = expiresAt;
    }

    public boolean isExpired(LocalDateTime now) {
        return !expiresAt.isAfter(now);
    }
}
