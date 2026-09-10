package com.example.jamkkaebi.auth.domain;

import com.example.jamkkaebi.global.common.BaseTimeEntity;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Index;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;
import lombok.AccessLevel;
import lombok.Builder;
import lombok.Getter;
import lombok.NoArgsConstructor;

import java.time.LocalDateTime;

/**
 * 발급된 Refresh Token 한 건.
 *
 * <p><b>원문이 아니라 해시만 저장한다</b> — DB 가 유출돼도 토큰을 그대로 쓸 수 없게 한다.
 *
 * <p>회전은 <b>기존 행을 폐기하고 새 행을 추가</b>하는 방식이다. 폐기된 행을 지우지 않고 남겨 두는
 * 것이 재사용 감지의 근거다 — 이미 폐기된 토큰이 다시 들어오면 그것은 정상 클라이언트가 아니라
 * 탈취를 뜻하므로, 그 사용자의 토큰을 전부 폐기한다.
 */
@Entity
@Table(
        name = "refresh_tokens",
        uniqueConstraints = @UniqueConstraint(
                name = "uk_refresh_tokens_token_hash", columnNames = "token_hash"),
        indexes = @Index(name = "idx_refresh_tokens_user", columnList = "user_id")
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class RefreshToken extends BaseTimeEntity {

    /** 기기 표시 값의 최대 길이. 요청 DTO 가 같은 값을 검증해 저장 단계에서 터지지 않게 한다. */
    public static final int DEVICE_LABEL_MAX_LENGTH = 100;

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "user_id", nullable = false)
    private Long userId;

    /** Refresh Token 원문의 SHA-256 해시(Base64). */
    @Column(name = "token_hash", nullable = false, length = 64)
    private String tokenHash;

    @Column(name = "expires_at", nullable = false)
    private LocalDateTime expiresAt;

    /** 폐기 시각. */
    @Column(name = "revoked_at")
    private LocalDateTime revokedAt;

    /** 어느 기기에서 발급했는지 표시하는 선택 값. 세션 목록·문제 추적용이다. */
    @Column(name = "device_label", length = DEVICE_LABEL_MAX_LENGTH)
    private String deviceLabel;

    @Builder
    private RefreshToken(Long userId, String tokenHash, LocalDateTime expiresAt, String deviceLabel) {
        this.userId = userId;
        this.tokenHash = tokenHash;
        this.expiresAt = expiresAt;
        this.deviceLabel = deviceLabel;
    }

    public boolean isRevoked() {
        return revokedAt != null;
    }

    public boolean isExpired(LocalDateTime now) {
        return !expiresAt.isAfter(now);
    }
}
