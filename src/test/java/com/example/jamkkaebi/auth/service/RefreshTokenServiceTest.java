package com.example.jamkkaebi.auth.service;

import com.example.jamkkaebi.auth.repository.RefreshTokenRepository;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;

import java.time.Duration;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * 회전과 재사용 감지를 검증한다.
 *
 * <p>회전형 토큰의 방어는 "한 번 쓰면 무효"와 "폐기된 토큰이 다시 오면 유출로 본다" 둘로 이뤄진다.
 * 앞은 정상 동작, 뒤는 사고 대응이라 둘 다 확인해야 한다.
 */
@SpringBootTest
class RefreshTokenServiceTest {

    private static final long TTL_MS = 60_000L;

    @Autowired
    private RefreshTokenService refreshTokenService;

    @Autowired
    private RefreshTokenRepository refreshTokenRepository;

    @AfterEach
    void clear() {
        refreshTokenRepository.deleteAll();
    }

    @Test
    @DisplayName("살아 있는 토큰을 제시하면 회전한다")
    void rotatesActiveToken() {
        refreshTokenService.save(1L, "token-a", TTL_MS, "device-1");

        assertThat(refreshTokenService.rotate(1L, "token-a", "token-b", TTL_MS, "device-1"))
                .isEqualTo(RefreshTokenService.RotationResult.ROTATED);

        // 옛 토큰은 폐기되고 새 토큰이 살아 있다.
        assertThat(refreshTokenRepository.findByTokenHash(hash("token-a")).orElseThrow().isRevoked())
                .isTrue();
        assertThat(refreshTokenService.rotate(1L, "token-b", "token-c", TTL_MS, "device-1"))
                .isEqualTo(RefreshTokenService.RotationResult.ROTATED);
    }

    @Test
    @DisplayName("이미 회전된 토큰이 다시 오면 유출로 보고 그 사용자의 세션을 전부 끊는다")
    void detectsReuseAndRevokesEverything() {
        refreshTokenService.save(1L, "token-a", TTL_MS, "phone");
        refreshTokenService.rotate(1L, "token-a", "token-b", TTL_MS, "phone");

        // 공격자가 가로챈 옛 토큰으로 재발급을 시도한다.
        assertThat(refreshTokenService.rotate(1L, "token-a", "token-x", TTL_MS, "attacker"))
                .isEqualTo(RefreshTokenService.RotationResult.REUSE_DETECTED);

        // 정상 사용자의 최신 토큰까지 함께 끊긴다 — 누가 진짜인지 알 수 없으므로 둘 다 재로그인시킨다.
        assertThat(refreshTokenService.rotate(1L, "token-b", "token-c", TTL_MS, "phone"))
                .isNotEqualTo(RefreshTokenService.RotationResult.ROTATED);
    }

    @Test
    @DisplayName("다른 사용자의 세션까지 끊지는 않는다")
    void reuseDetectionIsScopedToOneUser() {
        refreshTokenService.save(1L, "token-a", TTL_MS, "phone");
        refreshTokenService.rotate(1L, "token-a", "token-b", TTL_MS, "phone");
        refreshTokenService.save(2L, "other-token", TTL_MS, "phone");

        refreshTokenService.rotate(1L, "token-a", "token-x", TTL_MS, "attacker");

        assertThat(refreshTokenService.rotate(2L, "other-token", "other-next", TTL_MS, "phone"))
                .isEqualTo(RefreshTokenService.RotationResult.ROTATED);
    }

    @Test
    @DisplayName("저장된 적 없는 토큰은 재사용이 아니라 그냥 무효다")
    void reportsUnknownTokenAsNotFound() {
        assertThat(refreshTokenService.rotate(1L, "never-issued", "next", TTL_MS, null))
                .isEqualTo(RefreshTokenService.RotationResult.NOT_FOUND);
    }

    @Test
    @DisplayName("여러 기기에 발급해도 각 토큰이 따로 산다")
    void keepsPerDeviceTokens() {
        refreshTokenService.save(1L, "phone-token", TTL_MS, "phone");
        refreshTokenService.save(1L, "tablet-token", TTL_MS, "tablet");

        assertThat(refreshTokenService.rotate(1L, "phone-token", "phone-next", TTL_MS, "phone"))
                .isEqualTo(RefreshTokenService.RotationResult.ROTATED);
        assertThat(refreshTokenService.rotate(1L, "tablet-token", "tablet-next", TTL_MS, "tablet"))
                .isEqualTo(RefreshTokenService.RotationResult.ROTATED);
    }

    @Test
    @DisplayName("로그아웃하면 그 사용자의 토큰이 모두 폐기된다")
    void revokeAllOnLogout() {
        refreshTokenService.save(1L, "phone-token", TTL_MS, "phone");
        refreshTokenService.save(1L, "tablet-token", TTL_MS, "tablet");

        refreshTokenService.revokeAll(1L);

        assertThat(refreshTokenService.rotate(1L, "phone-token", "next", TTL_MS, "phone"))
                .isNotEqualTo(RefreshTokenService.RotationResult.ROTATED);
        assertThat(refreshTokenService.rotate(1L, "tablet-token", "next", TTL_MS, "tablet"))
                .isNotEqualTo(RefreshTokenService.RotationResult.ROTATED);
    }

    @Test
    @DisplayName("만료된 지 오래된 토큰만 지운다 — 재사용 감지의 근거를 먼저 버리지 않는다")
    void purgesOnlyLongExpiredTokens() {
        // 만료된 지 8일 지난 토큰. 그 시점엔 토큰 자체가 이미 무효라 남겨 둘 이유가 없다.
        refreshTokenService.save(1L, "long-gone", -Duration.ofDays(8).toMillis(), "phone");
        // 방금 회전돼 폐기됐지만 원래 수명은 아직 남은 토큰. 이 행이 재사용 감지의 근거다.
        refreshTokenService.save(1L, "just-rotated", TTL_MS, "phone");
        refreshTokenService.rotate(1L, "just-rotated", "current", TTL_MS, "phone");

        assertThat(refreshTokenService.purgeExpired()).isEqualTo(1);

        assertThat(refreshTokenRepository.findByTokenHash(hash("long-gone"))).isEmpty();
        // 폐기됐지만 남아 있어야 유출을 알아챌 수 있다.
        assertThat(refreshTokenService.rotate(1L, "just-rotated", "next", TTL_MS, "phone"))
                .isEqualTo(RefreshTokenService.RotationResult.REUSE_DETECTED);
    }

    private String hash(String token) {
        return com.example.jamkkaebi.common.policy.TokenHasher.sha256(token);
    }
}
