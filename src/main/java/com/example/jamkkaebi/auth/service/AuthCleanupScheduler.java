package com.example.jamkkaebi.auth.service;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;

/**
 * 다 쓴 로그인 세션과 인계 코드를 주기적으로 치운다.
 *
 * <p>둘 다 수명이 짧은데(10분·60초) 지우는 주체가 없으면 <b>만료된 행이 무한히 쌓인다</b>. 로그인
 * 시도마다 한 행씩 생기므로, 사용자가 늘수록 증가 속도도 같이 빨라진다. 만료된 행은 조회에서
 * 걸러지긴 하지만 그건 정확성 문제가 아니라 저장 공간·인덱스 문제다.
 *
 */
@Component
public class AuthCleanupScheduler {

    private static final Logger log = LoggerFactory.getLogger(AuthCleanupScheduler.class);

    /** 10분마다. 인계 코드 수명(60초)보다 훨씬 길어도 되는 이유는 만료된 코드가 이미 무효라서다. */
    private static final long INTERVAL_MS = 10 * 60 * 1000L;

    private final LoginSessionService loginSessionService;
    private final HandoffService handoffService;

    public AuthCleanupScheduler(LoginSessionService loginSessionService,
                                HandoffService handoffService) {
        this.loginSessionService = loginSessionService;
        this.handoffService = handoffService;
    }

    @Scheduled(fixedDelay = INTERVAL_MS, initialDelay = INTERVAL_MS)
    public void purgeExpired() {
        int sessions = loginSessionService.purgeExpired();
        int handoffs = handoffService.purgeExpired();
        if (sessions > 0 || handoffs > 0) {
            log.debug("만료된 로그인 세션 {}건, 인계 코드 {}건을 정리했습니다.", sessions, handoffs);
        }
    }
}
