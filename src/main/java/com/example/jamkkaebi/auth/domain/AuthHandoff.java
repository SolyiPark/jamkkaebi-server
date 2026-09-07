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
 * 브라우저에서 게임 앱으로 로그인 결과를 넘기는 <b>일회용 인계 코드</b>.
 *
 * <p>딥링크에 JWT 를 그대로 실으면 브라우저 기록에 남고, 안드로이드에서는 같은 커스텀 스킴을 등록한
 * 다른 앱이 가로챌 수 있다. 그래서 딥링크로는 이 코드만 흘리고, 실제 토큰은 HTTPS 본문으로 받는다.
 * 코드를 가로채도 {@code verifier} 원문이 없으면 교환에 실패한다.
 *
 * <p>수명은 60초이고 한 번 쓰면 끝난다({@code usedAt}).
 */
@Entity
@Table(name = "auth_handoffs")
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class AuthHandoff {

    @Id
    @Column(length = 64)
    private String code;

    @Column(name = "user_id", nullable = false)
    private Long userId;

    /** 로그인 세션에서 옮겨 온 {@code SHA-256(verifier)}. 교환 시 제시된 원문과 대조한다. */
    @Column(name = "verifier_hash", nullable = false, length = 64)
    private String verifierHash;

    @Column(name = "expires_at", nullable = false)
    private LocalDateTime expiresAt;

    /** 사용 시각. {@code null} 이 아니면 이미 교환된 코드다. */
    @Column(name = "used_at")
    private LocalDateTime usedAt;

    @Builder
    private AuthHandoff(String code, Long userId, String verifierHash, LocalDateTime expiresAt) {
        this.code = code;
        this.userId = userId;
        this.verifierHash = verifierHash;
        this.expiresAt = expiresAt;
    }

    public boolean isExpired(LocalDateTime now) {
        return !expiresAt.isAfter(now);
    }
}
