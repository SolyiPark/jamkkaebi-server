package com.example.jamkkaebi.auth.service;

import com.example.jamkkaebi.auth.domain.RefreshToken;
import com.example.jamkkaebi.auth.repository.RefreshTokenRepository;
import com.example.jamkkaebi.common.policy.TokenHasher;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.Duration;
import java.time.LocalDateTime;
import java.time.ZoneId;
import java.util.Optional;

/**
 * Refresh Token 을 저장·회전·폐기한다.
 *
 * <p>원문이 아니라 SHA-256 해시만 저장한다. 회전은 <b>기존 행을 폐기하고 새 행을 추가</b>하는
 * 방식이며, 폐기된 행을 남겨 두는 것이 재사용 감지의 근거다.
 */
@Service
public class RefreshTokenService {

    private static final Logger log = LoggerFactory.getLogger(RefreshTokenService.class);
    private static final ZoneId KST = ZoneId.of("Asia/Seoul");
    /** 만료 후에도 재사용 감지를 위해 행을 남겨 두는 기간. */
    private static final Duration RETENTION_AFTER_EXPIRY = Duration.ofDays(7);

    private final RefreshTokenRepository refreshTokenRepository;

    public RefreshTokenService(RefreshTokenRepository refreshTokenRepository) {
        this.refreshTokenRepository = refreshTokenRepository;
    }

    /** 회전 시도 결과. */
    public enum RotationResult {
        /** 제시한 토큰이 살아 있어 폐기하고 새 토큰으로 넘겼다. */
        ROTATED,
        /** 저장된 적이 없거나 이미 만료된 토큰이다. */
        NOT_FOUND,
        /** 이미 폐기된 토큰이 다시 왔다. 탈취로 보고 이 사용자의 토큰을 전부 폐기했다. */
        REUSE_DETECTED
    }

    /** 새 Refresh Token 을 저장한다. (로그인 인계 교환 시) */
    @Transactional
    public void save(Long userId, String refreshToken, long ttlMs, String deviceLabel) {
        refreshTokenRepository.save(RefreshToken.builder()
                .userId(userId)
                .tokenHash(TokenHasher.sha256(refreshToken))
                .expiresAt(expiresAt(ttlMs))
                .deviceLabel(deviceLabel)
                .build());
    }

    /**
     * 제시된 토큰을 폐기하고 새 토큰으로 교체한다.
     *
     * <p>폐기는 조건부 UPDATE 한 문장이라, 같은 토큰으로 동시에 재발급하면 먼저 도착한 하나만
     * 성공한다. 실패했을 때만 이유를 확인하러 조회한다 — 성공 경로에 조회를 얹지 않기 위해서다.
     *
     * <p><b>이미 폐기된 토큰이 다시 오면</b> 정상 클라이언트가 아니다. 정상 클라이언트는 회전 직후
     * 옛 토큰을 버리기 때문이다. 유출로 보고 이 사용자의 토큰을 전부 폐기해 세션을 끊는다.
     *
     * @return 회전 결과. {@link RotationResult#ROTATED} 일 때만 새 토큰이 저장된다.
     */
    @Transactional
    public RotationResult rotate(Long userId, String currentRefreshToken,
                                 String newRefreshToken, long ttlMs, String deviceLabel) {
        LocalDateTime now = LocalDateTime.now(KST);
        String currentHash = TokenHasher.sha256(currentRefreshToken);

        if (refreshTokenRepository.revokeIfActive(currentHash, now) == 1) {
            save(userId, newRefreshToken, ttlMs, deviceLabel);
            return RotationResult.ROTATED;
        }

        Optional<RefreshToken> stored = refreshTokenRepository.findByTokenHash(currentHash);
        if (stored.isPresent() && stored.get().isRevoked()) {
            log.warn("폐기된 Refresh Token 이 다시 제시되어 이 사용자의 세션을 모두 끊었습니다. userId={}",
                    userId);
            refreshTokenRepository.revokeAllByUserId(stored.get().getUserId(), now);
            return RotationResult.REUSE_DETECTED;
        }
        return RotationResult.NOT_FOUND;
    }

    /** 이 사용자의 살아 있는 토큰을 모두 폐기한다. (로그아웃) */
    @Transactional
    public void revokeAll(Long userId) {
        refreshTokenRepository.revokeAllByUserId(userId, LocalDateTime.now(KST));
    }

    /**
     * 수명이 다한 지 오래된 행을 치운다.
     *
     * <p>이 테이블은 <b>로그인과 재발급마다 한 행씩 늘어난다.</b> 회전 방식이라 30분마다 한 번씩
     * 재발급하는 사용자 한 명이 하루에 48행을 남기고, 지우는 주체가 없으면 그대로 쌓인다.
     *
     * <p>만료 직후가 아니라 {@link #RETENTION_AFTER_EXPIRY} 만큼 기다렸다 지운다 — 폐기된 행이
     * 재사용 감지의 근거이므로, 경계에 걸친 토큰이 "저장된 적 없음"으로 둔갑하지 않게 여유를 둔다.
     */
    @Transactional
    public int purgeExpired() {
        return refreshTokenRepository.deleteExpiredBefore(
                LocalDateTime.now(KST).minus(RETENTION_AFTER_EXPIRY));
    }

    private LocalDateTime expiresAt(long ttlMs) {
        return LocalDateTime.now(KST).plusNanos(ttlMs * 1_000_000L);
    }
}
