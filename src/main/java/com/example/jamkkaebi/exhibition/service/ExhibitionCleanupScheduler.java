package com.example.jamkkaebi.exhibition.service;

import com.example.jamkkaebi.exhibition.repository.ExhibitionVisitRepository;
import com.example.jamkkaebi.global.time.GameClock;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Transactional;

/**
 * 30일이 지난 행을 정리한다.
 */
@Component
public class ExhibitionCleanupScheduler {

    private static final Logger log = LoggerFactory.getLogger(ExhibitionCleanupScheduler.class);

    private static final long INTERVAL_MS = 60 * 60 * 1000L;

    private final ExhibitionVisitRepository exhibitionVisitRepository;
    private final GameClock gameClock;

    public ExhibitionCleanupScheduler(ExhibitionVisitRepository exhibitionVisitRepository, GameClock gameClock) {
        this.exhibitionVisitRepository = exhibitionVisitRepository;
        this.gameClock = gameClock;
    }

    @Scheduled(fixedDelay = INTERVAL_MS, initialDelay = INTERVAL_MS)
    @Transactional
    public void purgeExpired() {
        int visits = exhibitionVisitRepository.deleteOlderThan(
                gameClock.today().minusDays(ExhibitionService.RECENT_VISIT_DAYS - 1));
        if (visits > 0) {
            log.debug("지난 내 전시관 방문 기록 {}건을 정리했습니다.", visits);
        }
    }
}
