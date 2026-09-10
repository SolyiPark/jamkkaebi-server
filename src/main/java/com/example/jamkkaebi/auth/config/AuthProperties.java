package com.example.jamkkaebi.auth.config;

import org.springframework.boot.context.properties.ConfigurationProperties;

import java.time.Duration;

/**
 * 소셜 로그인 인계 흐름 설정.
 *
 * @param deepLinkUri     로그인을 마친 브라우저가 게임 앱으로 돌아올 커스텀 스킴
 * @param handoffTtl      일회용 인계 코드 수명
 * @param loginSessionTtl verifier 해시를 담아 두는 로그인 세션 수명
 */
@ConfigurationProperties(prefix = "app.auth")
public record AuthProperties(String deepLinkUri, Duration handoffTtl, Duration loginSessionTtl) {

    private static final Duration DEFAULT_HANDOFF_TTL = Duration.ofSeconds(60);
    private static final Duration DEFAULT_LOGIN_SESSION_TTL = Duration.ofMinutes(10);

    public AuthProperties {
        handoffTtl = handoffTtl == null ? DEFAULT_HANDOFF_TTL : handoffTtl;
        loginSessionTtl = loginSessionTtl == null ? DEFAULT_LOGIN_SESSION_TTL : loginSessionTtl;
    }
}
