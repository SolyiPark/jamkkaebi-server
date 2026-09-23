package com.example.jamkkaebi.purification.service;

import com.example.jamkkaebi.global.time.GameClock;
import com.example.jamkkaebi.purification.repository.PlaySessionRepository;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Transactional;

/**
 * 보고되지 않은 채 만료된 정화 세션을 무효로 돌린다.
 * <p>행을 지우지는 않는다 — 어느 페이즈·난이도에서 이탈했는지는 밸런싱 데이터다.
 */
@Component
public class PurificationCleanupScheduler {

    private static final Logger log = LoggerFactory.getLogger(PurificationCleanupScheduler.class);

    private static final long INTERVAL_MS = 30 * 60 * 1000L;

    private final PlaySessionRepository sessionRepository;
    private final GameClock gameClock;

    public PurificationCleanupScheduler(PlaySessionRepository sessionRepository, GameClock gameClock) {
        this.sessionRepository = sessionRepository;
        this.gameClock = gameClock;
    }

    @Scheduled(fixedDelay = INTERVAL_MS, initialDelay = INTERVAL_MS)
    @Transactional
    public void expireStaleSessions() {
        int expired = sessionRepository.expireIssuedBefore(gameClock.now());
        if (expired > 0) {
            log.debug("만료된 정화 세션 {}건을 무효 처리했습니다.", expired);
        }
    }
}
